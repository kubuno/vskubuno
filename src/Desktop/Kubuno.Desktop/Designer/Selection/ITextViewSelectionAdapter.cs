using System;
using Kubuno.Desktop.Designer.Editing;

namespace Kubuno.Desktop.Designer.Selection
{
    /// <summary>
    /// The seam <see cref="SelectionSyncService"/> depends on instead of a raw VS text view type -
    /// mirroring <see cref="Editing.IEditableTextBuffer"/>'s own reasoning (unit-testable with a fake,
    /// see tests/Kubuno.Desktop.Tests/Designer/Selection/Fakes/FakeTextViewSelectionAdapter.cs).
    /// <see cref="Infrastructure.VsTextViewSelectionAdapter"/> is the real, <c>IVsTextView</c>-dependent
    /// implementation, wrapping <see cref="UI.CodeWindowHost.PrimaryView"/> - see that property's own
    /// doc comment ("Callers (DSG-8's selection sync) must tolerate null here and re-query later").
    ///
    /// Deliberately narrow and LSP-position-shaped (docs/DESIGNER.md §2's "source of truth is the VS
    /// text buffer" carried over to this seam too): a caller only ever asks "where is the caret" or
    /// "select this range", never reads/writes text directly (that stays <see cref="Editing.IEditableTextBuffer"/>'s
    /// job, DSG-5's own scope).
    /// </summary>
    public interface ITextViewSelectionAdapter
    {
        /// <summary>
        /// Raised whenever the caret is observed to have moved to a DIFFERENT position than last
        /// observed. <see cref="Infrastructure.VsTextViewSelectionAdapter"/> implements this by polling
        /// on a ~150 ms timer (docs/DESIGNER.md §1: "placing the caret in the XML pane ... highlights the
        /// corresponding element ... continuously ... debounced ~150 ms") rather than a genuine
        /// caret-moved event - see that class's own doc comment for why the legacy <c>IVsTextView</c>
        /// this library already standardises on (<c>UI.CodeWindowHost</c>, `Handlers.Infrastructure
        /// .VsHandlerDocumentHost`) has none, and a poll interval doubles naturally as the debounce
        /// interval instead of needing a second timer on top of a real event. A fake used in tests raises
        /// this synchronously on demand instead (no timer involved).
        /// </summary>
        event EventHandler CaretMoved;

        /// <summary>The caret's current position, in the same LSP (0-based line, UTF-16 character) shape every other position on this wire already uses.</summary>
        LspPosition GetCaretPosition();

        /// <summary>The XML pane's current full text - the same "buffer, not disk, is authoritative" text docs/DESIGNER.md §2 already requires elsewhere, used here by <see cref="ElementAttributeReader"/> to feed the Properties panel without a round trip.</summary>
        string GetCurrentText();

        /// <summary>
        /// Selects <paramref name="range"/> (the whole element, docs/DESIGNER.md §8's <c>kubuno/rangeOfElement</c>
        /// result) and moves the caret to its start - the design surface -&gt; XML pane sync direction
        /// (docs/DESIGNER.md §1: "caret to start tag, selection of the start tag or whole element - pick,
        /// document"; <see cref="SelectionSyncService"/>'s own doc comment records the choice: the WHOLE
        /// element, caret at its start, matching the XAML designer's own "select the element's markup"
        /// behavior rather than WinForms' bare caret-jump). Also reveals the range (scrolls it into
        /// view) - a selection sync that scrolls the XML pane off-screen would defeat its own purpose.
        /// </summary>
        void SelectElementRange(LspRange range);
    }
}
