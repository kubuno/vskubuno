using System;
using System.IO;

namespace Kubuno.Desktop.Views.Locating
{
    /// <summary>
    /// Finds the <c>kubuno-views-ls.exe</c> executable to launch as the LSP server for <c>.kbview</c>
    /// files, in this order: an explicit option override (Tools &gt; Options &gt; Kubuno &gt; Views),
    /// then <c>&lt;extension install dir&gt;\tools\kubuno-views-ls.exe</c> (where the VSIX ships the
    /// binary once packaged), then PATH, then the local dev build output folders. Never throws and
    /// never returns a path that does not exist - callers must treat
    /// <see cref="KubunoViewsLanguageServerSource.NotFound"/> as a case to surface to the user (info
    /// bar), not to fail on silently. Mirrors the shape of the sibling VSIX project's
    /// <c>Kubuno.Rust.Logic.RustAnalyzerLocator</c>.
    /// </summary>
    public static class KubunoViewsLanguageServerLocator
    {
        /// <param name="optionOverridePath">
        /// The path configured in Tools &gt; Options &gt; Kubuno &gt; Views, or null/empty when unset.
        /// </param>
        /// <param name="extensionInstallDirectory">
        /// The directory the extension assembly is loaded from (typically
        /// <c>Path.GetDirectoryName(typeof(...).Assembly.Location)</c>), or null/empty when unknown -
        /// the <c>tools</c> subfolder candidate is simply skipped in that case.
        /// </param>
        /// <param name="environment">File-system/PATH/dev-folder access, kept fake-able for tests.</param>
        public static KubunoViewsLanguageServerLocateResult Locate(
            string? optionOverridePath,
            string? extensionInstallDirectory,
            IKubunoViewsLanguageServerEnvironment environment)
        {
            if (environment is null)
            {
                throw new ArgumentNullException(nameof(environment));
            }

            if (!string.IsNullOrWhiteSpace(optionOverridePath) && environment.FileExists(optionOverridePath!))
            {
                return KubunoViewsLanguageServerLocateResult.Found(optionOverridePath!, KubunoViewsLanguageServerSource.OptionOverride);
            }

            if (!string.IsNullOrWhiteSpace(extensionInstallDirectory))
            {
                var toolsCandidate = CombineSafely(extensionInstallDirectory!, KbviewConstants.ExtensionToolsFolderName, KbviewConstants.LanguageServerExecutableName);
                if (toolsCandidate != null && environment.FileExists(toolsCandidate))
                {
                    return KubunoViewsLanguageServerLocateResult.Found(toolsCandidate, KubunoViewsLanguageServerSource.ExtensionToolsFolder);
                }
            }

            foreach (var directory in environment.PathDirectories)
            {
                if (string.IsNullOrWhiteSpace(directory))
                {
                    continue;
                }

                var candidate = CombineSafely(directory, KbviewConstants.LanguageServerExecutableName);
                if (candidate != null && environment.FileExists(candidate))
                {
                    return KubunoViewsLanguageServerLocateResult.Found(candidate, KubunoViewsLanguageServerSource.Path);
                }
            }

            foreach (var directory in environment.DevBuildDirectories)
            {
                if (string.IsNullOrWhiteSpace(directory))
                {
                    continue;
                }

                var candidate = CombineSafely(directory, KbviewConstants.LanguageServerExecutableName);
                if (candidate != null && environment.FileExists(candidate))
                {
                    return KubunoViewsLanguageServerLocateResult.Found(candidate, KubunoViewsLanguageServerSource.DevBuildFolder);
                }
            }

            return KubunoViewsLanguageServerLocateResult.NotFound();
        }

        /// <summary>
        /// <see cref="Path.Combine(string, string)"/>, but a malformed segment (stray quote, illegal
        /// character - most commonly from a PATH entry or an option override typed by hand) must not
        /// abort the whole search; such a segment is simply treated as "no candidate here".
        /// </summary>
        private static string? CombineSafely(string first, string second)
        {
            try
            {
                return Path.Combine(first, second);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private static string? CombineSafely(string first, string second, string third)
        {
            try
            {
                return Path.Combine(first, second, third);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
    }
}
