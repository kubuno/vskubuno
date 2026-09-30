using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Kubuno.VisualStudio.Views.Logging;

namespace Kubuno.VisualStudio.Designer.DesignSurface
{
    /// <summary>
    /// The DSG-6 IPC protocol (`vskubuno/docs/DESIGNER.md`'s "DSG-6 protocol" section): line-delimited
    /// JSON on the design surface's own stdin (host -> surface: `setText`/`setDesignMode`/`select`) and
    /// stdout (surface -> host: `selectionChanged`/`editRequest`), matching exactly what
    /// `kubuno-views/src/protocol.rs` implements on the Rust side. Split out from
    /// <c>RustDesignSurfaceHost.cs</c> (a `partial class` - see that file's own class-level comment) so
    /// this work does not collide with concurrent changes to that file's keyboard-forwarding code.
    /// </summary>
    public sealed partial class RustDesignSurfaceHost
    {
        private string? _lastSentText;
        private bool _lastSentDesignMode;
        private string? _lastSentSelection;

        /// <summary>
        /// Raised when Delete or a nudging arrow on the design surface produces an edit request (DSG-6)
        /// - forward <see cref="DesignSurfaceEditRequestedEventArgs.Op"/> to `kubuno-views-ls`'s
        /// `kubuno/applyEdit` (not this class's job: see that type's own doc).
        /// </summary>
        public event EventHandler<DesignSurfaceEditRequestedEventArgs>? EditRequested;

        /// <summary>Turns design mode on/off on the surface (`kubuno/setDesignMode`).</summary>
        public void SetDesignMode(bool on) => SendSetDesignMode(on);

        /// <summary>Host-driven selection (`kubuno/select`) - e.g. the XML pane's caret moved onto a
        /// different element. <see langword="null"/> clears the selection.</summary>
        public void Select(string? elementId) => SendSelect(elementId);

        /// <summary>Host-driven multi-selection (<c>selectMany</c>, docs/DESIGNER.md §13).</summary>
        public void SelectMany(IReadOnlyList<string> elementIds, string? primary)
        {
            _lastSentSelection = primary;
            _lastSelectionIds = elementIds;
            SendLine(DesignSurfaceProtocol.EncodeSelectMany(elementIds, primary));
        }

        /// <summary>
        /// The whole last known selection (primary first), whoever made it - the host (<see cref="Select"/>,
        /// <see cref="SelectMany"/>) or the surface (<c>selectionChanged</c>): re-sent to a restarted
        /// surface, so a hot swap onto a rebuilt runtime keeps the selection (docs/DESIGNER.md section 15).
        /// </summary>
        private IReadOnlyList<string>? _lastSelectionIds;

        /// <summary>The last project controls sent (<see cref="SetProjectComponents"/>), resent to a restarted surface before its text.</summary>
        private string? _lastProjectComponents;

        /// <summary>
        /// Tells the surface which controls the project declares (docs/EVENTS.md EVT-7b: the language server's
        /// <c>origin: "project"</c> registry entries, a JSON array): it registers those it does not link as
        /// labelled placeholders and reloads the view, which then compiles instead of failing on an unknown element.
        /// </summary>
        public void SetProjectComponents(string componentsJsonArray)
        {
            _lastProjectComponents = componentsJsonArray;
            SendLine(DesignSurfaceProtocol.EncodeProjectComponents(componentsJsonArray));
        }

        /// <summary>Asks the surface to apply a Layout toolbar / Format menu command to its selection (<c>format</c>); it answers with one <c>editRequests</c> batch.</summary>
        public void Format(string command) => SendLine(DesignSurfaceProtocol.EncodeFormat(command));

        private void SendSetText(string text)
        {
            _lastSentText = text;
            SendLine(DesignSurfaceProtocol.EncodeSetText(text, BaseDirectory));
        }

        private void SendSetDesignMode(bool on)
        {
            _lastSentDesignMode = on;
            SendLine(DesignSurfaceProtocol.EncodeSetDesignMode(on));
        }

        private void SendSelect(string? id)
        {
            _lastSentSelection = id;
            _lastSelectionIds = id is null ? null : new[] { id };
            SendLine(DesignSurfaceProtocol.EncodeSelect(id));
        }

        /// <summary>
        /// Writes one already-encoded JSON line to the surface's stdin, best-effort: a surface that has
        /// not started yet, or has already exited, silently drops the write - the next
        /// <see cref="BeginProtocolIo"/> (on restart) resends the pane's last known state (see that
        /// method's own doc), so nothing is permanently lost.
        /// </summary>
        /// <summary>UTF-8, no byte-order mark - see <see cref="SendLine"/>'s own doc for why the write goes through this instead of <see cref="Process.StandardInput"/>'s own <see cref="System.IO.StreamWriter"/> directly.</summary>
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        private void SendLine(string json)
        {
            var proc = _surface;
            if (proc == null)
            {
                return;
            }

            try
            {
                // Writes raw bytes to the UNDERLYING stream, bypassing `Process.StandardInput`'s own
                // auto-created `StreamWriter` - `ProcessStartInfo.StandardInputEncoding` does not exist on
                // .NET Framework 4.8 (added only in .NET Core 3.0+/.NET 5, confirmed live: `CS0117` on a
                // build attempt), so it cannot be set the way `StandardOutputEncoding` (below, and on
                // `LaunchSurface`'s own `ProcessStartInfo`) is. Without this, that auto-created
                // `StreamWriter`'s default encoding prepended a UTF-8 BOM to the FIRST write on the stream
                // only - confirmed live during DSG-9's own visual check, the surface's `parse_host_message`
                // silently dropping the very first `setText`/`setDesignMode` a freshly launched surface
                // ever received as an unrecognised line prefixed with a U+FEFF byte-order mark, so it
                // never even loaded a document. Writing bytes directly here has no such preamble, on
                // the first write or any other.
                var bytes = Utf8NoBom.GetBytes(json + "\n");
                proc.StandardInput.BaseStream.Write(bytes, 0, bytes.Length);
                proc.StandardInput.BaseStream.Flush();
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
            {
                KubunoViewsLogHost.Current.WriteException("Writing a DSG-6 protocol message to the design surface failed", ex);
            }
        }

        /// <summary>
        /// Starts the async stdout reader (`LaunchSurface`'s own caller) and RESENDS whatever the pane
        /// already told a previous surface process - design mode, the current text, the current
        /// selection - to the fresh one: <see cref="OnSurfaceExited"/>'s restart-with-backoff must not
        /// silently forget that state just because the Rust process crashed and came back.
        /// </summary>
        private void BeginProtocolIo()
        {
            var proc = _surface;
            if (proc == null)
            {
                return;
            }

            proc.OutputDataReceived += OnSurfaceProtocolLine;
            proc.BeginOutputReadLine();

            // The project's controls first: the text then compiles against them.
            if (_lastProjectComponents != null)
            {
                SendLine(DesignSurfaceProtocol.EncodeProjectComponents(_lastProjectComponents));
            }

            if (_lastSentText != null)
            {
                SendSetText(_lastSentText);
            }

            if (_lastSentDesignMode)
            {
                SendSetDesignMode(true);
            }

            var selection = _lastSelectionIds;
            if (selection is { Count: > 1 })
            {
                SelectMany(selection, selection[0]);
            }
            else if (selection is { Count: 1 })
            {
                SendSelect(selection[0]);
            }
            else if (_lastSentSelection != null)
            {
                SendSelect(_lastSentSelection);
            }
        }

        /// <summary>
        /// One line of the surface's stdout (DSG-6 protocol traffic only - stderr stays the plain trace
        /// channel <see cref="LaunchSurface"/>'s `ErrorDataReceived` handler already owns). Runs on a
        /// .NET thread-pool thread (async pipe read, same as that stderr handler): parsing itself is pure
        /// (<see cref="DesignSurfaceProtocol"/>, unit-tested with no live process - see this project's
        /// test suite), but raising <see cref="SelectionChanged"/>/<see cref="EditRequested"/> is
        /// deferred to the UI thread via <see cref="System.Windows.Threading.Dispatcher.BeginInvoke(System.Delegate)"/>,
        /// the same pattern <see cref="OnSurfaceExited"/> already uses and for the same reason (a
        /// subscriber may touch WPF/VS objects that require it).
        /// </summary>
        private void OnSurfaceProtocolLine(object? sender, DataReceivedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.Data))
            {
                return;
            }

            var line = e.Data;
            if (!ReferenceEquals(sender, _surface))
            {
                return; // A late line of a process a hot swap already replaced.
            }

            // The ABI handshake (docs/DESIGNER.md section 15) - RustDesignSurfaceHost.Runtime.cs.
            if (TryHandleSurfaceInfo(line))
            {
                return;
            }

            if (DesignSurfaceProtocol.TryParseSelectionChanged(line, out var elementIds))
            {
                _lastSelectionIds = elementIds;
                _lastSentSelection = elementIds.Count > 0 ? elementIds[0] : null;
#pragma warning disable VSTHRD001, VSTHRD110
                Dispatcher.BeginInvoke(new Action(() => SelectionChanged?.Invoke(this, new DesignSurfaceSelectionChangedEventArgs(elementIds))));
#pragma warning restore VSTHRD001, VSTHRD110
                return;
            }

            if (DesignSurfaceProtocol.TryParseEditRequest(line, out var op) && op != null)
            {
#pragma warning disable VSTHRD001, VSTHRD110
                Dispatcher.BeginInvoke(new Action(() => EditRequested?.Invoke(this, new DesignSurfaceEditRequestedEventArgs(op))));
#pragma warning restore VSTHRD001, VSTHRD110
                return;
            }

            // DSG-9's shapes (editRequests, moveElement/insertChild, dropTargetChanged) - see
            // RustDesignSurfaceHost.DragDrop.cs.
            if (TryDispatchDragDropLine(line))
            {
                return;
            }

            // Context menus and keyboard commands (docs/DESIGNER.md §12) - RustDesignSurfaceHost.ContextMenu.cs.
            if (TryDispatchContextMenuLine(line))
            {
                return;
            }

            // Neither recognised shape matched: a malformed line, or a `type` this version of the host
            // does not know about yet - logged, never thrown (see the class doc's own "never crash the
            // pane over one bad line" posture, already established by `ErrorDataReceived`'s handler).
            KubunoViewsLogHost.Current.WriteLine("[designer] proto: ignoring unrecognised/malformed surface line: " + line);
        }
    }

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

    /// <summary>See <see cref="RustDesignSurfaceHost.EditRequested"/>.</summary>
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
    /// `Kubuno.VisualStudio.Designer.Tests/DesignSurface/RustDesignSurfaceHostProtocolTests.cs`) with a
    /// plain JSON string in, a plain value out. <see cref="RustDesignSurfaceHost"/>'s own partial class
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

        /// <summary>The <c>surfaceInfo</c> handshake version this host speaks (<c>SURFACE_INFO_VERSION</c> in <c>view_embed.rs</c>).</summary>
        public const int SurfaceInfoVersion = 1;

        /// <summary>
        /// Parses the <c>surfaceInfo</c> handshake (docs/DESIGNER.md section 15), the first line a surface
        /// writes: <c>{type, version, uiDll, uiDllSha256}</c> - the loaded <c>kubuno_ui.dll</c>'s path and
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
        /// <c>kubuno_ui.dll</c> found on PATH), and when a hash is known - the design build's, recorded by the
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
                : $"loaded kubuno_ui.dll {Short(actual)}, linked against {Short(expected)}";
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
