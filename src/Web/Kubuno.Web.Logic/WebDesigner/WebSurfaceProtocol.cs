using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Kubuno.Web.Logic.WebDesigner
{
    /// <summary>
    /// SPIKE (docs/WEB-VIEWS.md, lot WV-9a, §4.4): the JSON protocol between Visual Studio and the WebView2 design
    /// surface - the desktop design surface's wire shapes (<c>kubuno_desktop_views::protocol</c>, <c>DesignSurfaceProtocol</c>
    /// and <c>DesignSurfaceDragDropProtocol</c> in Kubuno.Desktop) carried by <c>CoreWebView2.PostWebMessageAsJson</c>
    /// (host -&gt; page) and <c>window.chrome.webview.postMessage</c> (page -&gt; host) instead of stdin/stdout lines.
    /// Pure: no WebView2, no Visual Studio, unit-tested by tests/Kubuno.Web.Tests.
    /// </summary>
    public static class WebSurfaceProtocol
    {
        /// <summary>The <c>surfaceInfo</c> version this host speaks.</summary>
        public const int SurfaceInfoVersion = 1;

        /// <summary>
        /// Same encoder as the desktop protocol (no HTML escaping of &lt; &gt; &amp;): the JSON reaches the page as a
        /// JavaScript value through <c>PostWebMessageAsJson</c>, never as HTML, so the escaping would only make the
        /// wire differ from the desktop's byte for byte.
        /// </summary>
        private static readonly JsonSerializerOptions Wire = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        // ---- host -> surface (the desktop shapes) ----

        public static string EncodeSetText(string text) => JsonSerializer.Serialize(new { type = "setText", text }, Wire);

        public static string EncodeSetDesignMode(bool on) => JsonSerializer.Serialize(new { type = "setDesignMode", on }, Wire);

        public static string EncodeSelect(string? id) => JsonSerializer.Serialize(new { type = "select", id }, Wire);

        public static string EncodeSetCanvasBackground(string color) => JsonSerializer.Serialize(new { type = "setCanvasBackground", color }, Wire);

        public static string EncodeDragEnter(string component) => JsonSerializer.Serialize(new { type = "dragEnter", component }, Wire);

        /// <summary><c>dragOver {x, y}</c>, page CSS pixels relative to the page's viewport.</summary>
        public static string EncodeDragOver(double x, double y) => "{\"type\":\"dragOver\",\"x\":" + Number(x) + ",\"y\":" + Number(y) + "}";

        public static string EncodeDrop(double x, double y) => "{\"type\":\"drop\",\"x\":" + Number(x) + ",\"y\":" + Number(y) + "}";

        public static string EncodeDragLeave() => "{\"type\":\"dragLeave\"}";

        // ---- host -> surface (web additions) ----

        /// <summary>
        /// Web addition: <c>setVsTheme {mode, colors}</c> - Visual Studio's theme (<c>"dark"</c>/<c>"light"</c>) and the
        /// environment colours the surface's chrome (canvas, adorners, insertion marker) is drawn with, as
        /// <c>#rrggbb</c>, sent at load and on every theme switch.
        /// </summary>
        public static string EncodeSetVsTheme(bool dark, IReadOnlyDictionary<string, string> colors)
        {
            var ordered = colors.OrderBy(c => c.Key, StringComparer.Ordinal).ToDictionary(c => c.Key, c => c.Value);
            return JsonSerializer.Serialize(new { type = "setVsTheme", mode = dark ? "dark" : "light", colors = ordered }, Wire);
        }

        /// <summary>SPIKE: <c>setDropChannel {channel}</c> - which Toolbox drag path the page listens to (<see cref="DropChannel"/>).</summary>
        public static string EncodeSetDropChannel(DropChannel channel) =>
            JsonSerializer.Serialize(new { type = "setDropChannel", channel = channel == DropChannel.Html5 ? "html5" : "host" }, Wire);

        /// <summary>
        /// SPIKE: <c>setComponents {components:[{name, container, xml}]}</c> - what the page may draw and insert (the
        /// catalog the Toolbox items come from; WV-9b: the registry, <c>projectComponents</c>).
        /// </summary>
        public static string EncodeSetComponents(IEnumerable<SpikeElement> elements) =>
            JsonSerializer.Serialize(new { type = "setComponents", components = elements.Select(e => new { name = e.Name, container = e.IsContainer, xml = e.ToolboxXml }).ToList() }, Wire);

        /// <summary>SPIKE: <c>showFontSpecimen {on}</c> - shows the font comparison block (the same block the live comparison measures).</summary>
        public static string EncodeShowFontSpecimen(bool on) => JsonSerializer.Serialize(new { type = "showFontSpecimen", on }, Wire);

        // ---- surface -> host ----

        /// <summary>
        /// Parses one message the page posted (<c>CoreWebView2WebMessageReceivedEventArgs.WebMessageAsJson</c>).
        /// False for invalid JSON, an unknown <c>type</c> or a malformed known one - never throws.
        /// </summary>
        public static bool TryParse(string? json, out WebSurfaceMessage? message)
        {
            message = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                using var document = JsonDocument.Parse(json!);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !TryString(root, "type", out var type))
                {
                    return false;
                }

                message = type switch
                {
                    "surfaceInfo" => ParseSurfaceInfo(root),
                    "selectionChanged" => ParseSelectionChanged(root),
                    "editRequest" => ParseEditRequest(root),
                    "dropTargetChanged" => ParseDropTargetChanged(root),
                    "focusState" => TryBool(root, "editing", out var editing) ? new FocusStateMessage(editing) : null,
                    "unhandledKey" => TryString(root, "key", out var key)
                        ? new UnhandledKeyMessage(key, Flag(root, "ctrl"), Flag(root, "shift"), Flag(root, "alt"))
                        : null,
                    "metrics" => TryNumber(root, "dpr", out var dpr) && TryNumber(root, "width", out var width) && TryNumber(root, "height", out var height)
                        ? new MetricsMessage(dpr, width, height)
                        : null,
                    "log" => TryString(root, "message", out var text) ? new LogMessage(text) : null,
                    "toolboxDragDetected" => new ToolboxDragDetectedMessage(),
                    "surfaceError" => TryString(root, "message", out var error)
                        ? new SurfaceErrorMessage(error, TryNumber(root, "line", out var line) ? (int)line : 0, TryNumber(root, "column", out var column) ? (int)column : 0)
                        : null,
                    _ => null,
                };
                return message is not null;
            }
            catch (JsonException)
            {
                message = null;
                return false;
            }
        }

        /// <summary>Null when the page may be used, else why not (the handshake check, like the desktop's <c>CheckSurfaceInfo</c>).</summary>
        public static string? CheckSurfaceInfo(SurfaceInfoMessage info)
        {
            if (info.Version != SurfaceInfoVersion)
            {
                return $"handshake version {info.Version}, expected {SurfaceInfoVersion}";
            }

            return info.Target == "web" ? null : $"surface target '{info.Target}', expected 'web'";
        }

        private static WebSurfaceMessage? ParseSurfaceInfo(JsonElement root)
        {
            if (!TryNumber(root, "version", out var version))
            {
                return null;
            }

            return new SurfaceInfoMessage((int)version, OptionalString(root, "target"), OptionalString(root, "views"), OptionalString(root, "ui"));
        }

        private static WebSurfaceMessage? ParseSelectionChanged(JsonElement root)
        {
            var ids = new List<string>();
            if (root.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
            {
                ids.Add(idProp.GetString()!);
                if (root.TryGetProperty("ids", out var idsProp) && idsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in idsProp.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String && item.GetString() is { } other && !ids.Contains(other))
                        {
                            ids.Add(other);
                        }
                    }
                }
            }
            else if (root.TryGetProperty("id", out idProp) && idProp.ValueKind != JsonValueKind.Null)
            {
                return null;
            }

            SurfaceBounds? bounds = null;
            if (root.TryGetProperty("bounds", out var boundsProp) && boundsProp.ValueKind == JsonValueKind.Object)
            {
                if (!TryBounds(boundsProp, out var b))
                {
                    return null;
                }

                bounds = b;
            }

            return new SelectionChangedMessage(ids, bounds);
        }

        private static WebSurfaceMessage? ParseEditRequest(JsonElement root)
        {
            if (!root.TryGetProperty("op", out var op) || op.ValueKind != JsonValueKind.Object || !TryString(op, "kind", out var kind))
            {
                return null;
            }

            switch (kind)
            {
                case "setAttribute":
                    return TryString(op, "elementId", out var id) && TryString(op, "name", out var name) && TryString(op, "value", out var value)
                        ? new EditRequestMessage(SurfaceEditOp.SetAttribute(id, name, value))
                        : null;
                case "removeElement":
                    return TryString(op, "elementId", out var removed) ? new EditRequestMessage(SurfaceEditOp.RemoveElement(removed)) : null;
                case "insertChild":
                    return TryString(op, "parentId", out var parent) && TryNumber(op, "index", out var index) && TryString(op, "xml", out var xml)
                        ? new EditRequestMessage(SurfaceEditOp.InsertChild(parent, (int)index, xml))
                        : null;
                case "moveElement":
                    return TryString(op, "elementId", out var moved) && TryString(op, "newParentId", out var newParent) && TryNumber(op, "index", out var at)
                        ? new EditRequestMessage(SurfaceEditOp.MoveElement(moved, newParent, (int)at))
                        : null;
                default:
                    return null;
            }
        }

        private static WebSurfaceMessage? ParseDropTargetChanged(JsonElement root)
        {
            if (!root.TryGetProperty("target", out var target))
            {
                return null;
            }

            if (target.ValueKind == JsonValueKind.Null)
            {
                return new DropTargetChangedMessage(null);
            }

            if (target.ValueKind != JsonValueKind.Object || !TryBool(target, "valid", out var valid) || !TryString(target, "parentId", out var parentId) ||
                !TryNumber(target, "index", out var index) ||
                !TryNumber(target, "markerLeft", out var left) || !TryNumber(target, "markerTop", out var top) ||
                !TryNumber(target, "markerRight", out var right) || !TryNumber(target, "markerBottom", out var bottom))
            {
                return null;
            }

            return new DropTargetChangedMessage(new SurfaceDropTarget(valid, parentId, (int)index, new SurfaceBounds(left, top, right - left, bottom - top)));
        }

        private static bool TryBounds(JsonElement element, out SurfaceBounds bounds)
        {
            bounds = default;
            if (!TryNumber(element, "x", out var x) || !TryNumber(element, "y", out var y) ||
                !TryNumber(element, "width", out var width) || !TryNumber(element, "height", out var height))
            {
                return false;
            }

            bounds = new SurfaceBounds(x, y, width, height);
            return true;
        }

        private static bool TryString(JsonElement element, string name, out string value)
        {
            if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
            {
                value = prop.GetString()!;
                return true;
            }

            value = string.Empty;
            return false;
        }

        private static string? OptionalString(JsonElement element, string name) => TryString(element, name, out var value) ? value : null;

        private static bool TryNumber(JsonElement element, string name, out double value)
        {
            if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Number)
            {
                value = prop.GetDouble();
                return !double.IsNaN(value) && !double.IsInfinity(value);
            }

            value = 0;
            return false;
        }

        private static bool TryBool(JsonElement element, string name, out bool value)
        {
            if (element.TryGetProperty(name, out var prop) && (prop.ValueKind == JsonValueKind.True || prop.ValueKind == JsonValueKind.False))
            {
                value = prop.GetBoolean();
                return true;
            }

            value = false;
            return false;
        }

        private static bool Flag(JsonElement element, string name) => TryBool(element, name, out var value) && value;

        private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    /// <summary>SPIKE: the two ways a Visual Studio Toolbox drag can reach the page (docs/WEB-VIEWS.md §4.4).</summary>
    public enum DropChannel
    {
        /// <summary>
        /// Hybrid (the one kept, see the WV-9a findings): Chromium only DETECTS the drag (its <c>dragenter</c> sees
        /// <c>text/plain</c>; the page posts <c>toolboxDragDetected</c>), then the host raises an almost transparent child
        /// window over the WebView2 holding Visual Studio's own OLE <c>IDropTarget</c>, which takes the rest of the drag:
        /// it reads the Toolbox item (the desktop's private format) and drives the page with the desktop protocol's
        /// <c>dragEnter/dragOver/drop/dragLeave</c>. An <c>IDropTarget</c> registered on the WebView2 control's own window
        /// is never reached (OLE does not look past the browser's cross-process windows).
        /// </summary>
        Host,

        /// <summary>Chromium's own HTML5 drag and drop: the Toolbox data object's <c>CF_UNICODETEXT</c> seen as <c>text/plain</c>.</summary>
        Html5,
    }
}
