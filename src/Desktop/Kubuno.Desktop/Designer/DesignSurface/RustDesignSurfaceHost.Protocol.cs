using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Kubuno.Views.Logging;
using Kubuno.Views.Designer;
using Kubuno.Views.Designer.DesignSurface;

namespace Kubuno.Desktop.Designer.DesignSurface
{
    /// <summary>
    /// The DSG-6 IPC protocol (`vskubuno/docs/DESIGNER.md`'s "DSG-6 protocol" section): line-delimited
    /// JSON on the design surface's own stdin (host -> surface: `setText`/`setDesignMode`/`select`) and
    /// stdout (surface -> host: `selectionChanged`/`editRequest`), matching exactly what
    /// `kubuno-desktop-views/src/protocol.rs` implements on the Rust side. Split out from
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
            // The preview kept on screen if the surface restarts follows the edits (RustDesignSurfaceHost.Status.cs).
            ScheduleSnapshot();
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

            // The canvas around the view in the colours of Visual Studio's theme (and on every theme change).
            SendCanvasTheme();

            // The project's resource files and the design-time language (docs/RESOURCES.md), before the text.
            SendResources(force: true);

            // The designer options that change what the surface draws, before anything is drawn.
            SendLine(DesignSurfaceProtocol.EncodeSetDesignOptions(Kubuno.Views.Designer.Options.DesignerOptionsHost.Current?.ShowDesignOutlines ?? true));

            // The designer's zoom (a restarted surface keeps it).
            if (_zoom != 1.0)
            {
                SendZoom();
            }

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

            // What the preview shows of the text, and its diagnostics (docs/DESIGNER.md section 17) -
            // RustDesignSurfaceHost.Status.cs.
            if (TryHandleRenderStatusLine(line))
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
}
