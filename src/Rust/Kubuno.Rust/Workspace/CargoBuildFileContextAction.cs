using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Rust.Cargo.Commands;
using Kubuno.Rust.Cargo.Diagnostics;
using Kubuno.Rust.Cargo.Processes;
using Kubuno.Rust.Debugging;
using Kubuno.Shared.Logging;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.Build;
using Microsoft.VisualStudio.Workspace.Extensions.VS;

namespace Kubuno.Rust.Workspace
{
    /// <summary>
    /// MEF entry point creating the actual Build/Rebuild/Clean behavior for the file contexts
    /// <see cref="CargoBuildFileContextProviderFactory"/> declares on every <c>Cargo.toml</c>.
    /// </summary>
    [ExportFileContextActionProvider(
        PackageGuids.CargoBuildFileContextActionProviderType,
        new[] { BuildContextTypes.BuildContextType, BuildContextTypes.RebuildContextType, BuildContextTypes.CleanContextType })]
    internal sealed class CargoBuildFileContextActionProviderFactory : IWorkspaceProviderFactory<IFileContextActionProvider>
    {
        public IFileContextActionProvider CreateProvider(Microsoft.VisualStudio.Workspace.IWorkspace workspaceContext) =>
            new CargoBuildFileContextActionProvider(workspaceContext);
    }

    internal sealed class CargoBuildFileContextActionProvider : IFileContextActionProvider
    {
        private readonly Microsoft.VisualStudio.Workspace.IWorkspace _workspace;

        public CargoBuildFileContextActionProvider(Microsoft.VisualStudio.Workspace.IWorkspace workspace)
        {
            _workspace = workspace;
        }

        public Task<IReadOnlyList<IFileContextAction>> GetActionsAsync(string filePath, FileContext fileContext, CancellationToken cancellationToken)
        {
            IFileContextAction action = new CargoBuildFileContextAction(_workspace, fileContext);
            return Task.FromResult<IReadOnlyList<IFileContextAction>>(new[] { action });
        }
    }

    /// <summary>
    /// Runs <c>cargo build</c>/<c>clean</c> (Rebuild = Clean then Build) for the <c>Cargo.toml</c>
    /// named by <see cref="FileContext.InputFiles"/>, streaming readable output and clickable
    /// diagnostics through <see cref="IBuildMessageService"/> - <c>BuildMessage.LogMessage</c>
    /// always lands in the **Build** Output pane, and any <c>TaskType</c> other than
    /// <c>None</c> additionally becomes a clickable **Error List** entry (file/line/column,
    /// severity, code) - see Microsoft's "Workspace build in Visual Studio" doc. This is the
    /// native Open Folder build-message channel, so it needs no custom Output pane or
    /// <c>ErrorListProvider</c> plumbing of our own.
    ///
    /// <see cref="IVsCommandItem"/> is what makes VS route its own well-known Build/Rebuild/Clean
    /// commands (Solution Explorer right-click, and the Build menu when this file is the active
    /// context) to <see cref="ExecuteAsync"/> - <see cref="CommandGroup"/>/<see cref="CommandId"/>
    /// are the documented constants for that command set, not IDs Kubuno owns.
    /// </summary>
    internal sealed class CargoBuildFileContextAction : IFileContextAction, IVsCommandItem
    {
        private static readonly Guid BuildCommandGroup = new("16537f6e-cb14-44da-b087-d1387ce3bf57");

        private readonly Microsoft.VisualStudio.Workspace.IWorkspace _workspace;

        public CargoBuildFileContextAction(Microsoft.VisualStudio.Workspace.IWorkspace workspace, FileContext source)
        {
            _workspace = workspace;
            Source = source;
        }

        public FileContext Source { get; }

        public string DisplayName => Source.DisplayName;

        public Guid CommandGroup => BuildCommandGroup;

        public uint CommandId
        {
            get
            {
                if (Source.ContextType == BuildContextTypes.RebuildContextTypeGuid)
                {
                    return 0x1010;
                }

                if (Source.ContextType == BuildContextTypes.CleanContextTypeGuid)
                {
                    return 0x1020;
                }

                return 0x1000;
            }
        }

        public async Task<IFileContextActionResult> ExecuteAsync(IProgress<IFileContextActionProgressUpdate> progress, CancellationToken cancellationToken)
        {
            var cargoTomlPath = GetManifestPath();
            var packageDirectory = Path.GetDirectoryName(cargoTomlPath) ?? cargoTomlPath;

            var buildMessageService = _workspace.GetBuildMessageService();
            var reporter = new CargoBuildMessageReporter(buildMessageService, packageDirectory, cargoTomlPath);

            bool success;
            if (Source.ContextType == BuildContextTypes.CleanContextTypeGuid)
            {
                success = await RunCargoAsync(CargoCommand.Clean().WithManifestPath(cargoTomlPath), packageDirectory, reporter, cancellationToken)
                    .ConfigureAwait(false);
            }
            else if (Source.ContextType == BuildContextTypes.RebuildContextTypeGuid)
            {
                var cleanSucceeded = await RunCargoAsync(CargoCommand.Clean().WithManifestPath(cargoTomlPath), packageDirectory, reporter, cancellationToken)
                    .ConfigureAwait(false);
                var buildSucceeded = await RunCargoAsync(BuildCommand(cargoTomlPath), packageDirectory, reporter, cancellationToken)
                    .ConfigureAwait(false);
                success = cleanSucceeded && buildSucceeded;
            }
            else
            {
                success = await RunCargoAsync(BuildCommand(cargoTomlPath), packageDirectory, reporter, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (success && Source.ContextType != BuildContextTypes.CleanContextTypeGuid)
            {
                // Targets (and their executable paths) may have changed - regenerate the
                // Select Startup Item / F5 launch configurations (phase 1c).
                await RustLaunchTargetsGenerator.GenerateAsync(packageDirectory, cargoTomlPath, cancellationToken, _workspace).ConfigureAwait(false);
            }

            return new FileContextActionResult(success);
        }

        private static CargoCommand BuildCommand(string cargoTomlPath) =>
            CargoCommand.Build().WithManifestPath(cargoTomlPath).WithMessageFormat("json-diagnostic-rendered-ansi");

        private static async Task<bool> RunCargoAsync(
            CargoCommand command, string workingDirectory, CargoBuildMessageReporter reporter, CancellationToken cancellationToken)
        {
            var commandLine = command.ToCommandLine();
            reporter.LogInfo($"> {commandLine}");
            KubunoLog.WriteLine($"[cargo] {commandLine} (cwd: {workingDirectory})");

            var runner = new ProcessRunner();
            var request = new ProcessRunRequest(commandLine.FileName, commandLine.Arguments)
            {
                WorkingDirectory = workingDirectory,
            };

            ProcessRunResult result;
            try
            {
                result = await runner.RunAsync(request, onOutput: null, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                KubunoLog.WriteException("cargo failed to start", exception);
                reporter.LogError($"Failed to run '{commandLine}': {exception.Message}");
                return false;
            }

            var isBuild = command.MessageFormat is not null;
            foreach (var line in result.StandardOutputLines)
            {
                if (isBuild)
                {
                    reporter.ReportBuildLine(line);
                }
                else
                {
                    reporter.LogInfo(line);
                }
            }

            foreach (var line in result.StandardErrorLines)
            {
                reporter.LogInfo(line);
            }

            await reporter.FlushAsync().ConfigureAwait(false);

            reporter.LogInfo(result.Succeeded
                ? $"'{commandLine}' succeeded."
                : $"'{commandLine}' failed (exit code {result.ExitCode}).");
            await reporter.FlushAsync().ConfigureAwait(false);

            return result.Succeeded;
        }

        private string GetManifestPath()
        {
            foreach (var file in Source.InputFiles)
            {
                return file;
            }

            throw new InvalidOperationException("The Cargo build file context has no input file.");
        }
    }
}
