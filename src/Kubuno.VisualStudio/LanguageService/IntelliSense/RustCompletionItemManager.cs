using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
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

        [ImportMany]
        internal IEnumerable<Lazy<IAsyncCompletionItemManagerProvider, IDictionary<string, object>>> Providers { get; set; } = null!;

        public IAsyncCompletionItemManager? GetOrCreate(ITextView textView)
        {
            var fallback = Providers
                .Where(p => p.Metadata.TryGetValue("Name", out var name) && name as string == DefaultManagerName)
                .Select(p => p.Value)
                .FirstOrDefault();
            var inner = fallback?.GetOrCreate(textView);
            return inner is null ? null : textView.Properties.GetOrCreateSingletonProperty(() => new RustCompletionItemManager(inner));
        }
    }

    internal sealed class RustCompletionItemManager : IAsyncCompletionItemManager2
    {
        private readonly IAsyncCompletionItemManager _inner;

        public RustCompletionItemManager(IAsyncCompletionItemManager inner)
        {
            _inner = inner;
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
                return Task.FromResult<FilteredCompletionModel?>(null);
            }

            return _inner.UpdateCompletionListAsync(session, data, token)!;
        }

        private async Task<CompletionList<CompletionItem>> SortAsListAsync(IAsyncCompletionSession session, AsyncCompletionSessionInitialDataSnapshot data, CancellationToken token)
        {
            var sorted = await _inner.SortCompletionListAsync(session, data, token).ConfigureAwait(false);
            return session.CreateCompletionList(sorted);
        }
    }
}
