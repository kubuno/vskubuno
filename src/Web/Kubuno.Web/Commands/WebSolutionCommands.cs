using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Kubuno.Core;
using Kubuno.Core.Logging;
using Kubuno.Rust.Logic.ProjectGeneration;
using Kubuno.Web.Logic.Generation;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace Kubuno.Web.Commands
{
    /// <summary>
    /// "Kubuno: Generate Web Solution" and "Kubuno: Generate Multi-Repository Web Solution" (docs/WEB.md,
    /// "Solutions"): the Visual Studio files of the core or of a module (<see cref="WebSolutionGenerator"/>), and the
    /// solution's own SDK feed so the checkout restores Kubuno.Rust.Sdk and Kubuno.Web.Sdk without the extension.
    /// Project files that exist are never touched; an existing solution only gets its missing projects.
    /// </summary>
    internal static class WebSolutionCommands
    {
        public static void Initialize(AsyncPackage package, OleMenuCommandService commandService)
        {
#pragma warning disable VSTHRD010 // menu command handlers are always invoked on the UI thread.
            commandService.AddCommand(new OleMenuCommand((_, _) => Run(package, multi: false), new CommandID(KubunoGuids.CommandSet, PackageIds.GenerateWebSolutionCommand)));
            commandService.AddCommand(new OleMenuCommand((_, _) => Run(package, multi: true), new CommandID(KubunoGuids.CommandSet, PackageIds.GenerateMultiRepoSolutionCommand)));
#pragma warning restore VSTHRD010
        }

        private static void Run(AsyncPackage package, bool multi)
        {
            _ = package.JoinableTaskFactory.RunAsync(async () =>
            {
                await package.JoinableTaskFactory.SwitchToMainThreadAsync();
                try
                {
                    if (multi)
                    {
                        GenerateMulti(package);
                    }
                    else
                    {
                        GenerateSingle(package);
                    }
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is InvalidOperationException || exception is ArgumentException || exception is Kubuno.Rust.Cargo.Toml.TomlParseException || exception is Kubuno.Web.Logic.Modules.ModuleManifestException)
                {
                    KubunoLog.WriteException("Kubuno web: solution generation failed", exception);
                    WebUi.ShowMessage(package, "The web solution could not be generated: " + exception.Message, error: true);
                }
            });
        }

        private static void GenerateSingle(AsyncPackage package)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var repository = WebUi.ResolveRepository(package, "Choose the Kubuno core or module repository");
            if (repository is null)
            {
                return;
            }

            var solutionPath = ExistingSolution(repository.Root) ?? WebSolutionGenerator.DefaultSolutionPath(repository);
            Write(package, new[] { repository }, solutionPath);
        }

        private static void GenerateMulti(AsyncPackage package)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var start = WebUi.ResolveRepository(package, null, quiet: true);
            var parent = start is not null ? Path.GetDirectoryName(start.Root) : WebUi.OpenSolutionFolderWithCore();
            parent ??= WebUi.BrowseFolder(package, "Choose the folder holding the Kubuno repositories (core, drive, calendar...)");
            if (parent is null)
            {
                return;
            }

            var repositories = Directory.GetDirectories(parent)
                .Select(directory => WebRepository.Detect(directory, out _))
                .Where(repository => repository is not null)
                .Select(repository => repository!)
                .OrderBy(repository => repository.Kind == WebRepositoryKind.Core ? 0 : 1)
                .ThenBy(repository => repository.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (repositories.Count == 0)
            {
                WebUi.ShowMessage(package, "No Kubuno repository (core or module) in '" + parent + "'.", error: true);
                return;
            }

            var preselected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "core" };
            if (start is not null)
            {
                preselected.Add(start.Name);
            }

            var dialog = new RepositoryPickerDialog(parent, repositories.Select(repository => (repository.Name, repository.Kind == WebRepositoryKind.Core ? "Kubuno Core Web (the server)" : "module " + repository.Id, preselected.Contains(repository.Name))).ToList());
            if (dialog.ShowModal() != true)
            {
                return;
            }

            var chosen = repositories.Where(repository => dialog.SelectedNames.Contains(repository.Name)).ToList();
            if (chosen.Count == 0)
            {
                return;
            }

            Write(package, chosen, Path.Combine(parent, "Kubuno.Web.slnx"));
        }

        private static void Write(AsyncPackage package, IReadOnlyList<WebRepository> repositories, string solutionPath)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var existing = File.Exists(solutionPath) ? File.ReadAllText(solutionPath) : null;
            var files = WebSolutionGenerator.Plan(repositories, solutionPath, WebSdkVersions.Current, existing);
            var report = new StringBuilder();
            foreach (var file in files)
            {
                var exists = File.Exists(file.Path);
                if (exists && !file.Mergeable)
                {
                    report.Append("  kept     ").Append(file.Path).Append('\n');
                    continue;
                }

                if (exists && string.Equals(File.ReadAllText(file.Path), file.Content, StringComparison.Ordinal))
                {
                    report.Append("  current  ").Append(file.Path).Append('\n');
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(file.Path)!);
                File.WriteAllText(file.Path, file.Content, new UTF8Encoding(false));
                report.Append(exists ? "  updated  " : "  created  ").Append(file.Path).Append('\n');
            }

            foreach (var repository in repositories)
            {
                report.Append("  ").Append(repository.Name).Append(":\n");
                foreach (var line in WebSolutionGenerator.Describe(repository))
                {
                    report.Append("    ").Append(line).Append('\n');
                }

                foreach (var violation in ModuleIsolation.Violations(repository))
                {
                    report.Append("    WARNING module isolation: ").Append(violation).Append('\n');
                }
            }

            var solutionDirectory = Path.GetDirectoryName(solutionPath)!;
            WebSdkFeed.EnsureSolutionLocal(solutionDirectory, line => KubunoLog.WriteLine(line));
            KubunoLog.WriteLine("Kubuno web: " + solutionPath + "\n" + report);
            KubunoLog.Activate();

            var open = WebUi.Ask(package, "Generated " + Path.GetFileName(solutionPath) + " (details in the \"Kubuno\" Output pane).\n\nOpen it now?");
            if (open && Package.GetGlobalService(typeof(SVsSolution)) is IVsSolution solution)
            {
                solution.OpenSolutionFile(0, solutionPath);
                SetStartupProject(WebSolutionGenerator.StartupProject(repositories, solutionPath));
            }
            else if (WebSolutionGenerator.StartupProject(repositories, solutionPath) is { } startup)
            {
                KubunoLog.WriteLine("Kubuno web: set " + startup + " as the startup project once the solution is open (Visual Studio keeps it per user).");
            }
        }

        /// <summary>The deliberate startup project (kubuno-core, or the module's backend): never a library crate.</summary>
        private static void SetStartupProject(string? startup)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (startup is null || Package.GetGlobalService(typeof(EnvDTE.DTE)) is not EnvDTE.DTE dte)
            {
                return;
            }

            try
            {
                // An array of unique names: a plain string is refused (E_INVALIDARG) for a CPS project.
                dte.Solution.SolutionBuild.StartupProjects = new object[] { startup };
                KubunoLog.WriteLine("Kubuno web: startup project " + startup + ".");
            }
            catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException || exception is ArgumentException)
            {
                KubunoLog.WriteLine("Kubuno web: could not make " + startup + " the startup project (" + exception.Message + "); set it from Solution Explorer.");
            }
        }

        /// <summary>An existing <c>Kubuno.*.slnx</c> at the repository root (merged into rather than creating a second one).</summary>
        private static string? ExistingSolution(string root)
        {
            var candidates = Directory.GetFiles(root, "Kubuno.*.slnx");
            return candidates.Length == 1 ? candidates[0] : null;
        }
    }

    /// <summary>
    /// The SDK packages a web solution restores - Kubuno.Rust.Sdk and Kubuno.Web.Sdk - copied side by side into the
    /// solution's <c>.kubuno\sdk-feed</c> by the Rust layer's <see cref="SdkFeedDistribution"/> (it carries every
    /// Kubuno SDK the extension bundles).
    /// </summary>
    internal static class WebSdkFeed
    {
        public static void EnsureSolutionLocal(string solutionDirectory, Action<string> log)
        {
            if (!SdkFeedDistribution.EnsureSolutionLocal(solutionDirectory, KubunoExtension.InstallDirectory, log))
            {
                log("Kubuno web: no bundled SDK feed in this build of the extension - the projects restore Kubuno.Rust.Sdk/Kubuno.Web.Sdk from the sources in your NuGet.Config.");
            }
        }
    }
}