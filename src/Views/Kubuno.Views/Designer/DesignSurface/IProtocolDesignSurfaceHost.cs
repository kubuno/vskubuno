using System;
using System.Collections.Generic;

namespace Kubuno.Views.Designer.DesignSurface
{
    /// <summary>
    /// A live design surface: an <see cref="IDesignSurfaceHost"/> that renders the view and speaks the design-surface
    /// protocol (<see cref="DesignSurfaceProtocol"/>, docs/DESIGNER.md; docs/WEB-VIEWS.md section 4.4 for the web
    /// additions) - selection, gestures turned into edit intents, context menus, drops, resources and zoom. It is the seam
    /// between the target-neutral designer of this layer and a target's renderer: the desktop layer implements it with the
    /// Rust <c>view_embed</c> surface (<c>Kubuno.Desktop.Designer.DesignSurface.RustDesignSurfaceHost</c>), the web layer
    /// will implement it with a WebView2 page (<c>WebDesignSurfaceHost</c>, WV-9b). The designer never names an
    /// implementation: a host that is not one of these (<see cref="PlaceholderDesignSurfaceHost"/>) only shows its content
    /// and every live feature below is skipped.
    /// </summary>
    /// <remarks>
    /// Raised on the UI thread unless stated otherwise. A host may also implement <see cref="IDesignSurfaceStatusAware"/> (render
    /// status, error banner) and <see cref="IDesignSurfaceRuntimeAware"/> (the runtime info bar).
    /// </remarks>
    public interface IProtocolDesignSurfaceHost : IDesignSurfaceHost
    {
        /// <summary>A single structural edit request (DSG-9): a Flow reorder drop (<c>moveElement</c>) or a toolbox drop (<c>insertChild</c>).</summary>
        event EventHandler<DesignSurfaceDragDropEditRequestedEventArgs>? DragDropEditRequested;

        /// <summary>The edit requests of one gesture (<c>editRequests</c>: a move or resize, a Format command), applied as one undo unit.</summary>
        event EventHandler<DesignSurfaceEditRequestsReceivedEventArgs>? EditRequestsReceived;

        /// <summary>A key pressed on the surface that Visual Studio's accelerator translation did not handle (Ctrl+Z / Ctrl+Y...).</summary>
        event EventHandler<DesignSurfaceKeyEventArgs>? UnhandledSurfaceKey;

        /// <summary>A right-click on the surface: the designer shows its context menu.</summary>
        event EventHandler<DesignSurfaceContextMenuEventArgs>? ContextMenuRequested;

        /// <summary>A double-click on an element: create or open its default event handler (docs/EVENTS.md section 5.2).</summary>
        event EventHandler<DesignSurfaceDoubleClickEventArgs>? ElementDoubleClicked;

        /// <summary>A clipboard or duplicate keyboard command on the surface (Ctrl+C/X/V/D, docs/DESIGNER.md section 12).</summary>
        event EventHandler<DesignSurfaceCommandEventArgs>? SurfaceCommandRequested;

        /// <summary>Raised (on any thread) when the project's resource files change: the cultures offered may have changed.</summary>
        event EventHandler? ResourcesChanged;

        /// <summary>Selects one element on the surface (<see langword="null"/> clears the selection).</summary>
        void Select(string? elementId);

        /// <summary>Selects several elements; <paramref name="primary"/> gets the primary selection frame.</summary>
        void SelectMany(IReadOnlyList<string> elementIds, string? primary);

        /// <summary>Tells the surface which controls the project declares (a JSON array of registry entries, docs/EVENTS.md EVT-7b).</summary>
        void SetProjectComponents(string componentsJsonArray);

        /// <summary>Applies a Layout toolbar / Format menu command to the surface's selection; it answers with one edit request batch.</summary>
        void Format(string command);

        /// <summary>The zoom asked for: a factor (1 = 100 %), 0 for « fit ».</summary>
        double Zoom { get; }

        /// <summary>Sets the zoom (a factor, 0 for « fit »).</summary>
        void SetZoom(double zoom);

        /// <summary>The design-time culture the view is previewed with (docs/RESOURCES.md); <c>""</c> = "(Default)", the neutral values.</summary>
        string DesignCulture { get; }

        /// <summary>Changes the design-time culture (a preview only: nothing is written to the files).</summary>
        void SetDesignCulture(string culture);

        /// <summary>The cultures of the project's resource files, sorted.</summary>
        IReadOnlyList<string> ProjectCultures();
    }
}
