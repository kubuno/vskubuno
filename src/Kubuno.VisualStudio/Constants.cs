namespace Kubuno.VisualStudio
{
    /// <summary>Shared identifiers used across the package (content type, output pane, options page).</summary>
    internal static class Constants
    {
        /// <summary>Editor content type for Rust source files (see <see cref="LanguageService.ContentDefinition"/>).</summary>
        public const string RustContentType = "rust";

        public const string RustFileExtension = ".rs";

        /// <summary>Title of the "Kubuno" pane in the Output window (LSP traffic/errors, discovery decisions).</summary>
        public const string OutputPaneTitle = "Kubuno";

        /// <summary>Tools &gt; Options category page shown for this extension.</summary>
        public const string OptionsCategoryName = "Kubuno";

        public const string OptionsRustPageName = "Rust";

        /// <summary>Exact remediation command shown in the info bar when rust-analyzer cannot be found.</summary>
        public const string InstallRustAnalyzerCommand = "rustup component add rust-analyzer";
    }
}
