using System;
using System.Windows;

namespace Kubuno.VisualStudio.Designer.DesignSurface
{
    /// <summary>
    /// The seam DSG-7 plugs into: everything <see cref="UI.DesignerSplitView"/> needs from "the thing
    /// that shows the Design pane", without knowing whether that thing is today's
    /// <see cref="PlaceholderDesignSurfaceHost"/> or DSG-7's real <c>HwndHost</c> subclass embedding
    /// the out-of-process <c>kubuno-views-designer</c> surface (docs/DESIGNER.md §3/§7:
    /// <c>kubuno/embed</c>, <c>kubuno/surfaceReady</c>, <c>SetParent</c>).
    ///
    /// Deliberately narrow and buffer-text-shaped, matching docs/DESIGNER.md §2's "source of truth is
    /// the VS text buffer": the host is only ever pushed the current document text (never asked to
    /// read or write a file), and it only ever reports a selection outward - it does not itself decide
    /// what a gesture means (docs/DESIGNER.md §3: "designer process = gesture → intent; language server
    /// = intent → text"; that translation belongs in DSG-6/DSG-9, not in this interface).
    ///
    /// A real implementation is expected to be a <see cref="FrameworkElement"/>-derived
    /// <see cref="System.Windows.Interop.HwndHost"/> (DSG-7); <see cref="Content"/> is typed as
    /// <see cref="FrameworkElement"/>, not <c>HwndHost</c> specifically, so the placeholder
    /// implementation here can be an ordinary WPF control instead.
    /// </summary>
    public interface IDesignSurfaceHost : IDisposable
    {
        /// <summary>The WPF content <see cref="UI.DesignerSplitView"/> places in the Design pane.</summary>
        FrameworkElement Content { get; }

        /// <summary>
        /// Pushes the current document text, mirroring the <c>kubuno/setBuffer</c> notification
        /// docs/DESIGNER.md §3 describes (full-text, debounced by the caller - this method itself must
        /// not block on a re-render). Called once with the buffer's initial contents right after the
        /// pane opens, then again on every <c>ITextBuffer.Changed</c> (see
        /// <see cref="UI.DesignerSplitView"/>).
        /// </summary>
        void SetDocumentText(string xmlText);

        /// <summary>
        /// Raised when the user selects one or more elements on the design surface (a click, a
        /// rubber-band drag). Consumed by DSG-8's bidirectional selection sync to move the XML pane's
        /// caret; unused by <see cref="PlaceholderDesignSurfaceHost"/>, which never raises it.
        /// </summary>
        event EventHandler<DesignSurfaceSelectionChangedEventArgs>? SelectionChanged;
    }

    /// <summary>
    /// The element-id scheme is owned jointly by DSG-2 and DSG-6 (docs/DESIGNER.md §6's cross-cutting
    /// note) and is not yet fixed; <see cref="ElementIds"/> is therefore an opaque string list rather
    /// than a typed id, so this seam does not have to change shape once that scheme lands.
    /// </summary>
    public sealed class DesignSurfaceSelectionChangedEventArgs : EventArgs
    {
        public DesignSurfaceSelectionChangedEventArgs(System.Collections.Generic.IReadOnlyList<string> elementIds)
        {
            ElementIds = elementIds;
        }

        public System.Collections.Generic.IReadOnlyList<string> ElementIds { get; }
    }
}
