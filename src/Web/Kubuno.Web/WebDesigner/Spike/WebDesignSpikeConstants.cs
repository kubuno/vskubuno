namespace Kubuno.Web.WebDesigner.Spike
{
    /// <summary>
    /// SPIKE (docs/WEB-VIEWS.md, lot WV-9a, "WV-9a findings"): identifiers of the WebView2 design surface spike - an
    /// editor for <c>.kbwebspike</c> files (a tiny view markup) hosting the surface in a Visual Studio document pane.
    /// Registered by KubunoPackage (<c>[ProvideEditorFactory]</c>, <c>[ProvideEditorExtension]</c>). Removed or replaced
    /// by the real <c>WebDesignSurfaceHost</c> in WV-9b.
    /// </summary>
    public static class WebDesignSpikeConstants
    {
        /// <summary>The editor factory (<see cref="WebDesignSpikeEditorFactory"/>).</summary>
        public const string EditorFactoryGuidString = "6c0f6f39-3d57-4f0b-9a8e-0b9a5f1e7a21";

        /// <summary>The command UI context of the spike's Design window.</summary>
        public const string CommandUiContextGuidString = "6c0f6f39-3d57-4f0b-9a8e-0b9a5f1e7a22";

        /// <summary>The spike's file extension.</summary>
        public const string FileExtension = ".kbwebspike";

        /// <summary>Caption suffix of the Design window, like the desktop designer's <c>[Design]</c>.</summary>
        public const string DesignCaption = " [Design web]";

        /// <summary>
        /// The Toolbox tab of the spike's items: the desktop designer's components (same private clipboard format,
        /// <c>Kubuno.Views.ToolboxItem</c>) plus the HTML5 text channel.
        /// </summary>
        public const string ToolboxTab = "Kubuno Web (spike)";

        /// <summary>The private clipboard format of a Kubuno Toolbox item (Kubuno.Desktop's <c>ToolboxItemFormat.FormatName</c>).</summary>
        public const string ToolboxItemFormat = "Kubuno.Views.ToolboxItem";

        /// <summary>
        /// Test hook: a folder given here is used as the WebView2 runtime ("browser executable folder"); a folder without
        /// a runtime reproduces "WebView2 runtime missing" without uninstalling anything.
        /// </summary>
        public const string BrowserFolderVariable = "KUBUNO_WEBVIEW2_BROWSER_FOLDER";

        /// <summary>The page's origin: a reserved name, every request answered from Kubuno.Web.dll's resources (no network).</summary>
        public const string PageOrigin = "https://kubuno-surface.invalid/";
    }
}
