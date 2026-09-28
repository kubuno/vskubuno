using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Kubuno.Cargo.Commands;
using Kubuno.Cargo.Diagnostics;
using Kubuno.Cargo.MSBuild.Tasks.Internal;
using Kubuno.Cargo.Processes;
using Microsoft.Build.Framework;
using MSBuildTask = Microsoft.Build.Utilities.Task;

namespace Kubuno.Cargo.MSBuild.Tasks
{
    /// <summary>
    /// Shared plumbing for every task in this assembly: builds a <see cref="CargoCommand"/> (left
    /// to each subclass), runs it with <c>--message-format=json-diagnostic-rendered-ansi</c>, and
    /// feeds each output line through <see cref="CargoMessageParser"/>/<see cref="CargoDiagnosticLogging"/>
    /// as it streams — so an Error List entry appears while cargo is still running, not only once
    /// the whole build has finished.
    /// </summary>
    public abstract class CargoBuildTaskBase : MSBuildTask
    {
        /// <summary>Path to the Cargo.toml this command targets, passed as <c>--manifest-path</c>.</summary>
        [Required]
        public string ManifestPath { get; set; } = string.Empty;

        /// <summary>Passed as <c>-p &lt;value&gt;</c> when set.</summary>
        public string? Package { get; set; }

        /// <summary>
        /// "debug" (the default — emits no flag), "release" (emits <c>--release</c>), or any other
        /// name (emitted as <c>--profile &lt;name&gt;</c>). See <see cref="CargoCommand.WithProfile"/>.
        /// </summary>
        public string? Profile { get; set; }

        /// <summary>When set, exported as the child process's <c>CARGO_TARGET_DIR</c> environment variable.</summary>
        public string? TargetDir { get; set; }

        /// <summary>Feature names, comma-joined into <c>--features</c>.</summary>
        public ITaskItem[] Features { get; set; } = Array.Empty<ITaskItem>();

        /// <summary>Extra, verbatim arguments appended after every other flag.</summary>
        public string[] ExtraArgs { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Working directory for the cargo process. Defaults to <see cref="ManifestPath"/>'s own
        /// directory, which is also used to resolve relative diagnostic spans to absolute paths
        /// (see <see cref="CargoMessageParser.Parse"/>) — correct whenever the manifest is a
        /// workspace root or a standalone package; a member of a larger workspace should set
        /// <see cref="WorkspaceRoot"/> explicitly.
        /// </summary>
        public string? WorkingDirectory { get; set; }

        /// <summary>Overrides the workspace root used to resolve relative diagnostic spans (see <see cref="WorkingDirectory"/>).</summary>
        public string? WorkspaceRoot { get; set; }

        /// <summary>Builds the command line for this task's own cargo subcommand (build/test/…).</summary>
        protected abstract CargoCommand CreateCommand();

        /// <summary>Called once per parsed build event, in order, as cargo's output streams in.</summary>
        protected virtual void OnBuildEvent(CargoBuildEvent buildEvent)
        {
        }

        /// <summary>Called once after the process has exited, before <see cref="Execute"/> returns.</summary>
        protected virtual void OnCompleted(ProcessRunResult result)
        {
        }

        public override bool Execute()
        {
            if (string.IsNullOrEmpty(ManifestPath))
            {
                Log.LogError("ManifestPath is required.");
                return false;
            }

            CargoCommand command = CreateCommand();
            command.WithManifestPath(ManifestPath);
            command.WithMessageFormat("json-diagnostic-rendered-ansi");

            if (!string.IsNullOrEmpty(Package))
            {
                command.WithPackage(Package!);
            }
            if (!string.IsNullOrEmpty(Profile))
            {
                command.WithProfile(Profile!);
            }
            foreach (ITaskItem feature in Features)
            {
                command.WithFeature(feature.ItemSpec);
            }
            if (ExtraArgs.Length > 0)
            {
                command.WithExtraArgs(ExtraArgs);
            }

            CargoCommandLine commandLine = command.ToCommandLine();
            Log.LogCommandLine(MessageImportance.Low, commandLine.ToString());

            string manifestDirectory = Path.GetDirectoryName(Path.GetFullPath(ManifestPath)) ?? ManifestPath;
            string workingDirectory = string.IsNullOrEmpty(WorkingDirectory) ? manifestDirectory : WorkingDirectory!;
            string workspaceRoot = string.IsNullOrEmpty(WorkspaceRoot) ? manifestDirectory : WorkspaceRoot!;

            IReadOnlyDictionary<string, string>? environment = null;
            if (!string.IsNullOrEmpty(TargetDir))
            {
                environment = new Dictionary<string, string> { ["CARGO_TARGET_DIR"] = TargetDir! };
            }

            var request = new ProcessRunRequest(commandLine.FileName, commandLine.Arguments)
            {
                WorkingDirectory = workingDirectory,
                EnvironmentVariables = environment,
            };

            var runner = new ProcessRunner();
            var progress = new Progress<ProcessOutputLine>(line => OnOutputLine(line, workspaceRoot));

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

            OnCompleted(result);

            if (!result.Succeeded && !Log.HasLoggedErrors)
            {
                // cargo failed (non-zero exit) but produced no parsed diagnostic with Error
                // severity (e.g. a manifest error, or a linker failure with no compiler span) —
                // still fail the MSBuild task, with whatever it wrote to stderr as the message.
                foreach (string errorLine in result.StandardErrorLines)
                {
                    Log.LogError(errorLine);
                }
                if (result.StandardErrorLines.Count == 0)
                {
                    Log.LogError($"cargo exited with code {result.ExitCode}.");
                }
            }

            return !Log.HasLoggedErrors;
        }

        private void OnOutputLine(ProcessOutputLine line, string workspaceRoot)
        {
            if (line.IsError)
            {
                // Plain-text stderr (progress like "Compiling foo v0.1.0", or a non-JSON cargo
                // error) never goes through the JSON parser — only stdout is
                // `--message-format`-encoded.
                Log.LogMessage(MessageImportance.Normal, line.Text);
                return;
            }

            CargoBuildEvent buildEvent = CargoMessageParser.Parse(line.Text, workspaceRoot);
            CargoDiagnosticLogging.Log(Log, buildEvent);
            OnBuildEvent(buildEvent);
        }
    }
}
