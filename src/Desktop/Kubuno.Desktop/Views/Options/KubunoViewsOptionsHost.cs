namespace Kubuno.Desktop.Views.Options
{
    /// <summary>
    /// Static gateway to the active <see cref="IKubunoViewsOptions"/>, for the same reason
    /// <see cref="Logging.KubunoViewsLogHost"/> exists: <see cref="LanguageService.KubunoViewsLanguageClient"/>
    /// is MEF-constructed, not built by a package class, so it cannot receive the options page through
    /// a constructor or an <c>[Import]</c>. The VSIX's <c>KubunoPackage</c> sets <see cref="Current"/>
    /// during <c>InitializeAsync</c>, typically to <c>(KbviewOptionsPage)GetDialogPage(typeof(KbviewOptionsPage))</c>
    /// - see INTEGRATION.md, "Options page registration", for the exact wiring the VSIX must add.
    /// </summary>
    public static class KubunoViewsOptionsHost
    {
        /// <summary>
        /// The active options source, or <see langword="null"/> before the VSIX has registered one
        /// (e.g. the language client activates before package load finishes) - callers must treat a
        /// null path override the same as an unset one in that case, exactly as if the option simply
        /// had its default (empty) value.
        /// </summary>
        public static IKubunoViewsOptions? Current { get; set; }
    }
}
