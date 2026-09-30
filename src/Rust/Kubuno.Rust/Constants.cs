namespace Kubuno.Rust
{
    /// <summary>Identifiers of the Rust layer (content type, file names, options page).</summary>
    public static class Constants
    {
        /// <summary>Editor content type for Rust source files (see <see cref="LanguageService.ContentDefinition"/>).</summary>
        public const string RustContentType = "rust";

        public const string RustFileExtension = ".rs";

        /// <summary>The Rust page of Tools &gt; Options &gt; Kubuno (<see cref="Options.RustOptionsPage"/>).</summary>
        public const string OptionsRustPageName = "Rust";

        /// <summary>Exact remediation command shown in the info bar when rust-analyzer cannot be found.</summary>
        public const string InstallRustAnalyzerCommand = "rustup component add rust-analyzer";

        public const string CargoManifestFileName = "Cargo.toml";

        /// <summary>File name of the generated Open Folder debug configuration (see <see cref="Kubuno.Rust.Launch.LaunchVsJsonWriter"/>).</summary>
        public const string LaunchVsJsonFileName = "launch.vs.json";

        /// <summary>The `.vs` hidden folder Visual Studio itself uses for Open Folder state, relative to the workspace root.</summary>
        public const string VsHiddenFolderName = ".vs";
    }
}
