using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Kubuno.Views.Logging;

namespace Kubuno.Views.Designer.DesignSurface
{
    /// <summary>One `kubuno/applyEdit` op (DSG-2's shape, `vskubuno/docs/DESIGNER.md` §8) a design-mode
    /// gesture produced - `SetAttribute` for a nudge/resize `X`/`Y`/`Width`/`Height`, `RemoveElement` for
    /// Delete. Mirrors `kubuno_views::design::EditOp` on the Rust side field-for-field.</summary>
    public enum DesignSurfaceEditOpKind
    {
        SetAttribute,
        RemoveElement,
    }

    /// <summary>See <see cref="DesignSurfaceEditOpKind"/>. <see cref="Name"/>/<see cref="Value"/> are
    /// <see langword="null"/> for a <see cref="DesignSurfaceEditOpKind.RemoveElement"/> op.</summary>
    public sealed class DesignSurfaceEditOp
    {
        public DesignSurfaceEditOp(DesignSurfaceEditOpKind kind, string elementId, string? name = null, string? value = null)
        {
            Kind = kind;
            ElementId = elementId ?? throw new ArgumentNullException(nameof(elementId));
            Name = name;
            Value = value;
        }

        public DesignSurfaceEditOpKind Kind { get; }

        public string ElementId { get; }

        public string? Name { get; }

        public string? Value { get; }
    }

    /// <summary>See <see cref="IDesignSurfaceHost.EditRequested"/>.</summary>
    public sealed class DesignSurfaceEditRequestedEventArgs : EventArgs
    {
        public DesignSurfaceEditRequestedEventArgs(DesignSurfaceEditOp op)
        {
            Op = op ?? throw new ArgumentNullException(nameof(op));
        }

        public DesignSurfaceEditOp Op { get; }
    }

    /// <summary>
    /// The pure encode/parse half of the DSG-6 protocol - no process, no event, no threading, so it is
    /// directly unit-testable (see this project's test suite,
    /// `Kubuno.Desktop.Tests/DesignSurface/RustDesignSurfaceHostProtocolTests.cs`) with a
    /// plain JSON string in, a plain value out. <c>RustDesignSurfaceHost</c>'s own partial class
    /// above is the only production caller.
    /// </summary>
    public static class DesignSurfaceProtocol
    {
        /// <summary>
        /// <see cref="System.Text.Json.JsonSerializer"/>'s default encoder HTML-escapes `&lt;`/`&gt;`/`&amp;`
        /// (and a few other code points) for browser-embedding safety - `serde_json` on the Rust side
        /// (`kubuno_views::protocol`) does not escape any of these, so a line like `setText` for a
        /// `.kbview` document (all angle brackets) would otherwise come out byte-for-byte different from
        /// what `kubuno_views::protocol`'s own round-trip tests assert (`vskubuno/docs/DESIGNER.md`'s
        /// "DSG-6 protocol" §9, "unit-tests every wire shape byte-for-byte"). `UnsafeRelaxedJsonEscaping`
        /// is safe here: this JSON never renders in a browser, only parsed back by `view_embed`'s own
        /// `serde_json` on the other end of a pipe.
        /// </summary>
        private static readonly JsonSerializerOptions WireOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        public static string EncodeSetText(string text) => JsonSerializer.Serialize(new { type = "setText", text }, WireOptions);

        /// <summary>`setText` with the folder of the view file (`baseDir`), against which the surface resolves the relative image paths the view names.</summary>
        public static string EncodeSetText(string text, string? baseDir) =>
            string.IsNullOrEmpty(baseDir) ? EncodeSetText(text) : JsonSerializer.Serialize(new { type = "setText", text, baseDir }, WireOptions);

        public static string EncodeSetDesignMode(bool on) => JsonSerializer.Serialize(new { type = "setDesignMode", on }, WireOptions);

        /// <summary>
        /// <c>setDesignOptions {containerOutlines}</c>: the designer options that change what the surface draws
        /// (Tools &gt; Options &gt; Kubuno &gt; Designer; <c>kubuno_views::protocol::HostMessage::SetDesignOptions</c>).
        /// </summary>
        public static string EncodeSetDesignOptions(bool containerOutlines) => JsonSerializer.Serialize(new { type = "setDesignOptions", containerOutlines }, WireOptions);

        public static string EncodeSelect(string? id) => JsonSerializer.Serialize(new { type = "select", id }, WireOptions);

        /// <summary><c>selectMany {ids, primary}</c> (docs/DESIGNER.md §13).</summary>
        public static string EncodeSelectMany(IReadOnlyList<string> ids, string? primary) => JsonSerializer.Serialize(new { type = "selectMany", ids, primary }, WireOptions);

        /// <summary><c>format {command}</c>: a Layout toolbar / Format menu command, by its surface name (docs/DESIGNER.md §13).</summary>
        public static string EncodeFormat(string command) => JsonSerializer.Serialize(new { type = "format", command }, WireOptions);

        /// <summary><c>{"type":"projectComponents","components":[…]}</c> (EVT-7b) - the array is the language server's export entries, passed through verbatim.</summary>
        public static string EncodeProjectComponents(string componentsJsonArray)
        {
            var array = string.IsNullOrWhiteSpace(componentsJsonArray) ? "[]" : componentsJsonArray.Trim();
            return "{\"type\":\"projectComponents\",\"components\":" + array + "}";
        }

        /// <summary>
        /// Parses a `selectionChanged` line: `elementIds` is an empty list for `id: null`/absent, a
        /// single-element list for a string `id` (including `""`, the root element) - matching
        /// <see cref="DesignSurfaceSelectionChangedEventArgs"/>'s own multi-select-shaped constructor
        /// (that type's own doc: "the element-id scheme ... is therefore an opaque string list"). `false`
        /// (and an empty list) for a blank line, invalid JSON, a different `type`, or one with a non-string
        /// `id` - never throws.
        /// </summary>
        public static bool TryParseSelectionChanged(string line, out IReadOnlyList<string> elementIds)
        {
            elementIds = Array.Empty<string>();
            if (!TryParseAsType(line, "selectionChanged", out var root))
            {
                return false;
            }

            // "" is a real id: the root element - the view itself, selected by a click on the design
            // canvas or on the view frame's title bar (docs/DESIGNER.md §12).
            if (root.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String && idProp.GetString() is { } id)
            {
                // docs/DESIGNER.md §13: `ids` is the whole multi-selection; the primary (`id`) always comes first here.
                var list = new List<string> { id };
                if (root.TryGetProperty("ids", out var idsProp) && idsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in idsProp.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String && item.GetString() is { } other && !list.Contains(other))
                        {
                            list.Add(other);
                        }
                    }
                }

                elementIds = list;
            }

            return true;
        }

        /// <summary>
        /// Parses an `editRequest` line into its <see cref="DesignSurfaceEditOp"/> - `false` (and
        /// `op: null`) for a blank line, invalid JSON, a different `type`, an unrecognised `op.kind`, or
        /// one missing a required field (`elementId` always; `name`/`value` for `setAttribute`) - never
        /// throws.
        /// </summary>
        public static bool TryParseEditRequest(string line, out DesignSurfaceEditOp? op)
        {
            op = null;
            if (!TryParseAsType(line, "editRequest", out var root))
            {
                return false;
            }

            if (!root.TryGetProperty("op", out var opProp))
            {
                return false;
            }

            if (!opProp.TryGetProperty("kind", out var kindProp) || kindProp.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            if (!opProp.TryGetProperty("elementId", out var idProp) || idProp.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var elementId = idProp.GetString()!;

            switch (kindProp.GetString())
            {
                case "setAttribute":
                    if (opProp.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String
                        && opProp.TryGetProperty("value", out var valueProp) && valueProp.ValueKind == JsonValueKind.String)
                    {
                        op = new DesignSurfaceEditOp(DesignSurfaceEditOpKind.SetAttribute, elementId, nameProp.GetString(), valueProp.GetString());
                        return true;
                    }

                    return false;
                case "removeElement":
                    op = new DesignSurfaceEditOp(DesignSurfaceEditOpKind.RemoveElement, elementId);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Parses a <c>renderStatus</c> line (docs/DESIGNER.md section 17): what the preview shows of the
        /// current text and its diagnostics. <c>false</c> for any other line, an unknown <c>state</c>, or a
        /// malformed one; a diagnostic missing its message or position is skipped - never throws.
        /// </summary>
        public static bool TryParseRenderStatus(string line, out DesignSurfaceRenderStatus? status)
        {
            status = null;
            if (!TryParseAsType(line, "renderStatus", out var root))
            {
                return false;
            }

            if (!root.TryGetProperty("state", out var stateProp) || stateProp.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            DesignSurfaceRenderState state;
            switch (stateProp.GetString())
            {
                case "clean": state = DesignSurfaceRenderState.Clean; break;
                case "tolerant": state = DesignSurfaceRenderState.Tolerant; break;
                case "recovered": state = DesignSurfaceRenderState.Recovered; break;
                case "stale": state = DesignSurfaceRenderState.Stale; break;
                case "empty": state = DesignSurfaceRenderState.Empty; break;
                default: return false;
            }

            var diagnostics = new List<DesignSurfaceDiagnostic>();
            if (root.TryGetProperty("diagnostics", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var d in list.EnumerateArray())
                {
                    if (d.ValueKind != JsonValueKind.Object
                        || !TryInt(d, "line", out var lineNumber) || !TryInt(d, "column", out var column)
                        || !d.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    diagnostics.Add(ParseDiagnostic(d, lineNumber, column, message.GetString() ?? string.Empty));
                }
            }

            status = new DesignSurfaceRenderStatus(state, diagnostics);
            return true;
        }

        private static DesignSurfaceDiagnostic ParseDiagnostic(JsonElement d, int line, int column, string message)
        {
            var endLine = TryInt(d, "endLine", out var el) ? el : line;
            var endColumn = TryInt(d, "endColumn", out var ec) ? ec : column;
            var syntax = d.TryGetProperty("syntax", out var s) && s.ValueKind == JsonValueKind.True;
            return new DesignSurfaceDiagnostic(line, column, endLine, endColumn, message, OptionalString(d, "code"), OptionalString(d, "element"), syntax);
        }

        /// <summary>
        /// Parses a <c>goToSource</c> line (docs/DESIGNER.md section 17): a click on an element's warning marker,
        /// whose finding the XML pane shows. <c>false</c> for any other line or a malformed one - never throws.
        /// </summary>
        public static bool TryParseGoToSource(string line, out DesignSurfaceDiagnostic? diagnostic)
        {
            diagnostic = null;
            if (!TryParseAsType(line, "goToSource", out var root)
                || !root.TryGetProperty("diagnostic", out var d) || d.ValueKind != JsonValueKind.Object
                || !TryInt(d, "line", out var lineNumber) || !TryInt(d, "column", out var column))
            {
                return false;
            }

            var message = OptionalString(d, "message") ?? string.Empty;
            diagnostic = ParseDiagnostic(d, lineNumber, column, message);
            return true;
        }

        private static bool TryInt(JsonElement element, string name, out int value)
        {
            value = 0;
            return element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out value);
        }

        private static string? OptionalString(JsonElement element, string name) =>
            element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;

        /// <summary>The <c>surfaceInfo</c> handshake version this host speaks (<c>SURFACE_INFO_VERSION</c> in <c>view_embed.rs</c>).</summary>
        public const int SurfaceInfoVersion = 1;

        /// <summary>
        /// Parses the <c>surfaceInfo</c> handshake (docs/DESIGNER.md section 15), the first line a surface
        /// writes: <c>{type, version, uiDll, uiDllSha256}</c> - the loaded <c>kubuno_ui-&lt;hash&gt;.dll</c>'s path and
        /// the SHA-256 of the one the exe was linked against (null when not built by the design build).
        /// </summary>
        public static bool TryParseSurfaceInfo(string line, out int version, out string? uiDll, out string? uiDllSha256)
        {
            version = 0;
            uiDll = null;
            uiDllSha256 = null;
            if (!TryParseAsType(line, "surfaceInfo", out var root))
            {
                return false;
            }

            if (root.TryGetProperty("version", out var versionProp) && versionProp.ValueKind == JsonValueKind.Number && versionProp.TryGetInt32(out var v))
            {
                version = v;
            }

            if (root.TryGetProperty("uiDll", out var dllProp) && dllProp.ValueKind == JsonValueKind.String)
            {
                uiDll = dllProp.GetString();
            }

            if (root.TryGetProperty("uiDllSha256", out var shaProp) && shaProp.ValueKind == JsonValueKind.String)
            {
                uiDllSha256 = shaProp.GetString();
            }

            return true;
        }

        /// <summary>
        /// The ABI check of the <c>surfaceInfo</c> handshake: <see langword="null"/> when the surface may run,
        /// else why it must not. The DLL it loaded must be the copy next to its exe (never another
        /// build of <c>kubuno_ui</c> found on PATH), and when a hash is known - the design build's, recorded by the
        /// host (<paramref name="expectedSha256"/>) and embedded in the exe (<paramref name="reportedSha256"/>) -
        /// the loaded file must have it. <paramref name="loadedSha256"/> hashes the loaded file (injectable
        /// for tests); it is only called when a hash is expected.
        /// </summary>
        public static string? CheckSurfaceInfo(int version, string? uiDll, string? reportedSha256, string exePath, string? expectedSha256, Func<string, string?> loadedSha256)
        {
            if (version != SurfaceInfoVersion)
            {
                return $"handshake version {version}, expected {SurfaceInfoVersion}";
            }

            if (string.IsNullOrEmpty(uiDll))
            {
                return "the surface did not report its kubuno_ui.dll";
            }

            var exeDirectory = System.IO.Path.GetDirectoryName(exePath) ?? string.Empty;
            if (!string.Equals(System.IO.Path.GetDirectoryName(uiDll), exeDirectory, StringComparison.OrdinalIgnoreCase))
            {
                return $"loaded {uiDll} instead of the copy in {exeDirectory}";
            }

            if (expectedSha256 is not null && reportedSha256 is not null && !string.Equals(expectedSha256, reportedSha256, StringComparison.OrdinalIgnoreCase))
            {
                return $"the exe was linked against {Short(reportedSha256)}, the design build recorded {Short(expectedSha256)}";
            }

            var expected = expectedSha256 ?? reportedSha256;
            if (expected is null)
            {
                return null;
            }

            var actual = loadedSha256(uiDll!);
            if (actual is null)
            {
                return $"{uiDll} could not be read";
            }

            return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)
                ? null
                : $"loaded {System.IO.Path.GetFileName(uiDll)} {Short(actual)}, linked against {Short(expected)}";
        }

        private static string Short(string sha) => sha.Length > 12 ? sha.Substring(0, 12) : sha;

        /// <summary>
        /// Parses `line` as JSON and checks its `type` property equals `expectedType` - the one place
        /// both `TryParse*` methods above share the "blank/invalid JSON/wrong `type`, never throw" rule.
        /// `root` is a DETACHED clone (<see cref="JsonElement.Clone"/>): the backing <see cref="JsonDocument"/>
        /// is disposed before this method returns, so a caller reading `root`'s own properties afterwards
        /// must not see it torn down from under them.
        /// </summary>
        private static bool TryParseAsType(string line, string expectedType, out JsonElement root)
        {
            root = default;
            if (string.IsNullOrWhiteSpace(line))
            {
                return false;
            }

            try
            {
                using var doc = JsonDocument.Parse(line);
                if (!doc.RootElement.TryGetProperty("type", out var typeProp) || typeProp.GetString() != expectedType)
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
    }
}
