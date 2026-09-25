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

        /// <summary>
        /// Turns design mode on/off (docs/DESIGNER.md's DSG-6 protocol, <c>kubuno/setDesignMode</c>):
        /// while on, clicks select elements instead of interacting with the compiled widgets, and
        /// Delete/arrow gestures raise <see cref="EditRequested"/>. A no-op on
        /// <see cref="PlaceholderDesignSurfaceHost"/>. Added to the interface (rather than left a
        /// <see cref="RustDesignSurfaceHost"/>-only member, which already had a matching method before
        /// this) so the VSIX integration step (INTEGRATION.md &sect;6/&sect;8) can turn it on through
        /// <see cref="UI.DesignerSplitView"/>'s own <see cref="IDesignSurfaceHost"/>-typed field, with no
        /// downcast.
        /// </summary>
        void SetDesignMode(bool on);

        /// <summary>
        /// Raised when a Delete or a nudging arrow on the design surface (while design mode is on)
        /// produces an edit request (docs/DESIGNER.md's DSG-6 protocol, <c>editRequest</c>) - forward
        /// <see cref="DesignSurfaceEditRequestedEventArgs.Op"/> to <c>kubuno-views-ls</c>'s
        /// <c>kubuno/applyEdit</c> and apply the result through <c>Editing/</c> (see
        /// <see cref="DesignSurfaceEditingCoordinator"/>, the VSIX integration step's own caller of this
        /// event - INTEGRATION.md &sect;8). Never raised by <see cref="PlaceholderDesignSurfaceHost"/>.
        /// </summary>
        event EventHandler<DesignSurfaceEditRequestedEventArgs>? EditRequested;
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
