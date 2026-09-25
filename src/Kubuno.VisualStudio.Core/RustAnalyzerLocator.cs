using System;
using System.IO;

namespace Kubuno.VisualStudio.Core
{
    /// <summary>
    /// Finds the rust-analyzer executable to launch as the LSP server, in the order documented in
    /// the extension's README: an explicit option override, then <c>rustup which rust-analyzer</c>,
    /// then the default rustup component install path, then PATH. Never throws and never returns a
    /// path that does not exist - callers must treat <see cref="RustAnalyzerSource.NotFound"/> as a
    /// case to surface to the user (info bar), not to fail on silently.
    /// </summary>
    public static class RustAnalyzerLocator
    {
        private const string ExecutableFileName = "rust-analyzer.exe";

        public static RustAnalyzerLocateResult Locate(string? optionOverridePath, IRustAnalyzerEnvironment environment)
        {
            if (environment is null)
            {
                throw new ArgumentNullException(nameof(environment));
            }

            if (!string.IsNullOrWhiteSpace(optionOverridePath) && environment.FileExists(optionOverridePath!))
            {
                return RustAnalyzerLocateResult.Found(optionOverridePath!, RustAnalyzerSource.OptionOverride);
            }

            var fromRustup = environment.RunRustupWhich();
            if (!string.IsNullOrWhiteSpace(fromRustup) && environment.FileExists(fromRustup!))
            {
                return RustAnalyzerLocateResult.Found(fromRustup!, RustAnalyzerSource.RustupWhich);
            }

            if (!string.IsNullOrWhiteSpace(environment.UserProfileDirectory))
            {
                var cargoBinPath = Path.Combine(environment.UserProfileDirectory!, ".cargo", "bin", ExecutableFileName);
                if (environment.FileExists(cargoBinPath))
                {
                    return RustAnalyzerLocateResult.Found(cargoBinPath, RustAnalyzerSource.CargoBinDefault);
                }
            }

            foreach (var directory in environment.PathDirectories)
            {
                if (string.IsNullOrWhiteSpace(directory))
                {
                    continue;
                }

                string candidate;
                try
                {
                    candidate = Path.Combine(directory, ExecutableFileName);
                }
                catch (ArgumentException)
                {
                    // A malformed PATH entry (stray quote, illegal character, ...) must not abort
                    // the search; just skip it and keep looking.
                    continue;
                }

                if (environment.FileExists(candidate))
                {
                    return RustAnalyzerLocateResult.Found(candidate, RustAnalyzerSource.Path);
                }
            }

            return RustAnalyzerLocateResult.NotFound();
        }
    }
}
