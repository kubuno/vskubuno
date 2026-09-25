using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;

namespace Kubuno.VisualStudio.Designer.DesignSurface
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
    /// <para><b>Receiving (surface -&gt; host).</b> <see cref="OnDragDropProtocolLine"/> is a SECOND,
    /// independent listener on the surface process's own <see cref="Process.OutputDataReceived"/> event -
    /// not a change to <c>RustDesignSurfaceHost.Protocol.cs</c>'s own listener (`OnSurfaceProtocolLine`),
    /// which keeps handling `selectionChanged` and a plain `editRequest` carrying `setAttribute`/
    /// `removeElement` exactly as it already did. This file's own listener recognises only the shapes DSG-9
    /// adds that the other one does NOT already parse: the batched `editRequests`, a single `editRequest`
    /// whose `op.kind` is `moveElement`/`insertChild` (`TryParseEditRequest` in the other file returns
    /// `false` for those two kinds, leaving the line otherwise unhandled - see that method's own doc), and
    /// `dropTargetChanged`. The two listeners' recognised shapes are DISJOINT by construction, so a line
    /// is never processed twice. Wiring this SECOND subscription without touching the constructor declared
    /// in `RustDesignSurfaceHost.cs` is done LAZILY instead, from every `Notify*` entry point
    /// (<see cref="EnsureDragDropListenerWired"/>'s own doc has the full reasoning, including why an
    /// instance field initializer - the first thing tried - does not compile).</para>
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
        /// The design-surface <see cref="Process"/> this half of the partial class has wired its OWN
        /// <see cref="Process.OutputDataReceived"/> listener to (<see cref="OnDragDropProtocolLine"/>) -
        /// tracked separately from whatever `RustDesignSurfaceHost.Protocol.cs`'s own subscription does,
        /// so a crash restart (a FRESH <see cref="Process"/> instance - <see cref="ChildReady"/>'s own
        /// doc: "Raised once Child exists after a (re)start") gets re-wired too, instead of silently
        /// keeping a subscription to a dead process's event that will never fire again.
        /// </summary>
        private Process? _dragDropWiredSurface;

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

        /// <summary>`kubuno/dragEnter {component}` - a VS Toolbox drag entered the surface's own window. `component` is a registry element name (`"Button"`).</summary>
        public void NotifyDragEnter(string component)
        {
            EnsureDragDropListenerWired();
            SendLine(DesignSurfaceDragDropProtocol.EncodeDragEnter(component));
        }

        /// <summary>`kubuno/dragOver {x, y}` - the toolbox drag moved to `(x, y)`, surface-client DIP.</summary>
        public void NotifyDragOver(double x, double y)
        {
            EnsureDragDropListenerWired();
            SendLine(DesignSurfaceDragDropProtocol.EncodeDragOver(x, y));
        }

        /// <summary>`kubuno/drop {x, y}` - the toolbox drag was released at `(x, y)`.</summary>
        public void NotifyDrop(double x, double y)
        {
            EnsureDragDropListenerWired();
            SendLine(DesignSurfaceDragDropProtocol.EncodeDrop(x, y));
        }

        /// <summary>`kubuno/dragLeave` - the toolbox drag left the surface's own window.</summary>
        public void NotifyDragLeave()
        {
            EnsureDragDropListenerWired();
            SendLine(DesignSurfaceDragDropProtocol.EncodeDragLeave());
        }

        /// <summary>
        /// Wires <see cref="OnDragDropProtocolLine"/> to the CURRENT <see cref="Surface"/> process, once,
        /// idempotently - called at the top of every <c>Notify*</c> method above rather than from the
        /// constructor: a field initializer cannot call an instance method (C# CS0236 - confirmed live
        /// while building this file), and this file must not touch `RustDesignSurfaceHost.cs`'s own
        /// constructor body either (the orchestrating task's own scope for this package: "don't touch
        /// other C# files"). Calling this from every `Notify*` entry point instead means the listener is
        /// wired the moment ANY toolbox-drag traffic actually happens - and, just as importantly, RE-wired
        /// after a crash restart: <see cref="ReferenceEquals(object, object)"/> against
        /// <see cref="_dragDropWiredSurface"/> is `false` for a fresh <see cref="Process"/> instance
        /// (`OnSurfaceExited`'s own restart-with-backoff, in `RustDesignSurfaceHost.cs`, reassigns the
        /// process the NEXT time this runs), so the very next `Notify*` call after a mid-drag crash
        /// re-attaches to the new process automatically, with no separate <see cref="ChildReady"/>
        /// subscription needed at all.
        /// </summary>
        private void EnsureDragDropListenerWired()
        {
            var proc = Surface;
            if (proc == null || ReferenceEquals(proc, _dragDropWiredSurface))
            {
                return;
            }

            _dragDropWiredSurface = proc;
            proc.OutputDataReceived += OnDragDropProtocolLine;
        }

        /// <summary>
        /// One line of the surface's stdout, DSG-9 shapes only (see the class doc for why this listener's
        /// recognised set is disjoint from `RustDesignSurfaceHost.Protocol.cs`'s own
        /// `OnSurfaceProtocolLine`). Runs on a .NET thread-pool thread (async pipe read, same as every
        /// other listener on this process), raises on the UI thread via
        /// <see cref="System.Windows.Threading.Dispatcher.BeginInvoke(System.Delegate)"/> for the same
        /// reason `OnSurfaceExited`/`OnSurfaceProtocolLine` already do (a subscriber may touch WPF/VS
        /// objects that require it). A line matching none of DSG-9's three shapes is silently ignored -
        /// it either belongs to the OTHER listener, or is a malformed/unrecognised line neither
        /// understands (already logged once by that listener; logging it again here would double the
        /// noise for no benefit).
        /// </summary>
        private void OnDragDropProtocolLine(object? sender, DataReceivedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.Data))
            {
                return;
            }

            var line = e.Data;

            if (DesignSurfaceDragDropProtocol.TryParseEditRequestsBatch(line, out var ops, out var gesture))
            {
#pragma warning disable VSTHRD001, VSTHRD110
                Dispatcher.BeginInvoke(new Action(() => EditRequestsReceived?.Invoke(this, new DesignSurfaceEditRequestsReceivedEventArgs(ops, gesture))));
#pragma warning restore VSTHRD001, VSTHRD110
                return;
            }

            if (DesignSurfaceDragDropProtocol.TryParseDragDropEditRequest(line, out var dragDropOp) && dragDropOp != null)
            {
#pragma warning disable VSTHRD001, VSTHRD110
                Dispatcher.BeginInvoke(new Action(() => DragDropEditRequested?.Invoke(this, new DesignSurfaceDragDropEditRequestedEventArgs(dragDropOp))));
#pragma warning restore VSTHRD001, VSTHRD110
                return;
            }

            if (DesignSurfaceDragDropProtocol.TryParseDropTargetChanged(line, out var target))
            {
#pragma warning disable VSTHRD001, VSTHRD110
                Dispatcher.BeginInvoke(new Action(() => DropTargetChanged?.Invoke(this, new DesignSurfaceDropTargetChangedEventArgs(target))));
#pragma warning restore VSTHRD001, VSTHRD110
            }
        }
    }

    /// <summary>Which kind of gesture a batched <c>editRequests</c> carries - mirrors `kubuno_views::design::Gesture` field-for-field.</summary>
    public enum DesignSurfaceGesture
    {
        Move,
        Resize,
    }

    /// <summary>See <see cref="RustDesignSurfaceHost.EditRequestsReceived"/>.</summary>
    public sealed class DesignSurfaceEditRequestsReceivedEventArgs : EventArgs
    {
        public DesignSurfaceEditRequestsReceivedEventArgs(IReadOnlyList<DesignSurfaceEditOp> ops, DesignSurfaceGesture gesture)
        {
            Ops = ops ?? throw new ArgumentNullException(nameof(ops));
            Gesture = gesture;
        }

        public IReadOnlyList<DesignSurfaceEditOp> Ops { get; }

        public DesignSurfaceGesture Gesture { get; }
    }

    /// <summary>The two `kubuno/applyEdit` op kinds DSG-9 adds (`DESIGNER.md` §8's `moveElement`/`insertChild`) that <see cref="DesignSurfaceEditOpKind"/> (a fixed enum declared in `RustDesignSurfaceHost.Protocol.cs`, which this package must not touch) cannot be extended to also carry.</summary>
    public enum DesignSurfaceDragDropOpKind
    {
        MoveElement,
        InsertChild,
    }

    /// <summary>
    /// One `moveElement`/`insertChild` op (DSG-2's shape, `DESIGNER.md` §8) a DSG-9 Flow reorder or
    /// toolbox drop produced. <see cref="ElementId"/>/<see cref="NewParentId"/> are set only for
    /// <see cref="DesignSurfaceDragDropOpKind.MoveElement"/>; <see cref="ParentId"/>/<see cref="Xml"/>
    /// only for <see cref="DesignSurfaceDragDropOpKind.InsertChild"/>; <see cref="Index"/> is set for
    /// both.
    /// </summary>
    public sealed class DesignSurfaceDragDropOp
    {
        public DesignSurfaceDragDropOp(DesignSurfaceDragDropOpKind kind, string? elementId = null, string? newParentId = null, string? parentId = null, int? index = null, string? xml = null)
        {
            Kind = kind;
            ElementId = elementId;
            NewParentId = newParentId;
            ParentId = parentId;
            Index = index;
            Xml = xml;
        }

        public DesignSurfaceDragDropOpKind Kind { get; }

        public string? ElementId { get; }

        public string? NewParentId { get; }

        public string? ParentId { get; }

        public int? Index { get; }

        public string? Xml { get; }
    }

    /// <summary>See <see cref="RustDesignSurfaceHost.DragDropEditRequested"/>.</summary>
    public sealed class DesignSurfaceDragDropEditRequestedEventArgs : EventArgs
    {
        public DesignSurfaceDragDropEditRequestedEventArgs(DesignSurfaceDragDropOp op)
        {
            Op = op ?? throw new ArgumentNullException(nameof(op));
        }

        public DesignSurfaceDragDropOp Op { get; }
    }

    /// <summary>
    /// The wire shape of `kubuno_views::design::DropTarget` (a NON-<see langword="null"/>
    /// <c>target</c> in a `dropTargetChanged` line) - where a toolbox drop would land right now, and
    /// whether it is currently allowed (`DESIGNER.md` DSG-9 item 3's "not allowed" marker/cursor).
    /// </summary>
    public sealed class DesignSurfaceDropTarget
    {
        public DesignSurfaceDropTarget(bool valid, string parentId, int index, double? x, double? y, double markerLeft, double markerTop, double markerRight, double markerBottom)
        {
            Valid = valid;
            ParentId = parentId ?? throw new ArgumentNullException(nameof(parentId));
            Index = index;
            X = x;
            Y = y;
            MarkerLeft = markerLeft;
            MarkerTop = markerTop;
            MarkerRight = markerRight;
            MarkerBottom = markerBottom;
        }

        public bool Valid { get; }

        public string ParentId { get; }

        public int Index { get; }

        /// <summary>The new element's placement X, parent-local DIP - <see langword="null"/> for a Flow/other parent (see `kubuno_views::design::DropTarget::xy`'s own doc).</summary>
        public double? X { get; }

        /// <summary>See <see cref="X"/>.</summary>
        public double? Y { get; }

        public double MarkerLeft { get; }

        public double MarkerTop { get; }

        public double MarkerRight { get; }

        public double MarkerBottom { get; }
    }

    /// <summary>See <see cref="RustDesignSurfaceHost.DropTargetChanged"/>.</summary>
    public sealed class DesignSurfaceDropTargetChangedEventArgs : EventArgs
    {
        public DesignSurfaceDropTargetChangedEventArgs(DesignSurfaceDropTarget? target)
        {
            Target = target;
        }

        /// <summary><see langword="null"/> when there is currently no drop target.</summary>
        public DesignSurfaceDropTarget? Target { get; }
    }

    /// <summary>
    /// The pure encode/parse half of DSG-9's protocol additions - no process, no event, no threading, so
    /// it is directly unit-testable (see this project's test suite,
    /// `Kubuno.VisualStudio.Designer.Tests/DesignSurface/RustDesignSurfaceHostDragDropTests.cs`), exactly
    /// mirroring how `DesignSurfaceProtocol` (`RustDesignSurfaceHost.Protocol.cs`) is structured for
    /// DSG-6's own messages - kept as a SEPARATE static class rather than added to that one, since this
    /// package must not touch that file.
    /// </summary>
    public static class DesignSurfaceDragDropProtocol
    {
        public static string EncodeDragEnter(string component) => JsonSerializer.Serialize(new { type = "dragEnter", component });

        public static string EncodeDragOver(double x, double y) => JsonSerializer.Serialize(new { type = "dragOver", x, y });

        public static string EncodeDrop(double x, double y) => JsonSerializer.Serialize(new { type = "drop", x, y });

        public static string EncodeDragLeave() => JsonSerializer.Serialize(new { type = "dragLeave" });

        /// <summary>
        /// Parses an `editRequests` line: `ops` (every entry a plain `setAttribute` - see
        /// <see cref="DesignSurfaceEditRequestsReceivedEventArgs"/>'s own doc for why nothing else is
        /// ever expected in this shape) and `gesture` (`"move"`/`"resize"` only). `false` for a blank
        /// line, invalid JSON, a different `type`, a missing/unrecognised `gesture`, a missing/non-array
        /// `ops`, or any entry of `ops` that is not a well-formed `setAttribute` op - never throws.
        /// </summary>
        public static bool TryParseEditRequestsBatch(string line, out IReadOnlyList<DesignSurfaceEditOp> ops, out DesignSurfaceGesture gesture)
        {
            ops = Array.Empty<DesignSurfaceEditOp>();
            gesture = DesignSurfaceGesture.Move;
            if (!TryParseAsType(line, "editRequests", out var root))
            {
                return false;
            }

            if (!root.TryGetProperty("gesture", out var gestureProp) || gestureProp.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            switch (gestureProp.GetString())
            {
                case "move":
                    gesture = DesignSurfaceGesture.Move;
                    break;
                case "resize":
                    gesture = DesignSurfaceGesture.Resize;
                    break;
                default:
                    return false;
            }

            if (!root.TryGetProperty("ops", out var opsProp) || opsProp.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var list = new List<DesignSurfaceEditOp>();
            foreach (var opElement in opsProp.EnumerateArray())
            {
                if (!TryParseSetAttributeOp(opElement, out var op) || op == null)
                {
                    return false;
                }

                list.Add(op);
            }

            ops = list;
            return true;
        }

        private static bool TryParseSetAttributeOp(JsonElement opElement, out DesignSurfaceEditOp? op)
        {
            op = null;
            if (!opElement.TryGetProperty("kind", out var kindProp) || kindProp.ValueKind != JsonValueKind.String || kindProp.GetString() != "setAttribute")
            {
                return false;
            }

            if (!opElement.TryGetProperty("elementId", out var idProp) || idProp.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            if (!opElement.TryGetProperty("name", out var nameProp) || nameProp.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            if (!opElement.TryGetProperty("value", out var valueProp) || valueProp.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            op = new DesignSurfaceEditOp(DesignSurfaceEditOpKind.SetAttribute, idProp.GetString()!, nameProp.GetString(), valueProp.GetString());
            return true;
        }

        /// <summary>
        /// Parses a single `editRequest` line whose `op.kind` is `moveElement` or `insertChild` - `false`
        /// for every other `op.kind` (including `setAttribute`/`removeElement`, which belong to
        /// `DesignSurfaceProtocol.TryParseEditRequest` instead), a blank line, invalid JSON, a different
        /// `type`, or one missing a required field for its own kind - never throws.
        /// </summary>
        public static bool TryParseDragDropEditRequest(string line, out DesignSurfaceDragDropOp? op)
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

            switch (kindProp.GetString())
            {
                case "moveElement":
                    if (opProp.TryGetProperty("elementId", out var eid) && eid.ValueKind == JsonValueKind.String
                        && opProp.TryGetProperty("newParentId", out var npid) && npid.ValueKind == JsonValueKind.String
                        && opProp.TryGetProperty("index", out var idx) && idx.ValueKind == JsonValueKind.Number)
                    {
                        op = new DesignSurfaceDragDropOp(DesignSurfaceDragDropOpKind.MoveElement, elementId: eid.GetString(), newParentId: npid.GetString(), index: idx.GetInt32());
                        return true;
                    }

                    return false;
                case "insertChild":
                    if (opProp.TryGetProperty("parentId", out var pid) && pid.ValueKind == JsonValueKind.String
                        && opProp.TryGetProperty("index", out var idx2) && idx2.ValueKind == JsonValueKind.Number
                        && opProp.TryGetProperty("xml", out var xml) && xml.ValueKind == JsonValueKind.String)
                    {
                        op = new DesignSurfaceDragDropOp(DesignSurfaceDragDropOpKind.InsertChild, parentId: pid.GetString(), index: idx2.GetInt32(), xml: xml.GetString());
                        return true;
                    }

                    return false;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Parses a `dropTargetChanged` line: <paramref name="target"/> is <see langword="null"/> for a
        /// `target: null` payload (a VALID, "cleared" state - still returns <see langword="true"/>) as
        /// well as for a blank line/invalid JSON/wrong `type`/malformed object (an INVALID parse - returns
        /// <see langword="false"/>); never throws.
        /// </summary>
        public static bool TryParseDropTargetChanged(string line, out DesignSurfaceDropTarget? target)
        {
            target = null;
            if (!TryParseAsType(line, "dropTargetChanged", out var root))
            {
                return false;
            }

            if (!root.TryGetProperty("target", out var targetProp))
            {
                return false;
            }

            if (targetProp.ValueKind == JsonValueKind.Null)
            {
                return true;
            }

            if (targetProp.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!targetProp.TryGetProperty("valid", out var validProp) || (validProp.ValueKind != JsonValueKind.True && validProp.ValueKind != JsonValueKind.False))
            {
                return false;
            }

            if (!targetProp.TryGetProperty("parentId", out var parentIdProp) || parentIdProp.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            if (!targetProp.TryGetProperty("index", out var indexProp) || indexProp.ValueKind != JsonValueKind.Number)
            {
                return false;
            }

            if (!targetProp.TryGetProperty("marker", out var markerProp) || markerProp.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!TryGetDouble(markerProp, "left", out var left) || !TryGetDouble(markerProp, "top", out var top)
                || !TryGetDouble(markerProp, "right", out var right) || !TryGetDouble(markerProp, "bottom", out var bottom))
            {
                return false;
            }

            double? x = null;
            double? y = null;
            if (targetProp.TryGetProperty("xy", out var xyProp) && xyProp.ValueKind == JsonValueKind.Array && xyProp.GetArrayLength() == 2)
            {
                var e0 = xyProp[0];
                var e1 = xyProp[1];
                if (e0.ValueKind == JsonValueKind.Number && e1.ValueKind == JsonValueKind.Number)
                {
                    x = e0.GetDouble();
                    y = e1.GetDouble();
                }
            }

            target = new DesignSurfaceDropTarget(validProp.GetBoolean(), parentIdProp.GetString()!, indexProp.GetInt32(), x, y, left, top, right, bottom);
            return true;
        }

        private static bool TryGetDouble(JsonElement obj, string name, out double value)
        {
            value = 0;
            if (!obj.TryGetProperty(name, out var prop) || prop.ValueKind != JsonValueKind.Number)
            {
                return false;
            }

            value = prop.GetDouble();
            return true;
        }

        /// <summary>
        /// Parses `line` as JSON and checks its `type` property equals `expectedType` - mirrors
        /// `DesignSurfaceProtocol`'s own identically-purposed private helper (duplicated rather than
        /// shared, since that class's own copy is private to IT, and this package must not touch that
        /// file to make it otherwise reachable - four lines, not worth a third shared file for).
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
