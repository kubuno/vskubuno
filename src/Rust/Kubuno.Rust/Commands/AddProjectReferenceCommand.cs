using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Kubuno.Rust.Cargo.Commands;
using Kubuno.Rust.Cargo.Metadata;
using Kubuno.Rust.Cargo.Processes;
using Kubuno.Rust.Logic.SolutionExplorer;
using Kubuno.Core.Logging;
using Kubuno.Rust.SolutionExplorer;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;

namespace Kubuno.Rust.Commands
{
    /// <summary>
    /// "Référence de projet..." (the extended "Ajouter" submenu) and "Ajouter une référence de
    /// projet..." (the Dependencies node): the <see cref="ReferenceManagerDialog"/>, listing every other
    /// <c>.rsproj</c> of the solution and the path dependencies already declared, pre-checked for what
    /// <c>Cargo.toml</c> already references. OK adds/removes path dependencies for whatever changed,
    /// entirely through <c>cargo add --path</c>/<c>cargo remove</c> - <c>Cargo.toml</c> is never
    /// hand-edited (docs/RSPROJ.md §1).
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

        /// <summary>Opens the Reference Manager for <paramref name="context"/> and applies what changed.</summary>
        public static async Task ShowReferenceManagerAsync(RsprojProjectContext context)
        {
            var candidates = await CollectCandidatesAsync(context);

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var dialog = new ReferenceManagerDialog(context.Project.Name, candidates);
            if (dialog.ShowModal() != true)
            {
                return;
            }

            var all = dialog.Candidates;
            var toAdd = all.Where(c => c.IsChecked && !c.IsReferenced).ToList();
            var toRemove = all.Where(c => !c.IsChecked && c.IsReferenced).ToList();
            if (toAdd.Count == 0 && toRemove.Count == 0)
            {
                return;
            }

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

                var description = $"cargo add --path \"{candidate.ProjectDirectory}\"";
                if (!await CargoRunner.RunAsync(command, description, reportFailure: false))
                {
                    errors.Add(description);
                }
            }

            foreach (var candidate in toRemove)
            {
                var name = candidate.DeclaredName ?? candidate.CrateName;
                var command = CargoCommand.Remove()
                    .WithManifestPath(context.ManifestPath)
                    .WithExtraArgs(name);
                if (!string.IsNullOrEmpty(context.PackageName))
                {
                    command = command.WithPackage(context.PackageName!);
                }

                var description = $"cargo remove {name}";
                if (!await CargoRunner.RunAsync(command, description, reportFailure: false))
                {
                    errors.Add(description);
                }
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (errors.Count > 0)
            {
                CargoRunner.ShowWarning(DependenciesText.ReferenceFailures(errors.Count, string.Join("\n", errors)));
            }

            ProjectDependenciesSource.For(context.Hierarchy)?.Reload();
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
            ThreadHelper.JoinableTaskFactory.RunAsync(() => ShowReferenceManagerAsync(context)).FileAndForget("Kubuno/AddProjectReference");
#pragma warning restore VSSDK007
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

            // Path dependency folder -> the name it is declared under (its rename, if any).
            var referenced = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dependency in (ownPackage?.Dependencies ?? Array.Empty<CargoDependency>()).Where(d => d.Path != null))
            {
                referenced[Normalize(dependency.Path!)] = dependency.LocalName;
            }

            var result = new List<ReferenceCandidate>();
            foreach (var manifest in candidateManifests)
            {
                var name = ownMetadata?.Packages.FirstOrDefault(p => string.Equals(Normalize(p.ManifestPath), Normalize(manifest), StringComparison.OrdinalIgnoreCase))?.Name
                    ?? ReferenceCandidate.ReadCrateName(manifest);
                var directory = Normalize(Path.GetDirectoryName(manifest)!);
                referenced.TryGetValue(directory, out var declaredName);
                result.Add(new ReferenceCandidate(name, manifest, declaredName != null) { DeclaredName = declaredName });
            }

            // Path dependencies that are not projects of the solution: listed under Browse > Recent, checked.
            foreach (var pair in referenced)
            {
                if (result.Any(c => string.Equals(Normalize(c.ProjectDirectory), pair.Key, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var manifest = Path.Combine(pair.Key, "Cargo.toml");
                var name = File.Exists(manifest) ? ReferenceCandidate.ReadCrateName(manifest) : pair.Value;
                result.Add(new ReferenceCandidate(name, manifest, isReferenced: true, ReferenceOrigin.Browse) { DeclaredName = pair.Value });
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
