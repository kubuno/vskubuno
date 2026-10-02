using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using Kubuno.Views.Designer;
using Kubuno.Views.Designer.DesignSurface;

namespace Kubuno.Desktop.Designer.DesignSurface
{
    /// <summary>
    /// DSG-9 (`vskubuno/docs/DESIGNER.md`'s "DSG-9 protocol" section): move/resize drag, Flow reorder
    /// and VS-Toolbox drop, layered onto the EXISTING DSG-6 stdio channel - a second `partial class`
    /// piece, kept separate from `RustDesignSurfaceHost.cs`/`RustDesignSurfaceHost.Protocol.cs` exactly
    /// as that file's own class doc explains DSG-6 was ("so this work does not collide with concurrent
    /// changes"): those two files are owned by other agents during this package's own development and
    /// are not touched here.
    ///
    /// <para><b>Sending (host -&gt; surface).</b> <see cref="NotifyDragEnter"/>/<see cref="NotifyDragOver"/>/
    /// <see cref="NotifyDrop"/>/<see cref="NotifyDragLeave"/> write one JSON line each, reusing the SAME
    /// private <c>SendLine</c> helper <c>RustDesignSurfaceHost.Protocol.cs</c> already declares - calling
    /// a sibling partial-class-part's private member is ordinary C#, not a file edit, so the two files
    /// stay byte-for-byte as this package found them. Named `Notify*`, not `DragEnter`/`DragOver`/`Drop`/
    /// `DragLeave`: this type derives from <see cref="System.Windows.Interop.HwndHost"/> -&gt;
    /// <see cref="System.Windows.UIElement"/>, which already declares ROUTED EVENTS of those exact names
    /// for WPF's own (unrelated) drag-drop system - reusing them would only hide, not override them
    /// (confirmed live: `CS0108` on a build), a legal but needlessly confusing shadow this file avoids
    /// entirely by not colliding on the name at all. No "resend on restart" caching (unlike
    /// <c>SendSetText</c>/<c>SendSetDesignMode</c>/<c>SendSelect</c>): a toolbox drag is an in-progress
    /// OLE gesture owned by VS's own Toolbox, not standing state the pane must remember across a surface
    /// crash - the drag itself would already have ended from the user's perspective by the time a restart
    /// finishes, so there is nothing meaningful to replay.</para>
    ///
    /// <para><b>Receiving (surface -&gt; host).</b> <see cref="TryDispatchDragDropLine"/> recognises the shapes DSG-9
    /// adds - the batched `editRequests`, a single `editRequest` whose `op.kind` is `moveElement`/`insertChild`
    /// (`TryParseEditRequest` in the other file returns `false` for those two kinds) and `dropTargetChanged` -
    /// and is called by <c>RustDesignSurfaceHost.Protocol.cs</c>'s one stdout listener for every line that
    /// listener does not handle itself. (This file used to attach a SECOND <c>OutputDataReceived</c> listener
    /// lazily, from <c>Notify*</c> and a <c>Loaded</c>/<c>ChildReady</c> class handler; found live
    /// (docs/DESIGNER.md section 11) that a Toolbox drop inside Visual Studio still reached neither - the
    /// line was only logged as "unrecognised" - so the second listener was replaced by this direct call.)</para>
    ///
    /// <para><b>Not this package's job</b> (same carve-out DSG-6's own doc already states for
    /// `EditRequested`): forwarding <see cref="EditRequestsReceived"/>/<see cref="DragDropEditRequested"/>
    /// to `kubuno-views-ls`'s `kubuno/applyEdit` as one undo-scoped batch (DSG-5's own compound-action
    /// machinery) and turning <see cref="DropTargetChanged"/> into an actual OLE drop-effect/cursor is
    /// wiring left for whichever package connects the two ends end-to-end
    /// (<see cref="DesignSurfaceEditingCoordinator"/>'s own future extension, or a sibling of it) - this
    /// file only ever parses the wire shape and raises the event.</para>
    /// </summary>
    public sealed partial class RustDesignSurfaceHost
    {
        /// <summary>
        /// Raised for the batched form of a DSG-9 move/resize drag's mouse-up
        /// (<c>{"type":"editRequests","ops":[...],"gesture":"move"|"resize"}</c>) - every op is a plain
        /// `setAttribute` (design.rs's own `DesignController::end_drag` never puts anything else in a
        /// batch), forward the whole <see cref="DesignSurfaceEditRequestsReceivedEventArgs.Ops"/> list to
        /// `kubuno-views-ls`'s `kubuno/applyEdit` inside ONE undo-scoped compound action (DSG-5's own
        /// scope) - `DESIGNER.md` DSG-9 item 1's "the host applies them as one undo unit".
        /// </summary>
        public event EventHandler<DesignSurfaceEditRequestsReceivedEventArgs>? EditRequestsReceived;

        /// <summary>
        /// Raised for a SINGLE DSG-9 edit request whose `op.kind` is `moveElement` (a Flow reorder drop)
        /// or `insertChild` (a toolbox drop) - the two op kinds
        /// `RustDesignSurfaceHost.Protocol.cs`'s own <see cref="IDesignSurfaceHost.EditRequested"/> does
        /// NOT recognise (its `DesignSurfaceProtocol.TryParseEditRequest` only ever produces
        /// `SetAttribute`/`RemoveElement`), so this is the ONLY event either op ever reaches a subscriber
        /// through.
        /// </summary>
        public event EventHandler<DesignSurfaceDragDropEditRequestedEventArgs>? DragDropEditRequested;

        /// <summary>
        /// Raised every time the surface reports a new toolbox-drop target
        /// (<c>{"type":"dropTargetChanged","target":{...}|null}</c>) - `DESIGNER.md` DSG-9 item 3's live
        /// drop feedback: <see cref="DesignSurfaceDropTargetChangedEventArgs.Target"/>.<c>Valid</c> is what
        /// a real OLE <c>IDropTarget.DragOver</c> implementation would read to choose
        /// <c>DROPEFFECT_COPY</c> vs. <c>DROPEFFECT_NONE</c> (the "not allowed" cursor); `Target` itself
        /// is <see langword="null"/> when there is currently no target (nothing under the pointer, or the
        /// drag just left).
        /// </summary>
        public event EventHandler<DesignSurfaceDropTargetChangedEventArgs>? DropTargetChanged;

        /// <summary>
        /// A key pressed on the surface that Visual Studio's own accelerator translation did not handle
        /// (raised on the UI thread, after <c>IVsFilterKeys2.TranslateAcceleratorEx</c> reported "not
        /// translated") - the designer pane handles the editing chords itself (Ctrl+Z / Ctrl+Y).
        /// </summary>
        public event EventHandler<DesignSurfaceKeyEventArgs>? UnhandledSurfaceKey;

        /// <summary>`kubuno/dragEnter {component}` - a VS Toolbox drag entered the surface's own window. `component` is a registry element name (`"Button"`).</summary>
        public void NotifyDragEnter(string component)
        {
            SendLine(DesignSurfaceDragDropProtocol.EncodeDragEnter(component));
        }

        /// <summary>`kubuno/dragOver {x, y}` - the toolbox drag moved to `(x, y)`, surface-client DIP.</summary>
        public void NotifyDragOver(double x, double y)
        {
            SendLine(DesignSurfaceDragDropProtocol.EncodeDragOver(x, y));
        }

        /// <summary>`kubuno/drop {x, y}` - the toolbox drag was released at `(x, y)`.</summary>
        public void NotifyDrop(double x, double y)
        {
            SendLine(DesignSurfaceDragDropProtocol.EncodeDrop(x, y));
        }

        /// <summary>`kubuno/dragLeave` - the toolbox drag left the surface's own window.</summary>
        public void NotifyDragLeave()
        {
            SendLine(DesignSurfaceDragDropProtocol.EncodeDragLeave());
        }

        /// <summary>
        /// Dispatches one line of the surface's stdout if it is one of DSG-9's shapes (returns <see langword="true"/>),
        /// else returns <see langword="false"/> and leaves it to the caller. Runs on a .NET thread-pool thread (async
        /// pipe read); raises on the UI thread via <see cref="System.Windows.Threading.Dispatcher.BeginInvoke(System.Delegate)"/>
        /// because a subscriber may touch WPF/VS objects.
        /// </summary>
        private bool TryDispatchDragDropLine(string line)
        {
            if (DesignSurfaceDragDropProtocol.TryParseEditRequestsBatch(line, out var ops, out var gesture))
            {
#pragma warning disable VSTHRD001, VSTHRD110
                Dispatcher.BeginInvoke(new Action(() => EditRequestsReceived?.Invoke(this, new DesignSurfaceEditRequestsReceivedEventArgs(ops, gesture))));
#pragma warning restore VSTHRD001, VSTHRD110
                return true;
            }

            if (DesignSurfaceDragDropProtocol.TryParseDragDropEditRequest(line, out var dragDropOp) && dragDropOp != null)
            {
#pragma warning disable VSTHRD001, VSTHRD110
                Dispatcher.BeginInvoke(new Action(() => DragDropEditRequested?.Invoke(this, new DesignSurfaceDragDropEditRequestedEventArgs(dragDropOp))));
#pragma warning restore VSTHRD001, VSTHRD110
                return true;
            }

            if (DesignSurfaceDragDropProtocol.TryParseDropTargetChanged(line, out var target))
            {
#pragma warning disable VSTHRD001, VSTHRD110
                Dispatcher.BeginInvoke(new Action(() => DropTargetChanged?.Invoke(this, new DesignSurfaceDropTargetChangedEventArgs(target))));
#pragma warning restore VSTHRD001, VSTHRD110
                return true;
            }

            return false;
        }
    }
}
