using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace Kubuno.VisualStudio.LanguageService.IntelliSense
{
    /// <summary>
    /// The completion list's filtering for Rust: Visual Studio's own (fuzzy matching, bold matched characters, filter
    /// buttons), plus what a C# list does that a generic one does not - it closes as soon as the text being completed
    /// stops being a name (a space, <c>=</c>, <c>;</c>... typed without committing), instead of lingering over the
    /// previous items.
    /// </summary>
    [Export(typeof(IAsyncCompletionItemManagerProvider))]
    [Name("Kubuno Rust Completion Item Manager")]
    [ContentType(Constants.RustContentType)]
    internal sealed class RustCompletionItemManagerProvider : IAsyncCompletionItemManagerProvider
    {
        private const string DefaultManagerName = "DefaultCompletionItemManager";

        [Import]
        internal Lazy<IAsyncCompletionBroker> Broker { get; set; } = null!;

        [ImportMany]
        internal IEnumerable<Lazy<IAsyncCompletionItemManagerProvider, IDictionary<string, object>>> Providers { get; set; } = null!;

        public IAsyncCompletionItemManager? GetOrCreate(ITextView textView)
        {
            var fallback = Providers
                .Where(p => p.Metadata.TryGetValue("Name", out var name) && name as string == DefaultManagerName)
                .Select(p => p.Value)
                .FirstOrDefault();
            var inner = fallback?.GetOrCreate(textView);
            return inner is null ? null : textView.Properties.GetOrCreateSingletonProperty(() => new RustCompletionItemManager(inner, Broker.Value, textView));
        }
    }

    internal sealed class RustCompletionItemManager : IAsyncCompletionItemManager2
    {
        private readonly IAsyncCompletionItemManager _inner;
        private readonly IAsyncCompletionBroker _broker;
        private readonly ITextView _textView;

        public RustCompletionItemManager(IAsyncCompletionItemManager inner, IAsyncCompletionBroker broker, ITextView textView)
        {
            _inner = inner;
            _broker = broker;
            _textView = textView;
        }

        public Task<ImmutableArray<CompletionItem>> SortCompletionListAsync(IAsyncCompletionSession session, AsyncCompletionSessionInitialDataSnapshot data, CancellationToken token) =>
            _inner.SortCompletionListAsync(session, data, token);

        public Task<CompletionList<CompletionItem>> SortCompletionItemListAsync(IAsyncCompletionSession session, AsyncCompletionSessionInitialDataSnapshot data, CancellationToken token) =>
            _inner is IAsyncCompletionItemManager2 inner2
                ? inner2.SortCompletionItemListAsync(session, data, token)
                : SortAsListAsync(session, data, token);

        public Task<FilteredCompletionModel?> UpdateCompletionListAsync(IAsyncCompletionSession session, AsyncCompletionSessionDataSnapshot data, CancellationToken token)
        {
            var typed = session.ApplicableToSpan.GetSpan(data.Snapshot).GetText();
            RustCompletionSource.Dbg($"Update typed='{typed}' reason={data.Trigger.Reason} items={data.InitialSortedItemList.Count}");
            if (typed.Any(c => !(char.IsLetterOrDigit(c) || c == '_')))
            {
                // Like C#: a name is no longer being typed (space, `=`, `;`...), the list goes away.
                // Typed fast, `Vec::` / `v.` reaches the still-open identifier session as one update, which would leave
                // no session at all: reopen the list right after this one goes away (C# opens it on `.` at once).
                bool memberOrPath = typed.EndsWith("::", StringComparison.Ordinal) || (typed.EndsWith(".", StringComparison.Ordinal) && !typed.EndsWith("..", StringComparison.Ordinal));
                if (memberOrPath && data.Trigger.Reason == CompletionTriggerReason.Insertion)
                {
                    ScheduleRetrigger(session.ApplicableToSpan.GetEndPoint(data.Snapshot), typed[typed.Length - 1]);
                }

                return Task.FromResult<FilteredCompletionModel?>(null);
            }

            return _inner.UpdateCompletionListAsync(session, data, token)!;
        }

        private void ScheduleRetrigger(SnapshotPoint end, char character)
        {
#pragma warning disable VSSDK007 // fire-and-forget from the completion pipeline; FileAndForget reports any fault to the "Kubuno" pane.
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                await Task.Delay(40);
                for (int attempt = 0; attempt < 10 && _broker.IsCompletionActive(_textView); attempt++)
                {
                    await Task.Delay(30);
                }

                var buffer = end.Snapshot.TextBuffer;
                var current = buffer.CurrentSnapshot;
                var point = end.TranslateTo(current, PointTrackingMode.Positive);
                var caret = _textView.Caret.Position.BufferPosition;
                if (_broker.IsCompletionActive(_textView) || caret.Snapshot.TextBuffer != buffer || caret.Position != point.Position
                    || point.Position == 0 || current[point.Position - 1] != character)
                {
                    return;
                }

                RustCompletionSource.Dbg($"Retrigger after '{character}' at {point.Position}");
                var session = _broker.TriggerCompletion(_textView, new CompletionTrigger(CompletionTriggerReason.Insertion, current, character), point, CancellationToken.None);
                // The broker creates the session but only starts computing it when asked.
                if (session != null)
                {
                    session.OpenOrUpdate(new CompletionTrigger(CompletionTriggerReason.Insertion, current, character), point, CancellationToken.None);
                }
            }).FileAndForget("Kubuno/RustCompletionRetrigger");
#pragma warning restore VSSDK007
        }

        private async Task<CompletionList<CompletionItem>> SortAsListAsync(IAsyncCompletionSession session, AsyncCompletionSessionInitialDataSnapshot data, CancellationToken token)
        {
            var sorted = await _inner.SortCompletionListAsync(session, data, token).ConfigureAwait(false);
            return session.CreateCompletionList(sorted);
        }
    }
}
