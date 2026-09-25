using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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

        private void SendSetText(string text)
        {
            _lastSentText = text;
            SendLine(DesignSurfaceProtocol.EncodeSetText(text));
        }

        private void SendSetDesignMode(bool on)
        {
            _lastSentDesignMode = on;
            SendLine(DesignSurfaceProtocol.EncodeSetDesignMode(on));
        }

        private void SendSelect(string? id)
        {
            _lastSentSelection = id;
            SendLine(DesignSurfaceProtocol.EncodeSelect(id));
        }

        /// <summary>
        /// Writes one already-encoded JSON line to the surface's stdin, best-effort: a surface that has
        /// not started yet, or has already exited, silently drops the write - the next
        /// <see cref="BeginProtocolIo"/> (on restart) resends the pane's last known state (see that
        /// method's own doc), so nothing is permanently lost.
        /// </summary>
        private void SendLine(string json)
        {
            var proc = _surface;
            if (proc == null)
            {
                return;
            }

            try
            {
                proc.StandardInput.WriteLine(json);
                proc.StandardInput.Flush();
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

            if (_lastSentText != null)
            {
                SendSetText(_lastSentText);
            }

            if (_lastSentDesignMode)
            {
                SendSetDesignMode(true);
            }

            if (_lastSentSelection != null)
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
            if (DesignSurfaceProtocol.TryParseSelectionChanged(line, out var elementIds))
            {
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

        public static string EncodeSetDesignMode(bool on) => JsonSerializer.Serialize(new { type = "setDesignMode", on }, WireOptions);

        public static string EncodeSelect(string? id) => JsonSerializer.Serialize(new { type = "select", id }, WireOptions);

        /// <summary>
        /// Parses a `selectionChanged` line: `elementIds` is an empty list for `id: null`/absent, a
        /// single-element list for a string `id` - matching
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

            if (root.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
            {
                var id = idProp.GetString();
                if (!string.IsNullOrEmpty(id))
                {
                    elementIds = new[] { id! };
                }
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
