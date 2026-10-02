namespace Kubuno.Views.Locating
{
    /// <summary>
    /// Where the <c>kubuno-views-ls.exe</c> path returned by <see cref="KubunoViewsLanguageServerLocator"/>
    /// came from, in the order they are tried. Surfaced so callers (the language client, the "Kubuno"
    /// output pane) can explain the decision instead of just showing a path.
    /// </summary>
    public enum KubunoViewsLanguageServerSource
    {
        /// <summary>The path configured in Tools &gt; Options &gt; Kubuno &gt; Views.</summary>
        OptionOverride,

        /// <summary>
        /// <c>&lt;extension install dir&gt;\tools\kubuno-views-ls.exe</c> - where the VSIX ships the
        /// binary (see INTEGRATION.md, "Shipping kubuno-views-ls.exe").
        /// </summary>
        ExtensionToolsFolder,

        /// <summary>A <c>kubuno-views-ls.exe</c> found on the PATH environment variable.</summary>
        Path,

        /// <summary>
        /// One of the local dev build output folders (<c>C:\kubuno-build\desktop-target\debug\</c>,
        /// <c>C:\kubuno-build\agent-views-ls\debug\</c>) - a convenience for developers iterating on
        /// <c>kubuno-views-ls</c> itself without packaging or installing it anywhere.
        /// </summary>
        DevBuildFolder,

        /// <summary>kubuno-views-ls could not be located by any of the above means.</summary>
        NotFound,
    }
}
