using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Kubuno.Views.Designer.Editing;
using Kubuno.Views.Designer.UI;
using Kubuno.Views.Logging;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.TextManager.Interop;

namespace Kubuno.Views.Designer.Selection.Infrastructure
{
    /// <summary>
    /// The real <see cref="ITextViewSelectionAdapter"/>: wraps the embedded XML pane's own
    /// <c>IVsTextView</c> (<see cref="UI.CodeWindowHost.PrimaryView"/> - see that property's own doc
    /// comment on why a caller must tolerate it being <see langword="null"/> right after construction and
    /// re-query later) using the SAME legacy COM surface this library already standardises on for exactly
    /// this kind of "move the caret, read the caret, select a range" work
    /// (<c>Handlers.Infrastructure.VsHandlerDocumentHost.NavigateTo</c> is the precedent this class's
    /// <see cref="SelectElementRange"/> mirrors: <c>SetCaretPos</c>/<c>EnsureSpanVisible</c>, here
    /// extended to <c>SetSelection</c> for a whole-element range instead of a bare caret jump).
    ///
    /// <see cref="CaretMoved"/> is implemented by POLLING <c>GetCaretPos</c> on a
    /// <see cref="DispatcherTimer"/>, not a genuine caret-changed event: the legacy <c>IVsTextView</c>/
    /// <c>IVsTextViewEvents</c> COM surface this library already builds on has no such notification
    /// (checked - <c>IVsTextViewEvents</c> only carries <c>OnSetFocus</c>/<c>OnKillFocus</c>/
    /// <c>OnSetBuffer</c>/<c>OnChangeScrollInfo</c>). The modern alternative, <c>Microsoft.VisualStudio
    /// .Text.Editor.ITextView.Caret.PositionChanged</c>, needs a MEF-imported
    /// <c>IVsEditorAdaptersFactoryService</c> bridge this library deliberately keeps out of its own code
    /// (INTEGRATION.md §8 point 1: that import needs the VSIX's own MEF catalog composing it, so it is
    /// left to a later wiring step even for DSG-5's buffer/undo-history bridge) - polling avoids that
    /// dependency entirely, and doubles as the ~150 ms debounce docs/DESIGNER.md §1 itself asks for
    /// ("continuously ... debounced ~150 ms") without a second timer on top of a real event.
    /// </summary>
    public sealed class VsTextViewSelectionAdapter : ITextViewSelectionAdapter, IDisposable
    {
        /// <summary>docs/DESIGNER.md §1's own "~150 ms" figure.</summary>
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(150);

        private readonly IVsTextView _textView;
        private readonly IVsTextLines _textLines;
        private readonly DispatcherTimer _timer;
        private int _lastLine;
        private int _lastColumn;
        private bool _disposed;

        public VsTextViewSelectionAdapter(IVsTextView textView)
        {
            _textView = textView ?? throw new ArgumentNullException(nameof(textView));
            ErrorHandler.ThrowOnFailure(_textView.GetBuffer(out _textLines));

            // Deliberately NOT initialized from TryGetCaretPos(): this constructor runs at the end of
            // DesignSurfaceEditingCoordinator.SetupSelectionSyncAsync's own async chain (a laid-out
            // PrimaryView + a resolved JsonRpc + a fetched ComponentRegistry, all awaited first) - a caller
            // that moved the caret programmatically BEFORE that chain finished (e.g. Solution Explorer's
            // SymbolNavigator, whose own poll for a laid-out XmlTextView is much cheaper and typically wins
            // the race against the RPC round trip above) would otherwise have that first position baked in
            // as "no change yet", and OnTimerTick's very first tick would see the caret already sitting
            // there and never raise CaretMoved for it - silently dropping that navigation's selection sync
            // (docs/RSPROJ.md lot 8: "double-click a .kbview element node ... puts the XML caret on the
            // element", but the design surface never gets told). An impossible sentinel forces the first
            // poll tick to always treat wherever the caret already is as a change, exactly like a caller
            // that only started polling after the fact should.
            (_lastLine, _lastColumn) = (-1, -1);

            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = PollInterval };
            _timer.Tick += OnTimerTick;
            _timer.Start();
        }

        public event EventHandler? CaretMoved;

        public LspPosition GetCaretPosition()
        {
            var (line, column) = TryGetCaretPos();
            return new LspPosition(line, column);
        }

        public string GetCurrentText() => VsTextLinesText.ReadAll(_textLines);

        public void SelectElementRange(LspRange range)
        {
            try
            {
                ErrorHandler.ThrowOnFailure(_textView.SetSelection(range.Start.Line, range.Start.Character, range.End.Line, range.End.Character));
                var span = new TextSpan
                {
                    iStartLine = range.Start.Line,
                    iStartIndex = range.Start.Character,
                    iEndLine = range.End.Line,
                    iEndIndex = range.End.Character,
                };
                ErrorHandler.ThrowOnFailure(_textView.EnsureSpanVisible(span));
            }
            catch (COMException ex)
            {
                // Best-effort: a stale range (the buffer changed between kubuno/rangeOfElement resolving
                // it and this call landing) must not crash the pane - the next debounced re-parse/selection
                // round trip corrects it, the same tolerance DesignerSplitView's own SetDocumentText call
                // already applies to a COMException from an uninitialized buffer.
                KubunoViewsLogHost.Current.WriteException("DSG-8 selection sync: selecting an element range in the XML view failed", ex);
            }
        }

        private void OnTimerTick(object? sender, EventArgs e)
        {
            var (line, column) = TryGetCaretPos();
            if (line == _lastLine && column == _lastColumn)
            {
                return;
            }

            _lastLine = line;
            _lastColumn = column;
            CaretMoved?.Invoke(this, EventArgs.Empty);
        }

        private (int Line, int Column) TryGetCaretPos()
        {
            try
            {
                ErrorHandler.ThrowOnFailure(_textView.GetCaretPos(out var line, out var column));
                return (line, column);
            }
            catch (COMException)
            {
                // Best-effort: a view that is not yet laid out (right after construction, per
                // UI.CodeWindowHost.PrimaryView's own doc) has no caret position yet.
                return (_lastLine, _lastColumn);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _timer.Stop();
            _timer.Tick -= OnTimerTick;
        }
    }
}
