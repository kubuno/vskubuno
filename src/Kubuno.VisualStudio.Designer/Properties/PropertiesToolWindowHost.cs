namespace Kubuno.VisualStudio.Designer.Properties
{
    /// <summary>
    /// The single, shared <see cref="PropertiesPanelViewModel"/> instance for the whole VS session -
    /// mirrors <see cref="DesignSurface.DesignSurfaceHostFactoryHost"/>/<c>Options.DesignerOptionsHost</c>'s
    /// own static-gateway shape. Exists so <see cref="Selection.SelectionSyncService"/> (constructed once
    /// per open <c>.kbview</c> designer pane, INTEGRATION.md §9 point 5: "the SAME instance the
    /// Properties tool window already shows") and <c>ToolWindows.PropertiesToolWindow</c> (constructed by
    /// the VS shell, independently, whenever the user first shows it) always end up sharing the ONE
    /// view-model, regardless of which of the two is created first - a plain "VSIX package hands it to
    /// both" static field cannot do that ordering-independent hand-off (a tool window's parameterless
    /// constructor gets no such injection), so this uses a lazy singleton instead:
    /// <see cref="Current"/> creates the view-model on its own first access.
    /// </summary>
    public static class PropertiesToolWindowHost
    {
        private static PropertiesPanelViewModel? _current;

        public static PropertiesPanelViewModel Current => _current ??= new PropertiesPanelViewModel();
    }
}
