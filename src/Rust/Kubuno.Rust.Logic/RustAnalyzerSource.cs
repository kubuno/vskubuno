namespace Kubuno.VisualStudio.Core
{
    /// <summary>
    /// Where the rust-analyzer executable path returned by <see cref="RustAnalyzerLocator"/> came
    /// from, in the order they are tried. Surfaced so callers (the language client, the "Kubuno"
    /// output pane, the info bar) can explain the decision instead of just showing a path.
    /// </summary>
    public enum RustAnalyzerSource
    {
        /// <summary>The path configured in Tools &gt; Options &gt; Kubuno &gt; Rust.</summary>
        OptionOverride,

        /// <summary>The path printed by <c>rustup which rust-analyzer</c>.</summary>
        RustupWhich,

        /// <summary>The default rustup component install location, <c>%USERPROFILE%\.cargo\bin\rust-analyzer.exe</c>.</summary>
        CargoBinDefault,

        /// <summary>A <c>rust-analyzer.exe</c> found on the PATH environment variable.</summary>
        Path,

        /// <summary>rust-analyzer could not be located by any of the above means.</summary>
        NotFound,
    }
}
