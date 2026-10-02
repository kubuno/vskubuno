using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Kubuno.Rust.Logic;
using Kubuno.Shared.Logging;

namespace Kubuno.Rust.Infrastructure
{
    /// <summary>
    /// The real, VS-independent implementation of <see cref="IRustAnalyzerEnvironment"/>: actual
    /// file system checks, the actual PATH variable, and actually spawning
    /// <c>rustup which rust-analyzer</c>. Kept separate from <see cref="Core.RustAnalyzerLocator"/>
    /// so that locator's decision logic stays unit-testable without a real machine.
    /// </summary>
    internal sealed class RealRustAnalyzerEnvironment : IRustAnalyzerEnvironment
    {
        public string? UserProfileDirectory => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        public IEnumerable<string> PathDirectories
        {
            get
            {
                var pathVariable = Environment.GetEnvironmentVariable("PATH");
                if (string.IsNullOrEmpty(pathVariable))
                {
                    yield break;
                }

                foreach (var directory in pathVariable!.Split(Path.PathSeparator))
                {
                    yield return directory;
                }
            }
        }

        public bool FileExists(string path)
        {
            try
            {
                return File.Exists(path);
            }
            catch (Exception exception) when (exception is ArgumentException or PathTooLongException or NotSupportedException)
            {
                // A malformed candidate path (illegal characters coming from a PATH entry, for
                // example) must not abort the search; treat it as "not found" and move on.
                return false;
            }
        }

        public string? RunRustupWhich()
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "rustup",
                    Arguments = "which rust-analyzer",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    return null;
                }

                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                if (!process.WaitForExit(5000))
                {
                    KubunoLog.WriteLine("'rustup which rust-analyzer' timed out after 5s.");
                    TryKill(process);
                    return null;
                }

                if (process.ExitCode != 0)
                {
                    KubunoLog.WriteLine($"'rustup which rust-analyzer' exited with code {process.ExitCode}: {error.Trim()}");
                    return null;
                }

                var trimmed = output.Trim();
                return string.IsNullOrEmpty(trimmed) ? null : trimmed;
            }
            catch (Exception exception)
            {
                // Most commonly: rustup itself is not installed (Win32Exception, file not found).
                // This is an expected, non-fatal outcome - the locator falls through to the next
                // strategy - so it is logged for diagnostics, not surfaced as an error.
                KubunoLog.WriteLine($"'rustup which rust-analyzer' could not be run: {exception.Message}");
                return null;
            }
        }

        private static void TryKill(Process process)
        {
            try
            {
                process.Kill();
            }
            catch (Exception)
            {
                // Best-effort cleanup only.
            }
        }
    }
}
