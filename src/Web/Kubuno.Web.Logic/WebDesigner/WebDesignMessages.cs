using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Kubuno.Web.Logic.WebDesigner
{
    /// <summary>
    /// The web additions to the design-surface protocol (docs/WEB-VIEWS.md section 4.4, WV-9b/WV-10) that the desktop
    /// protocol (<c>DesignSurfaceProtocol</c> of Kubuno.Views, whose shapes the page speaks for everything else) has no
    /// counterpart for, and the parse of the page's web-only messages. Pure.
    /// </summary>
    public static class WebDesignMessages
    {
        /// <summary>The <c>surfaceInfo</c> version of the web surface (<c>version</c> of its first message).</summary>
        public const int SurfaceInfoVersion = 1;

        /// <summary>The plan ABI of <c>@kubuno/views</c> this designer speaks (<c>VIEWS_ABI</c> of the runtime).</summary>
        public const string ViewsAbi = "1";

        private static readonly JsonSerializerOptions Wire = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        /// <summary>
        /// <c>setDocumentInfo {file, codeBehind, className, userControls, designData}</c>: what the page needs to compile and
        /// render the buffer like the project's build - the project-relative view path, its code-behind module as imported
        /// from the view's folder (null: none), the class it exports, the project's user controls, and the parsed design data
        /// (<c>&lt;view&gt;.design.json</c>, null when absent or malformed).
        /// </summary>
        public static string EncodeSetDocumentInfo(string file, string? codeBehind, string? className, IEnumerable<WebUserControl> userControls, string? designDataJson)
        {
            var message = new JsonObject
            {
                ["type"] = "setDocumentInfo",
                ["file"] = file,
                ["codeBehind"] = codeBehind,
                ["className"] = className,
                ["userControls"] = new JsonArray((userControls ?? Array.Empty<WebUserControl>()).Select(u => (JsonNode)new JsonObject { ["name"] = u.Name, ["module"] = u.Module }).ToArray()),
                ["designData"] = ParseOrNull(designDataJson),
            };
            return message.ToJsonString(Wire);
        }

        /// <summary>
        /// <c>setVsTheme {mode, colors}</c> - Visual Studio's theme (<c>"dark"</c>/<c>"light"</c>) and the environment colours
        /// the page's chrome (canvas, toolbar, adorners, insertion markers) is drawn with, as <c>#rrggbb</c>, sorted by name.
        /// </summary>
        public static string EncodeSetVsTheme(bool dark, IReadOnlyDictionary<string, string> colors)
        {
            var ordered = new JsonObject();
            foreach (var color in (colors ?? new Dictionary<string, string>()).OrderBy(c => c.Key, StringComparer.Ordinal))
            {
                ordered[color.Key] = color.Value;
            }

            return new JsonObject { ["type"] = "setVsTheme", ["mode"] = dark ? "dark" : "light", ["colors"] = ordered }.ToJsonString(Wire);
        }

        /// <summary><c>setLanguage {lang}</c>: the design-time language (Visual Studio's language picker).</summary>
        public static string EncodeSetLanguage(string lang) => JsonSerializer.Serialize(new { type = "setLanguage", lang }, Wire);

        /// <summary><c>setZoom {zoom}</c> (0 = fit).</summary>
        public static string EncodeSetZoom(double zoom) => "{\"type\":\"setZoom\",\"zoom\":" + zoom.ToString("0.###", CultureInfo.InvariantCulture) + "}";

        /// <summary>
        /// The page's <c>surfaceInfo</c>: null when it may be used, else why not (another handshake version, another
        /// target, another plan ABI).
        /// </summary>
        public static string? CheckSurfaceInfo(string? json)
        {
            if (!TryRoot(json, "surfaceInfo", out var root))
            {
                return "not a surfaceInfo message";
            }

            var version = root.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : 0;
            if (version != SurfaceInfoVersion)
            {
                return $"handshake version {version}, expected {SurfaceInfoVersion}";
            }

            var target = root.TryGetProperty("target", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
            if (target != "web")
            {
                return $"surface target '{target}', expected 'web'";
            }

            var views = root.TryGetProperty("views", out var a) ? (a.ValueKind == JsonValueKind.String ? a.GetString() : a.ToString()) : null;
            return views is null || views == ViewsAbi ? null : $"views ABI {views}, expected {ViewsAbi}";
        }

        /// <summary>The <c>mode</c> of a <c>surfaceInfo</c>: <c>project</c> (the project's dev server) or <c>bundled</c>.</summary>
        public static string? SurfaceMode(string? json) =>
            TryRoot(json, "surfaceInfo", out var root) && root.TryGetProperty("mode", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;

        /// <summary>The <c>type</c> of a page message, or null for anything that is not a JSON object with a string <c>type</c>.</summary>
        public static string? TypeOf(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                using var document = JsonDocument.Parse(json!);
                return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String
                    ? type.GetString()
                    : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>A string property of a page message, or null.</summary>
        public static string? StringOf(string? json, string name)
        {
            try
            {
                using var document = JsonDocument.Parse(json ?? string.Empty);
                return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>A boolean property of a page message (false when absent).</summary>
        public static bool FlagOf(string? json, string name)
        {
            try
            {
                using var document = JsonDocument.Parse(json ?? string.Empty);
                return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        /// <summary>
        /// The Windows virtual-key code of a DOM <c>KeyboardEvent.key</c> the page did not use (<c>unhandledKey</c>), for the
        /// designer's key handling (Ctrl+Z / Ctrl+Y); 0 when it has none.
        /// </summary>
        public static int VirtualKeyOf(string? domKey)
        {
            if (string.IsNullOrEmpty(domKey))
            {
                return 0;
            }

            if (domKey!.Length == 1)
            {
                var c = char.ToUpperInvariant(domKey[0]);
                return (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') ? c : 0;
            }

            switch (domKey)
            {
                case "Delete": return 0x2E;
                case "Backspace": return 0x08;
                case "Escape": return 0x1B;
                case "Enter": return 0x0D;
                case "Tab": return 0x09;
                case "ArrowLeft": return 0x25;
                case "ArrowUp": return 0x26;
                case "ArrowRight": return 0x27;
                case "ArrowDown": return 0x28;
                default:
                    if (domKey.Length >= 2 && domKey[0] == 'F' && int.TryParse(domKey.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out var f) && f >= 1 && f <= 24)
                    {
                        return 0x70 + f - 1;
                    }

                    return 0;
            }
        }

        private static JsonNode? ParseOrNull(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                return JsonNode.Parse(json!, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static bool TryRoot(string? json, string type, out JsonElement root)
        {
            root = default;
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                using var document = JsonDocument.Parse(json!);
                if (document.RootElement.ValueKind != JsonValueKind.Object ||
                    !document.RootElement.TryGetProperty("type", out var t) || t.GetString() != type)
                {
                    return false;
                }

                root = document.RootElement.Clone();
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }
}
