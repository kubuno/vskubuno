using System;
using System.IO;
using System.Threading;
using Kubuno.Rust.Cargo.Commands;
using Kubuno.Rust.Cargo.Processes;
using Microsoft.Build.Framework;
using MSBuildTask = Microsoft.Build.Utilities.Task;

namespace Kubuno.Cargo.MSBuild.Tasks
{
    /// <summary>
    /// <c>cargo fetch</c>: downloads dependencies without building — the <c>Kubuno.Rust.Sdk</c>
    /// <c>CargoRestore</c> target's own NuGet-free "Restore" step (docs/RSPROJ.md §2). Kept
    /// separate from <see cref="CargoBuildTaskBase"/>: <c>cargo fetch</c> has no
    /// <c>--message-format</c> support (it emits no build diagnostics), so nothing here needs the
    /// JSON streaming/parsing the build/test tasks do — only an exit code and stderr passthrough.
    /// </summary>
    public sealed class CargoFetch : MSBuildTask
    {
        [Required]
        public string ManifestPath { get; set; } = string.Empty;

        public string? TargetDir { get; set; }

        public string? WorkingDirectory { get; set; }

        public override bool Execute()
        {
            if (string.IsNullOrEmpty(ManifestPath))
            {
                Log.LogError("ManifestPath is required.");
                return false;
            }

            CargoCommandLine commandLine = CargoCommand.Fetch().WithManifestPath(ManifestPath).ToCommandLine();
            Log.LogCommandLine(MessageImportance.Low, commandLine.ToString());

            string manifestDirectory = Path.GetDirectoryName(Path.GetFullPath(ManifestPath)) ?? ManifestPath;
            string workingDirectory = string.IsNullOrEmpty(WorkingDirectory) ? manifestDirectory : WorkingDirectory!;

            var request = new ProcessRunRequest(commandLine.FileName, commandLine.Arguments)
            {
                WorkingDirectory = workingDirectory,
                EnvironmentVariables = string.IsNullOrEmpty(TargetDir)
                    ? null
                    : new System.Collections.Generic.Dictionary<string, string> { ["CARGO_TARGET_DIR"] = TargetDir! },
            };

            var runner = new ProcessRunner();
            var progress = new Internal.InlineProgress<ProcessOutputLine>(line => Log.LogMessage(MessageImportance.Low, line.Text));

            ProcessRunResult result;
            try
            {
                result = runner.RunAsync(request, progress, CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Log.LogErrorFromException(ex, showStackTrace: false);
                return false;
            }

            if (!result.Succeeded)
            {
                foreach (string errorLine in result.StandardErrorLines)
                {
                    Log.LogError(errorLine);
                }
                if (result.StandardErrorLines.Count == 0)
                {
                    Log.LogError($"cargo fetch exited with code {result.ExitCode}.");
                }
                return false;
            }

            return true;
        }
    }
}
