namespace Kubuno.VisualStudio.Views.Options
{
    /// <summary>
    /// The options <see cref="LanguageService.KubunoViewsLanguageClient"/> reads before starting
    /// <c>kubuno-views-ls</c>. Implemented by <see cref="KbviewOptionsPage"/> (a
    /// <c>Microsoft.VisualStudio.Shell.DialogPage</c>, so it can be hosted as a real Tools &gt;
    /// Options page), kept as a separate interface only so <see cref="KubunoViewsOptionsHost"/> does
    /// not have to name the concrete <c>DialogPage</c> type at every call site.
    /// </summary>
    public interface IKubunoViewsOptions
    {
        /// <summary>
        /// Full path to <c>kubuno-views-ls.exe</c>, or empty/null to use the default search order
        /// (see <see cref="Locating.KubunoViewsLanguageServerLocator"/>).
        /// </summary>
        string? LanguageServerPathOverride { get; }
    }
}
