namespace Kubuno.Desktop.Designer.Options
{
    /// <summary>
    /// Static gateway to the active <see cref="IDesignerOptions"/>, mirroring
    /// <c>Kubuno.Desktop.Views.Options.KubunoViewsOptionsHost</c>: the editor factory and window
    /// pane are constructed by the VS shell (via COM activation of
    /// <see cref="EditorFactory.KbviewEditorFactory"/>'s registered CLSID), not by a package class, so
    /// they cannot receive the options page through a constructor. The VSIX's <c>KubunoPackage</c> sets
    /// <see cref="Current"/> during <c>InitializeAsync</c>, typically to
    /// <c>(KbviewDesignerOptionsPage)GetDialogPage(typeof(KbviewDesignerOptionsPage))</c> - see
    /// INTEGRATION.md.
    /// </summary>
    public static class DesignerOptionsHost
    {
        /// <summary>
        /// The active options source, or <see langword="null"/> before the VSIX has registered one.
        /// Callers must treat a null value the same as an unset/default option in that case (i.e.
        /// <c>UseDesignerAsDefaultEditor == false</c>), exactly as <c>KubunoViewsOptionsHost</c> does.
        /// </summary>
        public static IDesignerOptions? Current { get; set; }
    }
}
