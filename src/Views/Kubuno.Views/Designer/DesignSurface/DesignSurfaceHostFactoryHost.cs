namespace Kubuno.Views.Designer.DesignSurface
{
    /// <summary>
    /// Static gateway to the active <see cref="IDesignSurfaceHostFactory"/> - the one seam DSG-7 needs
    /// to touch to go from a placeholder Design pane to the real embedded surface. Nothing in this
    /// library (the editor factory, the window pane, the split view) constructs
    /// <see cref="PlaceholderDesignSurfaceHost"/> directly; they all go through <see cref="Current"/>,
    /// so DSG-7 only has to set this property (e.g. from its own package/component initialization,
    /// or - if it stays in this same library - simply change the default below) rather than edit
    /// <see cref="EditorFactory.DesignerWindowPane"/> or <see cref="UI.DesignerSplitView"/>.
    ///
    /// Same shape as <c>Kubuno.Views.Options.KubunoViewsOptionsHost</c>/
    /// <c>KubunoViewsLogHost</c>: a mutable static property with a safe, working default, because the
    /// window pane can be constructed before whatever sets a real factory has run.
    /// </summary>
    public static class DesignSurfaceHostFactoryHost
    {
        private static IDesignSurfaceHostFactory _current = PlaceholderDesignSurfaceHostFactory.Instance;

        public static IDesignSurfaceHostFactory Current
        {
            get => _current;
            set => _current = value ?? PlaceholderDesignSurfaceHostFactory.Instance;
        }
    }
}
