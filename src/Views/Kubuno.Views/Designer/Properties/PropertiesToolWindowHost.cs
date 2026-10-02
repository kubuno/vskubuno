namespace Kubuno.Desktop.Designer.Properties
{
    /// <summary>
    /// The single, shared <see cref="PropertiesPanelViewModel"/> instance for the whole VS session -
    /// mirrors <see cref="DesignSurface.DesignSurfaceHostFactoryHost"/>/<c>Options.DesignerOptionsHost</c>'s
    /// own static-gateway shape. The Events-tab handler bridge and <see cref="Selection.SelectionSyncService"/>
    /// (constructed once per open <c>.kbview</c> designer pane) share this ONE view-model; the fallback
    /// "Kubuno Properties" tool window that also displayed it was removed (Visual Studio's own
    /// Properties window took over, docs/DESIGNER.md §11). <see cref="Current"/> creates the view-model
    /// on its own first access.
    /// </summary>
    public static class PropertiesToolWindowHost
    {
        private static PropertiesPanelViewModel? _current;

        public static PropertiesPanelViewModel Current => _current ??= new PropertiesPanelViewModel();
    }
}
