using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Kubuno.Cargo.Commands;
using Kubuno.Cargo.Metadata;
using Kubuno.Cargo.Processes;
using Kubuno.VisualStudio.Core.SolutionExplorer;
using Kubuno.VisualStudio.Logging;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;

namespace Kubuno.VisualStudio.Commands
{
    /// <summary>One other <c>.rsproj</c> in the solution, as listed by <see cref="ProjectReferenceDialog"/>.</summary>
    internal sealed class ReferenceCandidate
    {
        public ReferenceCandidate(string crateName, string manifestPath, bool isReferenced)
        {
            CrateName = crateName;
            ManifestPath = manifestPath;
            IsReferenced = IsChecked = isReferenced;
        }

        public string CrateName { get; }

        public string ManifestPath { get; }

        public string ProjectDirectory => Path.GetDirectoryName(ManifestPath) ?? string.Empty;

        /// <summary>Whether <c>Cargo.toml</c> already declares this as a path dependency (the dialog's initial checkbox state).</summary>
        public bool IsReferenced { get; }

        public bool IsChecked { get; set; }
    }

    /// <summary>
    /// "Référence de projet..." (the extended "Ajouter" submenu, docs/RSPROJ.md/CLAUDE.md's product
    /// request): lists every other <c>.rsproj</c> in the solution with a checkbox (like VS's own
    /// Reference Manager), pre-checked for whatever is already a path dependency of the selected
    /// project's <c>Cargo.toml</c>. OK adds/removes path dependencies for whatever changed, entirely
    /// through <c>cargo add --path</c>/<c>cargo remove</c> - <c>Cargo.toml</c> is never hand-edited
    /// (docs/RSPROJ.md §1, CLAUDE.md §7's "never hand-rewrite the TOML").
    /// </summary>
    internal static class AddProjectReferenceCommand
    {
        public static void Initialize(AsyncPackage package, OleMenuCommandService commandService)
        {
            var id = new CommandID(PackageGuids.KubunoCommandSet, PackageIds.AddProjectReferenceCommand);
#pragma warning disable VSTHRD100, VSTHRD010 // both handlers always fire on the UI thread (OleMenuCommand invoke/query events), like GenerateRustProjectsCommand's own suppression.
            var command = new OleMenuCommand((sender, args) => Execute(), id);
            command.BeforeQueryStatus += (sender, args) => OnBeforeQueryStatus((OleMenuCommand)sender!);
#pragma warning restore VSTHRD100, VSTHRD010
            commandService.AddCommand(command);
        }

        private static void OnBeforeQueryStatus(OleMenuCommand command)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            command.Visible = command.Enabled = RsprojSelection.TryGetCurrent() is not null;
        }

        private static void Execute()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var context = RsprojSelection.TryGetCurrent();
            if (context is null)
            {
                return;
            }

#pragma warning disable VSSDK007 // deliberately fire-and-forget from a synchronous command handler; FileAndForget reports any fault to the "Kubuno" pane instead of dropping it silently.
            ThreadHelper.JoinableTaskFactory.RunAsync(() => ExecuteAsync(context)).FileAndForget("Kubuno/AddProjectReference");
#pragma warning restore VSSDK007
        }

        private static async Task ExecuteAsync(RsprojProjectContext context)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var candidates = await CollectCandidatesAsync(context);

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (candidates.Count == 0)
            {
                VsShellUtilities.ShowMessageBox(
                    ServiceProvider.GlobalProvider,
                    "Aucun autre projet .rsproj n'a été trouvé dans la solution.",
                    "Kubuno",
                    OLEMSGICON.OLEMSGICON_INFO,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                return;
            }

            var dialog = new ProjectReferenceDialog(candidates);
            if (dialog.ShowModal() != true)
            {
                return;
            }

            var toAdd = candidates.Where(c => c.IsChecked && !c.IsReferenced).ToList();
            var toRemove = candidates.Where(c => !c.IsChecked && c.IsReferenced).ToList();
            if (toAdd.Count == 0 && toRemove.Count == 0)
            {
                return;
            }

            await TaskScheduler.Default;

            var runner = new ProcessRunner();
            var errors = new List<string>();

            foreach (var candidate in toAdd)
            {
                var command = CargoCommand.Add()
                    .WithManifestPath(context.ManifestPath)
                    .WithExtraArgs("--path", candidate.ProjectDirectory);
                if (!string.IsNullOrEmpty(context.PackageName))
                {
                    command = command.WithPackage(context.PackageName!);
                }

                await RunAsync(runner, command, $"cargo add --path \"{candidate.ProjectDirectory}\"", errors);
            }

            foreach (var candidate in toRemove)
            {
                var command = CargoCommand.Remove()
                    .WithManifestPath(context.ManifestPath)
                    .WithExtraArgs(candidate.CrateName);
                if (!string.IsNullOrEmpty(context.PackageName))
                {
                    command = command.WithPackage(context.PackageName!);
                }

                await RunAsync(runner, command, $"cargo remove {candidate.CrateName}", errors);
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (errors.Count > 0)
            {
                VsShellUtilities.ShowMessageBox(
                    ServiceProvider.GlobalProvider,
                    $"{errors.Count} commande(s) cargo ont échoué - voir le panneau de sortie \"Kubuno\" pour le détail.\n\n{string.Join("\n", errors)}",
                    "Kubuno",
                    OLEMSGICON.OLEMSGICON_WARNING,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            }

            // No manual refresh needed: CollectionSources.ProjectDependenciesSource already watches
            // this Cargo.toml (DebouncedFileWatcher) and reloads the Dependencies node on its own.
        }

        private static async Task RunAsync(ProcessRunner runner, CargoCommand command, string description, List<string> errors)
        {
            KubunoLog.WriteLine($"Kubuno: {description}...");
            try
            {
                var line = command.ToCommandLine();
                var result = await runner.RunAsync(
                    new ProcessRunRequest(line.FileName, line.Arguments),
                    onOutput: new Progress<ProcessOutputLine>(l => KubunoLog.WriteLine($"Kubuno:   {l.Text}")),
                    System.Threading.CancellationToken.None);
                if (!result.Succeeded)
                {
                    errors.Add(description);
                    KubunoLog.WriteLine($"Kubuno:   {description} failed (exit code {result.ExitCode}).");
                }
            }
            catch (Exception exception)
            {
                errors.Add(description);
                KubunoLog.WriteException($"Kubuno: {description}", exception);
            }
        }

        private static async Task<List<ReferenceCandidate>> CollectCandidatesAsync(RsprojProjectContext context)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var rsprojPaths = new List<string>();
            if (ServiceProvider.GlobalProvider.GetService(typeof(SVsSolution)) is IVsSolution solution)
            {
                if (ErrorHandler.Succeeded(solution.GetProjectFilesInSolution(0, 0, null, out var count)) && count > 0)
                {
                    var names = new string[count];
                    if (ErrorHandler.Succeeded(solution.GetProjectFilesInSolution(0, count, names, out _)))
                    {
                        rsprojPaths.AddRange(names.Where(n => string.Equals(Path.GetExtension(n), ".rsproj", StringComparison.OrdinalIgnoreCase)));
                    }
                }
            }

            var ownManifestFull = Normalize(context.ManifestPath);
            var ownProjectPath = Normalize(context.Project.FullName ?? string.Empty);

            var candidateManifests = new List<string>();
            foreach (var rsprojPath in rsprojPaths)
            {
                if (string.Equals(Normalize(rsprojPath), ownProjectPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var manifest = RsprojFileReader.TryReadManifestPath(rsprojPath);
                if (manifest is null || string.Equals(Normalize(manifest), ownManifestFull, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!candidateManifests.Any(m => string.Equals(Normalize(m), Normalize(manifest), StringComparison.OrdinalIgnoreCase)))
                {
                    candidateManifests.Add(manifest);
                }
            }

            if (candidateManifests.Count == 0)
            {
                return new List<ReferenceCandidate>();
            }

            await TaskScheduler.Default;

            var reader = new CargoMetadataReader(new ProcessRunner());
            CargoMetadata? ownMetadata = null;
            try
            {
                ownMetadata = await reader.ReadAsync(context.ProjectDirectory, context.ManifestPath);
            }
            catch (Exception exception)
            {
                KubunoLog.WriteException("Kubuno: 'cargo metadata' failed while listing project references", exception);
            }

            var ownPackage = ownMetadata != null ? CargoDependencyGroups.FindPackage(ownMetadata, context.ManifestPath, context.PackageName) : null;
            var referencedDirs = new HashSet<string>(
                (ownPackage?.Dependencies ?? Array.Empty<CargoDependency>())
                    .Where(d => d.Path != null)
                    .Select(d => Normalize(d.Path!)),
                StringComparer.OrdinalIgnoreCase);

            var result = new List<ReferenceCandidate>();
            foreach (var manifest in candidateManifests)
            {
                var package = ownMetadata?.Packages.FirstOrDefault(p => string.Equals(Normalize(p.ManifestPath), Normalize(manifest), StringComparison.OrdinalIgnoreCase));
                if (package is null)
                {
                    // Not a member of the current workspace (e.g. a sibling repo referenced across
                    // filesystem paths) - resolve its own name with a small individual read.
                    try
                    {
                        var directory = Path.GetDirectoryName(manifest)!;
                        var otherMetadata = await reader.ReadAsync(directory, manifest);
                        package = CargoDependencyGroups.FindPackage(otherMetadata, manifest, packageName: null);
                    }
                    catch (Exception exception)
                    {
                        KubunoLog.WriteException($"Kubuno: 'cargo metadata' failed for '{manifest}'", exception);
                    }
                }

                var name = package?.Name ?? Path.GetFileNameWithoutExtension(Path.GetDirectoryName(manifest));
                var isReferenced = referencedDirs.Contains(Normalize(Path.GetDirectoryName(manifest)!));
                result.Add(new ReferenceCandidate(name ?? "?", manifest, isReferenced));
            }

            return result.OrderBy(c => c.CrateName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string Normalize(string path)
        {
            try
            {
                return string.IsNullOrEmpty(path) ? path : Path.GetFullPath(path).TrimEnd('\\', '/');
            }
            catch (Exception)
            {
                return path;
            }
        }
    }
}
