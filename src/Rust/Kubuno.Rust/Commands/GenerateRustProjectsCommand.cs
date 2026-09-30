using System;
using System.ComponentModel.Design;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EnvDTE;
using Kubuno.Rust.Cargo.Metadata;
using Kubuno.Rust.Cargo.Processes;
using Kubuno.Rust.Logic;
using Kubuno.Rust.Logic.ProjectGeneration;
using Kubuno.Core.Logging;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Workspace.VSIntegration.Contracts;

namespace Kubuno.Rust.Commands
{
    /// <summary>
    /// "Kubuno: Generate Visual Studio Projects" (docs/RSPROJ.md work package 5): from
    /// <c>cargo metadata</c>, creates one <c>.rsproj</c> per workspace member with a
    /// <c>[[bin]]</c> target (never overwriting one that already exists - see
    /// <see cref="RsprojGenerationPlanner"/>'s own idempotency contract), plus a <c>.sln</c> at the
    /// workspace root listing all of them (merged surgically into an existing one - see
    /// <see cref="RsprojSolutionGenerator"/>). Reachable from the Tools menu (auto-detects the
    /// workspace root from the active document or the open folder) and from Solution
    /// Explorer/Open Folder's item context menu on a <c>Cargo.toml</c> that is itself a workspace
    /// root (<see cref="OnContextBeforeQueryStatus"/>).
    /// </summary>
    internal static class GenerateRustProjectsCommand
    {
        /// <summary>
        /// Pinned <c>Kubuno.Rust.Sdk</c> version every generated <c>.rsproj</c> imports - the
        /// source of truth is <c>sdk/Kubuno.Rust.Sdk/Kubuno.Rust.Sdk.csproj</c>'s own
        /// <c>&lt;Version&gt;</c>; kept here as a constant (rather than read from that file at run
        /// time) the same way the SDK's own <c>DefaultProjectTypeGuid</c> is a literal in
        /// <c>Sdk.props</c> - bump both together when the SDK is ever re-versioned.
        /// </summary>
        public const string RsprojSdkVersion = "1.1.0";

        public static void Initialize(AsyncPackage package, OleMenuCommandService commandService)
        {
            var toolsCommandId = new CommandID(PackageGuids.KubunoCommandSet, PackageIds.GenerateRustProjectsCommand);
            var toolsCommand = new OleMenuCommand((sender, args) => Execute(package, explicitManifestPath: null), toolsCommandId);
            commandService.AddCommand(toolsCommand);

            var contextCommandId = new CommandID(PackageGuids.KubunoCommandSet, PackageIds.GenerateRustProjectsContextCommand);
#pragma warning disable VSTHRD010 // both handlers always fire on the UI thread (OleMenuCommand invoke/query events), like DebugRustTestAtCursorCommand's own suppression.
            var contextCommand = new OleMenuCommand((sender, args) => ExecuteFromContext(package), contextCommandId);
            contextCommand.BeforeQueryStatus += (sender, args) => OnContextBeforeQueryStatus(package, (OleMenuCommand)sender!);
#pragma warning restore VSTHRD010
            commandService.AddCommand(contextCommand);
        }

        private static void OnContextBeforeQueryStatus(AsyncPackage package, OleMenuCommand command)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            command.Visible = command.Enabled = TryGetSelectedWorkspaceRootManifest(package) is not null;
        }

        private static void ExecuteFromContext(AsyncPackage package)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var manifestPath = TryGetSelectedWorkspaceRootManifest(package);
            Execute(package, manifestPath);
        }

        private static void Execute(AsyncPackage package, string? explicitManifestPath)
        {
            _ = package.JoinableTaskFactory.RunAsync(async () => await ExecuteAsync(package, explicitManifestPath));
        }

        private static async Task ExecuteAsync(AsyncPackage package, string? explicitManifestPath)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var manifestPath = explicitManifestPath ?? TryResolveWorkspaceRootManifest(package);
            if (manifestPath is null)
            {
                ShowMessage(package, "Could not find a Cargo.toml: open a .rs file, a Cargo workspace folder, or right-click a workspace-root Cargo.toml first.");
                return;
            }

            var workspaceRoot = Path.GetDirectoryName(manifestPath)!;

            await TaskScheduler.Default;

            KubunoLog.WriteLine($"Kubuno: generating Visual Studio projects for '{manifestPath}'...");

            CargoMetadata metadata;
            try
            {
                var reader = new CargoMetadataReader(new ProcessRunner());
                metadata = await reader.ReadAsync(workspaceRoot, manifestPath).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                KubunoLog.WriteException("Kubuno: 'cargo metadata' failed", exception);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                ShowMessage(package, $"'cargo metadata' failed - see the \"Kubuno\" Output pane for details.\n\n{exception.Message}");
                return;
            }

            // The manifest found from the active document may be a member's: the solution belongs at the root of the
            // workspace cargo itself reports.
            if (!string.IsNullOrEmpty(metadata.WorkspaceRoot) && Directory.Exists(metadata.WorkspaceRoot))
            {
                workspaceRoot = metadata.WorkspaceRoot;
            }

            // A .slnx (the default for a new solution) also lists the library-only members, in a "Libraries" solution
            // folder: their sources become browsable and buildable in Solution Explorer. A classic .sln keeps the
            // executables only, as it always did.
            var solutionPath = ResolveSolutionPath(workspaceRoot);
            var isSlnx = solutionPath is not null && solutionPath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase);
            var options = new RsprojGenerationOptions(RsprojSdkVersion, includeLibraryOnlyMembers: isSlnx);
            var plan = RsprojGenerationPlanner.Plan(metadata, options, File.Exists);

            var created = 0;
            var skipped = 0;
            foreach (var item in plan)
            {
                if (item.Action == RsprojPlanAction.SkipExisting)
                {
                    skipped++;
                    KubunoLog.WriteLine($"Kubuno:   skipped {item.ProjectPath} (already exists)");
                    continue;
                }

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(item.ProjectPath)!);
                    File.WriteAllText(item.ProjectPath, item.Content);
                    created++;
                    KubunoLog.WriteLine($"Kubuno:   created {item.ProjectPath}");
                }
                catch (Exception exception)
                {
                    KubunoLog.WriteException($"Kubuno: could not write '{item.ProjectPath}'", exception);
                }
            }

            var addedToSolution = Array.Empty<string>() as System.Collections.Generic.IReadOnlyList<string>;
            if (solutionPath is not null && plan.Count > 0)
            {
                string? existingContent = File.Exists(solutionPath) ? File.ReadAllText(solutionPath) : null;
                var solutionPlan = isSlnx
                    ? RsprojSlnxGenerator.Plan(workspaceRoot, existingContent, plan)
                    : RsprojSolutionGenerator.Plan(workspaceRoot, existingContent, plan);
                if (solutionPlan.Changed)
                {
                    try
                    {
                        File.WriteAllText(solutionPath, solutionPlan.Content);
                        KubunoLog.WriteLine($"Kubuno:   {(existingContent is null ? "created" : "updated")} {solutionPath}");
                        addedToSolution = solutionPlan.AddedProjectNames;
                    }
                    catch (Exception exception)
                    {
                        KubunoLog.WriteException($"Kubuno: could not write '{solutionPath}'", exception);
                    }
                }
            }

            // The generated solution carries its own Kubuno.Rust.Sdk feed, so it opens on a machine where this extension's
            // package has never loaded (SdkFeedDistribution).
            if (plan.Count > 0)
            {
                SdkFeedDistribution.EnsureSolutionLocal(
                    solutionPath is null ? workspaceRoot : Path.GetDirectoryName(solutionPath)!,
                    Path.GetDirectoryName(typeof(GenerateRustProjectsCommand).Assembly.Location),
                    KubunoLog.WriteLine);
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var summary = plan.Count == 0
                ? "No workspace member with a [[bin]] target was found - nothing to generate."
                : $"{created} project(s) created, {skipped} already existed and were left untouched." +
                  (solutionPath is null ? string.Empty : $"\n\nSolution: {solutionPath}");
            KubunoLog.WriteLine($"Kubuno: done - {summary.Replace("\n\n", " ")}");
            ShowMessage(package, summary);
        }

        /// <summary>
        /// One solution at the workspace root: an existing <c>.sln</c> or <c>.slnx</c> is reused (merged into - see
        /// <see cref="RsprojSolutionGenerator"/>/<see cref="RsprojSlnxGenerator"/>) when exactly one is found there; with
        /// none, a fresh <c>.slnx</c> (Visual Studio 2026's default format) named after the workspace directory is created;
        /// with more than one, generation is skipped (logged) rather than guessing which the developer meant.
        /// </summary>
        private static string? ResolveSolutionPath(string workspaceRoot)
        {
            string[] existingSolutions;
            try
            {
                // "*.sln" alone would also match .slnx files (a three-character extension pattern matches longer ones).
                existingSolutions = Directory.GetFiles(workspaceRoot, "*.sln*")
                    .Where(path => path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return null;
            }

            if (existingSolutions.Length == 1)
            {
                return existingSolutions[0];
            }

            if (existingSolutions.Length > 1)
            {
                KubunoLog.WriteLine($"Kubuno: found more than one .sln at '{workspaceRoot}' ({string.Join(", ", existingSolutions.Select(Path.GetFileName))}) - not touching any of them; add the generated .rsproj files to one yourself.");
                return null;
            }

            return Path.Combine(workspaceRoot, new DirectoryInfo(workspaceRoot).Name + ".slnx");
        }

        private static string? TryResolveWorkspaceRootManifest(AsyncPackage package)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var activeDocumentPath = TryGetActiveDocumentPath(package);
            if (activeDocumentPath is not null)
            {
                var root = CargoWorkspaceLocator.FindWorkspaceRoot(activeDocumentPath, Directory.Exists, File.Exists);
                if (root is not null && File.Exists(Path.Combine(root, Constants.CargoManifestFileName)))
                {
                    return Path.Combine(root, Constants.CargoManifestFileName);
                }
            }

            var openFolderLocation = TryGetOpenFolderLocation(package);
            if (openFolderLocation is not null)
            {
                var root = CargoWorkspaceLocator.FindWorkspaceRoot(openFolderLocation, Directory.Exists, File.Exists);
                if (root is not null && File.Exists(Path.Combine(root, Constants.CargoManifestFileName)))
                {
                    return Path.Combine(root, Constants.CargoManifestFileName);
                }
            }

            return null;
        }

        /// <summary>
        /// The selected Solution Explorer/Open Folder item, when it is a single <c>Cargo.toml</c>
        /// whose own text has a <c>[workspace]</c> table (<see cref="WorkspaceManifestScanner"/>) -
        /// the context menu only ever targets a workspace root, never an arbitrary member manifest.
        /// </summary>
        private static string? TryGetSelectedWorkspaceRootManifest(AsyncPackage package)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                if (ServiceProvider.GlobalProvider.GetService(typeof(DTE)) is not DTE dte)
                {
                    return null;
                }

                var selectedItems = dte.SelectedItems;
                if (selectedItems is null || selectedItems.Count != 1)
                {
                    return null;
                }

                var selected = selectedItems.Item(1);
                var path = selected.ProjectItem?.FileNames[1];
                if (path is null || !string.Equals(Path.GetFileName(path), Constants.CargoManifestFileName, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                if (!File.Exists(path))
                {
                    return null;
                }

                var text = File.ReadAllText(path);
                return WorkspaceManifestScanner.IsWorkspaceManifest(text) ? path : null;
            }
            catch (Exception)
            {
                // Best-effort visibility check only, mirroring DebugRustTestAtCursorCommand's own.
                return null;
            }
        }

        private static string? TryGetActiveDocumentPath(AsyncPackage package)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                return (ServiceProvider.GlobalProvider.GetService(typeof(DTE)) as DTE)?.ActiveDocument?.FullName;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string? TryGetOpenFolderLocation(AsyncPackage package)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                if (ServiceProvider.GlobalProvider.GetService(typeof(SComponentModel)) is not IComponentModel componentModel)
                {
                    return null;
                }

                var workspaceService = componentModel.GetService<IVsFolderWorkspaceService>();
                return workspaceService?.CurrentWorkspace?.Location;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void ShowMessage(AsyncPackage package, string message)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            VsShellUtilities.ShowMessageBox(
                package,
                message,
                "Kubuno",
                OLEMSGICON.OLEMSGICON_INFO,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }
    }
}
