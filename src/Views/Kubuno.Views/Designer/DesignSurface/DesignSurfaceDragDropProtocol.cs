using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;

namespace Kubuno.Views.Designer.DesignSurface
{
    /// <summary>See <see cref="IProtocolDesignSurfaceHost.UnhandledSurfaceKey"/>.</summary>
    public sealed class DesignSurfaceKeyEventArgs : EventArgs
    {
        public DesignSurfaceKeyEventArgs(int virtualKey, bool control, bool shift, bool alt)
        {
            VirtualKey = virtualKey;
            Control = control;
            Shift = shift;
            Alt = alt;
        }

        public int VirtualKey { get; }

        public bool Control { get; }

        public bool Shift { get; }

        public bool Alt { get; }

        /// <summary>Ctrl+Z.</summary>
        public bool IsUndo => Control && !Shift && !Alt && VirtualKey == 0x5A;

        /// <summary>Ctrl+Y or Ctrl+Shift+Z.</summary>
        public bool IsRedo => Control && !Alt && ((VirtualKey == 0x59 && !Shift) || (VirtualKey == 0x5A && Shift));
    }

    /// <summary>Which kind of gesture a batched <c>editRequests</c> carries - mirrors `kubuno_desktop_views::design::Gesture` field-for-field.</summary>
    public enum DesignSurfaceGesture
    {
        Move,
        Resize,

        /// <summary>Delete on a multi-selection: every op is a <c>removeElement</c> (docs/DESIGNER.md §13).</summary>
        Delete,

        /// <summary>A Layout toolbar / Format menu command (<c>format</c>): every op is a <c>setAttribute</c> (docs/DESIGNER.md §13).</summary>
        Format,
    }

    /// <summary>See <see cref="IProtocolDesignSurfaceHost.EditRequestsReceived"/>.</summary>
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

    /// <summary>See <see cref="IProtocolDesignSurfaceHost.DragDropEditRequested"/>.</summary>
    public sealed class DesignSurfaceDragDropEditRequestedEventArgs : EventArgs
    {
        public DesignSurfaceDragDropEditRequestedEventArgs(DesignSurfaceDragDropOp op)
        {
            Op = op ?? throw new ArgumentNullException(nameof(op));
        }

        public DesignSurfaceDragDropOp Op { get; }
    }

    /// <summary>
    /// The wire shape of `kubuno_desktop_views::design::DropTarget` (a NON-<see langword="null"/>
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

        /// <summary>The new element's placement X, parent-local DIP - <see langword="null"/> for a Flow/other parent (see `kubuno_desktop_views::design::DropTarget::xy`'s own doc).</summary>
        public double? X { get; }

        /// <summary>See <see cref="X"/>.</summary>
        public double? Y { get; }

        public double MarkerLeft { get; }

        public double MarkerTop { get; }

        public double MarkerRight { get; }

        public double MarkerBottom { get; }
    }

    /// <summary>See <c>RustDesignSurfaceHost.DropTargetChanged</c>.</summary>
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
    /// `Kubuno.Desktop.Tests/DesignSurface/RustDesignSurfaceHostDragDropTests.cs`), exactly
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
                case "delete":
                    gesture = DesignSurfaceGesture.Delete;
                    break;
                case "format":
                    gesture = DesignSurfaceGesture.Format;
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
                if (!TryParseBatchOp(opElement, out var op) || op == null)
                {
                    return false;
                }

                list.Add(op);
            }

            ops = list;
            return true;
        }

        /// <summary>One op of an <c>editRequests</c> batch: a <c>setAttribute</c> (move, resize, format) or - docs/DESIGNER.md §13, Delete on a multi-selection - a <c>removeElement</c>.</summary>
        private static bool TryParseBatchOp(JsonElement opElement, out DesignSurfaceEditOp? op)
        {
            if (opElement.ValueKind == JsonValueKind.Object &&
                opElement.TryGetProperty("kind", out var kindProp) && kindProp.ValueKind == JsonValueKind.String && kindProp.GetString() == "removeElement")
            {
                op = null;
                if (!opElement.TryGetProperty("elementId", out var idProp) || idProp.ValueKind != JsonValueKind.String)
                {
                    return false;
                }

                op = new DesignSurfaceEditOp(DesignSurfaceEditOpKind.RemoveElement, idProp.GetString()!);
                return true;
            }

            return TryParseSetAttributeOp(opElement, out op);
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
