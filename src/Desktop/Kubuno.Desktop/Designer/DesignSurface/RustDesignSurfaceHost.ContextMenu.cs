using System;
using System.Text.Json;
using Kubuno.Views.Designer;
using Kubuno.Views.Designer.DesignSurface;

namespace Kubuno.Desktop.Designer.DesignSurface
{
    /// <summary>
    /// The surface → host messages of the design surface's context menus and keyboard commands
    /// (docs/DESIGNER.md §12): <c>contextMenu</c> (a right-click, Shift+F10 or the context-menu key - the
    /// surface has already selected the element and sent <c>selectionChanged</c>) and <c>command</c>
    /// (Ctrl+C / Ctrl+X / Ctrl+V / Ctrl+D while the surface has the focus). Both mirror
    /// <c>kubuno_views::protocol::SurfaceMessage</c> field for field.
    /// </summary>
    public sealed partial class RustDesignSurfaceHost
    {
        /// <summary>The surface asks for its context menu (raised on the UI thread).</summary>
        public event EventHandler<DesignSurfaceContextMenuEventArgs>? ContextMenuRequested;

        /// <summary>A clipboard/duplicate keyboard command on the surface (raised on the UI thread).</summary>
        public event EventHandler<DesignSurfaceCommandEventArgs>? SurfaceCommandRequested;

        /// <summary>A double-click on an element of the surface (raised on the UI thread): create or open its default event handler (docs/EVENTS.md §5.2).</summary>
        public event EventHandler<DesignSurfaceDoubleClickEventArgs>? ElementDoubleClicked;

        /// <summary>Dispatches a <c>contextMenu</c>/<c>command</c> line (true), or leaves the line to the caller (false).</summary>
        private bool TryDispatchContextMenuLine(string line)
        {
            if (DesignSurfaceContextMenuProtocol.TryParseContextMenu(line, out var menu) && menu is not null)
            {
#pragma warning disable VSTHRD001, VSTHRD110
                Dispatcher.BeginInvoke(new Action(() => ContextMenuRequested?.Invoke(this, menu)));
#pragma warning restore VSTHRD001, VSTHRD110
                return true;
            }

            if (DesignSurfaceContextMenuProtocol.TryParseCommand(line, out var command) && command is not null)
            {
#pragma warning disable VSTHRD001, VSTHRD110
                Dispatcher.BeginInvoke(new Action(() => SurfaceCommandRequested?.Invoke(this, command)));
#pragma warning restore VSTHRD001, VSTHRD110
                return true;
            }

            if (DesignSurfaceContextMenuProtocol.TryParseDoubleClick(line, out var doubleClick) && doubleClick is not null)
            {
#pragma warning disable VSTHRD001, VSTHRD110
                Dispatcher.BeginInvoke(new Action(() => ElementDoubleClicked?.Invoke(this, doubleClick)));
#pragma warning restore VSTHRD001, VSTHRD110
                return true;
            }

            return false;
        }
    }
}
