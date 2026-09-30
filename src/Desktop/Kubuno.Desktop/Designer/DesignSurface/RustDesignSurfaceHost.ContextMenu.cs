using System;
using System.Text.Json;

namespace Kubuno.VisualStudio.Designer.DesignSurface
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

    /// <summary>See <see cref="RustDesignSurfaceHost.ContextMenuRequested"/>.</summary>
    public sealed class DesignSurfaceContextMenuEventArgs : EventArgs
    {
        public DesignSurfaceContextMenuEventArgs(double x, double y, int screenX, int screenY, string? elementId)
        {
            X = x;
            Y = y;
            ScreenX = screenX;
            ScreenY = screenY;
            ElementId = elementId;
        }

        /// <summary>The point in surface-client DIP.</summary>
        public double X { get; }

        public double Y { get; }

        /// <summary>The point in physical screen pixels - where the menu opens.</summary>
        public int ScreenX { get; }

        public int ScreenY { get; }

        /// <summary>The element the menu is for (<c>""</c> = the root element), or null for the view itself.</summary>
        public string? ElementId { get; }
    }

    /// <summary>A design-surface keyboard command (mirrors <c>kubuno_views::design::DesignCommand</c>).</summary>
    public enum DesignSurfaceCommand
    {
        Copy,
        Cut,
        Paste,
        Duplicate,
    }

    /// <summary>See <see cref="RustDesignSurfaceHost.SurfaceCommandRequested"/>.</summary>
    public sealed class DesignSurfaceCommandEventArgs : EventArgs
    {
        public DesignSurfaceCommandEventArgs(DesignSurfaceCommand command, string? elementId)
        {
            Command = command;
            ElementId = elementId;
        }

        public DesignSurfaceCommand Command { get; }

        /// <summary>The selected element (<c>""</c> = the root / the view), or null when nothing is selected.</summary>
        public string? ElementId { get; }
    }

    /// <summary>See <see cref="RustDesignSurfaceHost.ElementDoubleClicked"/>.</summary>
    public sealed class DesignSurfaceDoubleClickEventArgs : EventArgs
    {
        public DesignSurfaceDoubleClickEventArgs(string elementId)
        {
            ElementId = elementId ?? throw new ArgumentNullException(nameof(elementId));
        }

        /// <summary>The double-clicked element (<c>""</c> = the view's root element).</summary>
        public string ElementId { get; }
    }

    /// <summary>The pure parse half of <c>contextMenu</c>/<c>command</c> (unit-tested with no live process).</summary>
    public static class DesignSurfaceContextMenuProtocol
    {
        /// <summary>Parses <c>{"type":"doubleClick","elementId":"1"}</c> (<c>kubuno_views::protocol::SurfaceMessage::DoubleClick</c>).</summary>
        public static bool TryParseDoubleClick(string line, out DesignSurfaceDoubleClickEventArgs? doubleClick)
        {
            doubleClick = null;
            if (!TryParse(line, "doubleClick", out var root) ||
                !root.TryGetProperty("elementId", out var idProp) || idProp.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            doubleClick = new DesignSurfaceDoubleClickEventArgs(idProp.GetString() ?? string.Empty);
            return true;
        }

        public static bool TryParseContextMenu(string line, out DesignSurfaceContextMenuEventArgs? menu)
        {
            menu = null;
            if (!TryParse(line, "contextMenu", out var root) ||
                !TryNumber(root, "x", out var x) || !TryNumber(root, "y", out var y) ||
                !TryNumber(root, "screenX", out var screenX) || !TryNumber(root, "screenY", out var screenY) ||
                !TryOptionalString(root, "elementId", out var elementId))
            {
                return false;
            }

            menu = new DesignSurfaceContextMenuEventArgs(x, y, (int)screenX, (int)screenY, elementId);
            return true;
        }

        public static bool TryParseCommand(string line, out DesignSurfaceCommandEventArgs? command)
        {
            command = null;
            if (!TryParse(line, "command", out var root) ||
                !root.TryGetProperty("name", out var nameProp) || nameProp.ValueKind != JsonValueKind.String ||
                !TryOptionalString(root, "elementId", out var elementId))
            {
                return false;
            }

            DesignSurfaceCommand name;
            switch (nameProp.GetString())
            {
                case "copy": name = DesignSurfaceCommand.Copy; break;
                case "cut": name = DesignSurfaceCommand.Cut; break;
                case "paste": name = DesignSurfaceCommand.Paste; break;
                case "duplicate": name = DesignSurfaceCommand.Duplicate; break;
                default: return false;
            }

            command = new DesignSurfaceCommandEventArgs(name, elementId);
            return true;
        }

        private static bool TryParse(string line, string type, out JsonElement root)
        {
            root = default;
            if (string.IsNullOrWhiteSpace(line))
            {
                return false;
            }

            try
            {
                using var doc = JsonDocument.Parse(line);
                if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                    !doc.RootElement.TryGetProperty("type", out var typeProp) || typeProp.ValueKind != JsonValueKind.String ||
                    typeProp.GetString() != type)
                {
                    return false;
                }

                root = doc.RootElement.Clone();
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static bool TryNumber(JsonElement root, string name, out double value)
        {
            value = 0;
            return root.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Number && prop.TryGetDouble(out value);
        }

        /// <summary>A string or null property (a missing one is an error).</summary>
        private static bool TryOptionalString(JsonElement root, string name, out string? value)
        {
            value = null;
            if (!root.TryGetProperty(name, out var prop))
            {
                return false;
            }

            if (prop.ValueKind == JsonValueKind.Null)
            {
                return true;
            }

            if (prop.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            value = prop.GetString();
            return true;
        }
    }
}
