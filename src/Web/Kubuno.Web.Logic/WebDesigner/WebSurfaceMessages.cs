using System;
using System.Collections.Generic;

namespace Kubuno.Web.Logic.WebDesigner
{
    // SPIKE (docs/WEB-VIEWS.md, lot WV-9a): the surface -> host messages of the WebView2 design surface, as typed
    // values. The wire shapes are the desktop design surface's (kubuno_desktop_views::protocol, DesignSurfaceProtocol in
    // Kubuno.Desktop) wherever a message exists there; the web additions are marked. WV-9b replaces this with the
    // shared protocol once the designer has moved down to Kubuno.Views (WV-8).

    /// <summary>A message the page posted (<c>window.chrome.webview.postMessage</c>), parsed by <see cref="WebSurfaceProtocol.TryParse"/>.</summary>
    public abstract class WebSurfaceMessage
    {
        protected WebSurfaceMessage(string type) => Type = type;

        /// <summary>The wire <c>type</c>.</summary>
        public string Type { get; }
    }

    /// <summary><c>surfaceInfo {version, target, views, ui}</c>: the handshake, the first message of a loaded page.</summary>
    public sealed class SurfaceInfoMessage : WebSurfaceMessage
    {
        public SurfaceInfoMessage(int version, string? target, string? views, string? ui)
            : base("surfaceInfo")
        {
            Version = version;
            Target = target;
            Views = views;
            Ui = ui;
        }

        public int Version { get; }

        /// <summary><c>"web"</c> for the web surface.</summary>
        public string? Target { get; }

        /// <summary>The <c>@kubuno/views</c> ABI the page speaks.</summary>
        public string? Views { get; }

        /// <summary>The <c>@kubuno/ui</c> version the page renders with.</summary>
        public string? Ui { get; }
    }

    /// <summary>An element's bounds in the page, CSS pixels (= DIP at zoom 1).</summary>
    public readonly struct SurfaceBounds : IEquatable<SurfaceBounds>
    {
        public SurfaceBounds(double x, double y, double width, double height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public double X { get; }

        public double Y { get; }

        public double Width { get; }

        public double Height { get; }

        public bool Equals(SurfaceBounds other) => X.Equals(other.X) && Y.Equals(other.Y) && Width.Equals(other.Width) && Height.Equals(other.Height);

        public override bool Equals(object? obj) => obj is SurfaceBounds other && Equals(other);

        public override int GetHashCode() => unchecked((((X.GetHashCode() * 397) ^ Y.GetHashCode()) * 397 ^ Width.GetHashCode()) * 397 ^ Height.GetHashCode());

        public override string ToString() => $"{X},{Y} {Width}x{Height}";
    }

    /// <summary><c>selectionChanged {id, bounds, ids}</c>: the primary id first in <see cref="Ids"/>; empty when nothing is selected.</summary>
    public sealed class SelectionChangedMessage : WebSurfaceMessage
    {
        public SelectionChangedMessage(IReadOnlyList<string> ids, SurfaceBounds? bounds)
            : base("selectionChanged")
        {
            Ids = ids;
            Bounds = bounds;
        }

        public IReadOnlyList<string> Ids { get; }

        /// <summary>The primary element's id, or null when nothing is selected (<c>""</c> is the root element).</summary>
        public string? PrimaryId => Ids.Count > 0 ? Ids[0] : null;

        public SurfaceBounds? Bounds { get; }
    }

    /// <summary>The <c>op.kind</c> values of <c>editRequest</c> (the desktop's <c>EditOp</c>, docs/DESIGNER.md §8).</summary>
    public enum SurfaceEditKind
    {
        SetAttribute,
        RemoveElement,
        InsertChild,
        MoveElement,
    }

    /// <summary>One edit intent: the page never writes text, Visual Studio turns the intent into a buffer edit (docs/DESIGNER.md §3).</summary>
    public sealed class SurfaceEditOp
    {
        private SurfaceEditOp(SurfaceEditKind kind, string elementId, string? name, string? value, string? parentId, int index, string? xml)
        {
            Kind = kind;
            ElementId = elementId;
            Name = name;
            Value = value;
            ParentId = parentId;
            Index = index;
            Xml = xml;
        }

        public SurfaceEditKind Kind { get; }

        /// <summary>The element edited, removed or moved (empty for <see cref="SurfaceEditKind.InsertChild"/>).</summary>
        public string ElementId { get; }

        public string? Name { get; }

        public string? Value { get; }

        /// <summary>The container of an inserted or moved element.</summary>
        public string? ParentId { get; }

        /// <summary>The child index of an inserted or moved element (-1 when not relevant).</summary>
        public int Index { get; }

        /// <summary>The markup of an inserted element.</summary>
        public string? Xml { get; }

        public static SurfaceEditOp SetAttribute(string elementId, string name, string value) =>
            new SurfaceEditOp(SurfaceEditKind.SetAttribute, elementId, name, value, null, -1, null);

        public static SurfaceEditOp RemoveElement(string elementId) =>
            new SurfaceEditOp(SurfaceEditKind.RemoveElement, elementId, null, null, null, -1, null);

        public static SurfaceEditOp InsertChild(string parentId, int index, string xml) =>
            new SurfaceEditOp(SurfaceEditKind.InsertChild, string.Empty, null, null, parentId, index, xml);

        public static SurfaceEditOp MoveElement(string elementId, string newParentId, int index) =>
            new SurfaceEditOp(SurfaceEditKind.MoveElement, elementId, null, null, newParentId, index, null);
    }

    /// <summary><c>editRequest {op}</c>.</summary>
    public sealed class EditRequestMessage : WebSurfaceMessage
    {
        public EditRequestMessage(SurfaceEditOp op)
            : base("editRequest") => Op = op;

        public SurfaceEditOp Op { get; }
    }

    /// <summary>Where a Toolbox drop would land (<c>kubuno_desktop_views::design::DropTarget</c>): marker in page CSS pixels.</summary>
    public sealed class SurfaceDropTarget
    {
        public SurfaceDropTarget(bool valid, string parentId, int index, SurfaceBounds marker)
        {
            Valid = valid;
            ParentId = parentId;
            Index = index;
            Marker = marker;
        }

        public bool Valid { get; }

        public string ParentId { get; }

        public int Index { get; }

        public SurfaceBounds Marker { get; }
    }

    /// <summary><c>dropTargetChanged {target}</c>; <see cref="Target"/> is null when the drag has no target.</summary>
    public sealed class DropTargetChangedMessage : WebSurfaceMessage
    {
        public DropTargetChangedMessage(SurfaceDropTarget? target)
            : base("dropTargetChanged") => Target = target;

        public SurfaceDropTarget? Target { get; }
    }

    /// <summary>
    /// Web addition: <c>focusState {editing}</c> - the page tells the host whether a text editor of its own (inline
    /// text editing) has the focus, so the host knows which keys belong to the page (<see cref="AcceleratorRouting"/>).
    /// </summary>
    public sealed class FocusStateMessage : WebSurfaceMessage
    {
        public FocusStateMessage(bool editing)
            : base("focusState") => Editing = editing;

        public bool Editing { get; }
    }

    /// <summary>
    /// Web addition (docs/WEB-VIEWS.md §4.4): <c>unhandledKey {key, ctrl, shift, alt}</c> - a key the page's own
    /// keyboard hook did not use and Visual Studio should see (the key never reached Visual Studio's message loop).
    /// </summary>
    public sealed class UnhandledKeyMessage : WebSurfaceMessage
    {
        public UnhandledKeyMessage(string key, bool control, bool shift, bool alt)
            : base("unhandledKey")
        {
            Key = key;
            Control = control;
            Shift = shift;
            Alt = alt;
        }

        /// <summary>The DOM <c>KeyboardEvent.key</c>.</summary>
        public string Key { get; }

        public bool Control { get; }

        public bool Shift { get; }

        public bool Alt { get; }
    }

    /// <summary>SPIKE: <c>toolboxDragDetected {}</c> - a Toolbox drag (<c>text/plain</c>) entered the page: the host takes the drag over (<see cref="DropChannel.Host"/>).</summary>
    public sealed class ToolboxDragDetectedMessage : WebSurfaceMessage
    {
        public ToolboxDragDetectedMessage()
            : base("toolboxDragDetected")
        {
        }
    }

    /// <summary>SPIKE measurement: <c>metrics {dpr, width, height}</c> - the page's <c>devicePixelRatio</c> and viewport in CSS pixels.</summary>
    public sealed class MetricsMessage : WebSurfaceMessage
    {
        public MetricsMessage(double devicePixelRatio, double width, double height)
            : base("metrics")
        {
            DevicePixelRatio = devicePixelRatio;
            Width = width;
            Height = height;
        }

        public double DevicePixelRatio { get; }

        public double Width { get; }

        public double Height { get; }
    }

    /// <summary>SPIKE diagnostics: <c>log {message}</c>, written to the Kubuno Output pane.</summary>
    public sealed class LogMessage : WebSurfaceMessage
    {
        public LogMessage(string message)
            : base("log") => Message = message;

        public string Message { get; }
    }

    /// <summary>Web addition: <c>surfaceError {message, line, column}</c> - the page could not render the view.</summary>
    public sealed class SurfaceErrorMessage : WebSurfaceMessage
    {
        public SurfaceErrorMessage(string message, int line, int column)
            : base("surfaceError")
        {
            Message = message;
            Line = line;
            Column = column;
        }

        public string Message { get; }

        public int Line { get; }

        public int Column { get; }
    }
}
