namespace Kubuno.Desktop.Views
{
    /// <summary>
    /// Shared identifiers for this library (content type, file extension, options page, remediation
    /// text). Kept internal-facing but public where MEF export attributes or the VSIX integration
    /// (see INTEGRATION.md) need to reference them by compile-time constant.
    /// </summary>
    public static class KbviewConstants
    {
        /// <summary>Editor content type for Kubuno view files (see <see cref="LanguageService.ContentDefinition"/>).</summary>
        public const string ContentType = "kbview";

        public const string FileExtension = ".kbview";

        /// <summary>File name of the Rust language server binary this library launches over stdio.</summary>
        public const string LanguageServerExecutableName = "kubuno-views-ls.exe";

        /// <summary>
        /// Subfolder of the extension's install directory the VSIX ships <see cref="LanguageServerExecutableName"/>
        /// under (see INTEGRATION.md, "Shipping kubuno-views-ls.exe").
        /// </summary>
        public const string ExtensionToolsFolderName = "tools";

        /// <summary>
        /// Title of the existing "Kubuno" Output pane this library logs into, via <see cref="Logging.IKubunoLog"/>
        /// (the pane itself is owned and created by the VSIX's KubunoPackage - see
        /// src/Core/Kubuno.Core/Logging/KubunoLog.cs - not by this library).
        /// </summary>
        public const string OutputPaneTitle = "Kubuno";

        /// <summary>Tools &gt; Options category page shown for this extension (shared with the Rust options page).</summary>
        public const string OptionsCategoryName = "Kubuno";

        public const string OptionsViewsPageName = "Views";

        public const string CargoManifestFileName = "Cargo.toml";
    }
}
