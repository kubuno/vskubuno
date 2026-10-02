using System;
using System.Text.Json;

namespace Kubuno.Views.Designer.DesignSurface
{
    /// <summary>See <see cref="IProtocolDesignSurfaceHost.ContextMenuRequested"/>.</summary>
    public sealed class DesignSurfaceContextMenuEventArgs : EventArgs
    {
        public DesignSurfaceContextMenuEventArgs(double x, double y, int screenX, int screenY, string? elementId, string? menu = null)
        {
            Menu = menu;
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

        /// <summary>
        /// A menu other than the context menu (docs/RIBBON.md section 9): <c>"add"</c> (a ribbon's "+" glyph - what can be added
        /// into the element) or <c>"tasks"</c> (its smart tag); null for the context menu.
        /// </summary>
        public string? Menu { get; }
    }

    /// <summary>A design-surface keyboard command (mirrors <c>kubuno_views::design::DesignCommand</c>).</summary>
    public enum DesignSurfaceCommand
    {
        Copy,
        Cut,
        Paste,
        Duplicate,
    }

    /// <summary>See <see cref="IProtocolDesignSurfaceHost.SurfaceCommandRequested"/>.</summary>
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

    /// <summary>See <see cref="IProtocolDesignSurfaceHost.ElementDoubleClicked"/>.</summary>
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

            var kind = root.TryGetProperty("menu", out var menuProp) && menuProp.ValueKind == JsonValueKind.String ? menuProp.GetString() : null;
            menu = new DesignSurfaceContextMenuEventArgs(x, y, (int)screenX, (int)screenY, elementId, kind);
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
