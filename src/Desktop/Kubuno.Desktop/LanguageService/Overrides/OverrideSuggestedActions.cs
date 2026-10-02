using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Desktop.Logic.Overrides;
using Kubuno.Views.Designer;
using Kubuno.Shared.Logging;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace Kubuno.Desktop.LanguageService.Overrides
{
    /// <summary>
    /// The light bulb (Ctrl+.) "Substituer des membres…" in a Rust file (docs/EVENTS.md EVT-7b, "Override
    /// assistance"): offered on a Kubuno control class - inside <c>impl Control for X</c> (any level trait) or on a
    /// <c>#[derive(Component)]</c> / <c>#[derive(UserControl)]</c> struct - independently of rust-analyzer.
    /// </summary>
    [Export(typeof(ISuggestedActionsSourceProvider))]
    [Name("Kubuno Override Members")]
    [ContentType(Kubuno.Rust.Constants.RustContentType)]
    internal sealed class OverrideSuggestedActionsSourceProvider : ISuggestedActionsSourceProvider
    {
        public ISuggestedActionsSource? CreateSuggestedActionsSource(ITextView textView, ITextBuffer textBuffer) =>
            textView is null || textBuffer is null ? null : new OverrideSuggestedActionsSource(textView, textBuffer);
    }

    internal sealed class OverrideSuggestedActionsSource : ISuggestedActionsSource
    {
        private readonly ITextView _view;
        private readonly ITextBuffer _buffer;

        public OverrideSuggestedActionsSource(ITextView view, ITextBuffer buffer)
        {
            _view = view;
            _buffer = buffer;
        }

        public event EventHandler<EventArgs>? SuggestedActionsChanged
        {
            add { }
            remove { }
        }

        private OverrideContext? ContextAt(SnapshotSpan range)
        {
            try
            {
                var snapshot = range.Snapshot;
                var context = OverrideAssistant.Analyze(snapshot.GetText(), range.Start.Position, OverrideCatalog.Default);
                return context is { Available.Count: > 0 } ? context : null;
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                return null;
            }
        }

        public Task<bool> HasSuggestedActionsAsync(ISuggestedActionCategorySet requestedActionCategories, SnapshotSpan range, CancellationToken cancellationToken) =>
            Task.Run(() => ContextAt(range) is not null, cancellationToken);

        public IEnumerable<SuggestedActionSet> GetSuggestedActions(ISuggestedActionCategorySet requestedActionCategories, SnapshotSpan range, CancellationToken cancellationToken)
        {
            if (ContextAt(range) is not { } context)
            {
                return Enumerable.Empty<SuggestedActionSet>();
            }

            return new[]
            {
                new SuggestedActionSet(PredefinedSuggestedActionCategoryNames.Refactoring, new ISuggestedAction[] { new OverrideMembersAction(_view, _buffer, context) }, priority: SuggestedActionSetPriority.Medium),
            };
        }

        public bool TryGetTelemetryId(out Guid telemetryId)
        {
            telemetryId = Guid.Empty;
            return false;
        }

        public void Dispose()
        {
        }
    }

    /// <summary>Opens <see cref="OverrideMembersDialog"/> and writes the chosen members.</summary>
    internal sealed class OverrideMembersAction : ISuggestedAction
    {
        private readonly ITextView _view;
        private readonly ITextBuffer _buffer;
        private readonly OverrideContext _context;

        public OverrideMembersAction(ITextView view, ITextBuffer buffer, OverrideContext context)
        {
            _view = view;
            _buffer = buffer;
            _context = context;
        }

        public string DisplayText => DesignerText.IsFrench ? "Substituer des membres…" : "Override Members…";

        public bool HasActionSets => false;

        public string? IconAutomationText => null;

        public ImageMoniker IconMoniker => default;

        public string? InputGestureText => null;

        public bool HasPreview => false;

        public Task<IEnumerable<SuggestedActionSet>?> GetActionSetsAsync(CancellationToken cancellationToken) => Task.FromResult<IEnumerable<SuggestedActionSet>?>(null);

        public Task<object?> GetPreviewAsync(CancellationToken cancellationToken) => Task.FromResult<object?>(null);

        public void Invoke(CancellationToken cancellationToken)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var dialog = new OverrideMembersDialog(_context);
            if (dialog.ShowModal() != true || dialog.Selected.Count == 0)
            {
                return;
            }

            Apply(_view, _buffer, _context, dialog.Selected);
        }

        /// <summary>Writes <paramref name="members"/> into the buffer as ONE undo unit, then places the caret in the first one.</summary>
        internal static void Apply(ITextView view, ITextBuffer buffer, OverrideContext context, IReadOnlyList<OverridableMember> members)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var snapshot = buffer.CurrentSnapshot;
            if (!string.Equals(snapshot.GetText(), context.Text, StringComparison.Ordinal))
            {
                // The text changed while the dialog was open: plan against the current one.
                var caretNow = view.Caret.Position.BufferPosition.Position;
                context = OverrideAssistant.Analyze(snapshot.GetText(), Math.Min(caretNow, snapshot.Length), OverrideCatalog.Default) ?? context;
            }

            var newline = snapshot.GetText().Contains("\r\n") ? "\r\n" : "\n";
            var edits = OverrideAssistant.Plan(context, members, newline, out var caret);
            using (var edit = buffer.CreateEdit())
            {
                foreach (var e in edits)
                {
                    edit.Replace(e.Start, e.Length, e.NewText);
                }

                edit.Apply();
            }

            if (caret >= 0 && caret <= buffer.CurrentSnapshot.Length)
            {
                view.Caret.MoveTo(new SnapshotPoint(buffer.CurrentSnapshot, caret));
                view.ViewScroller.EnsureSpanVisible(new SnapshotSpan(buffer.CurrentSnapshot, caret, 0));
            }

            KubunoLog.WriteLine($"Kubuno: {members.Count} member(s) of {context.TypeName} overridden.");
        }

        public bool TryGetTelemetryId(out Guid telemetryId)
        {
            telemetryId = Guid.Empty;
            return false;
        }

        public void Dispose()
        {
        }
    }
}
