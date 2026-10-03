using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kubuno.Rust.Cargo.Metadata;
using Kubuno.Rust.Cargo.Naming;

namespace Kubuno.Rust.Logic.ProjectGeneration
{
    /// <summary>
    /// The solution of the desktop repository (docs/RSPROJ.md, "Project and solution names"): ONE <c>Kubuno.Desktop.slnx</c>
    /// at the repository root, over its two Cargo workspaces (<c>windows/</c>: the framework, the apps and the drive
    /// engine; <c>common/</c>: the multi-OS crates), every member as a <c>Kubuno.&lt;Product&gt;.&lt;Component&gt;.rsproj</c>
    /// (<see cref="ProjectNaming"/>) in one of the folders below. Pure apart from <see cref="WorkspaceManifests"/>, which
    /// lists the repository's workspace roots.
    /// </summary>
    public static class DesktopRepositoryLayout
    {
        public const string Applications = "/Applications/";

        public const string Framework = "/Framework/";

        public const string SharedControls = "/Shared controls/";

        public const string Common = "/Common (multi-OS)/";

        public const string DriveEngine = "/Drive engine/";

        public const string Tools = "/Tools/";

        /// <summary>The web side kept in the desktop repository for now (the web views compiler, <c>kubuno-web-*</c>).</summary>
        public const string Web = "/Web/";

        /// <summary>The folders in solution order.</summary>
        public static IReadOnlyList<string> FolderOrder { get; } = new[] { Applications, Framework, SharedControls, Common, DriveEngine, Tools, Web };

        /// <summary>The header controls every app can place (and the header's data), under their current and former names.</summary>
        private static readonly HashSet<string> SharedControlCrates = new HashSet<string>(StringComparer.Ordinal)
        {
            "kubuno-desktop-shell-controls", "kubuno-desktop-header-data",
            "kubuno-shell-controls", "kubuno-header-data",
        };

        /// <summary>Folders never searched for a workspace: build outputs, dependencies, vendored crates.</summary>
        private static readonly HashSet<string> SkippedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "target", "obj", "bin", "node_modules", "dist", "suppa", "vendor", "TestResults",
        };

        /// <summary>
        /// The folder of a member: <c>common/</c> → Common (multi-OS); <c>kubuno-web-*</c> → Web; the shell controls → Shared
        /// controls; a <c>kubuno-drive-desktop-*</c> library → Drive engine; a program under <c>windows/src/crates</c> → Tools;
        /// any other program → Applications; any other library → Framework.
        /// </summary>
        public static string FolderOf(RsprojProjectPlanItem member, string repositoryRoot)
        {
            if (member is null)
            {
                throw new ArgumentNullException(nameof(member));
            }

            var manifest = Normalize(member.ManifestPath);
            var root = Normalize(repositoryRoot).TrimEnd('/') + "/";
            var name = member.PackageName;
            if (manifest.StartsWith(root + "common/", StringComparison.OrdinalIgnoreCase))
            {
                return Common;
            }

            if (name.StartsWith("kubuno-web-", StringComparison.Ordinal) || name == "kubuno-views-web")
            {
                return Web;
            }

            if (SharedControlCrates.Contains(name))
            {
                return SharedControls;
            }

            if (member.HasBin)
            {
                return manifest.StartsWith(root + "windows/src/crates/", StringComparison.OrdinalIgnoreCase) ? Tools : Applications;
            }

            if (name.StartsWith("kubuno-drive-desktop", StringComparison.Ordinal) || name.StartsWith("drive-", StringComparison.Ordinal))
            {
                return DriveEngine;
            }

            return Framework;
        }

        /// <summary>
        /// The generation options of the desktop repository: every member (libraries included), named by
        /// <see cref="ProjectNaming.ForCrate"/>, a whole-workspace build when the workspace has several programs.
        /// </summary>
        public static RsprojGenerationOptions Options(string sdkVersion) =>
            new RsprojGenerationOptions(
                sdkVersion,
                includeLibraryOnlyMembers: true,
                resolveProjectName: package => ProjectNaming.ForCrate(package.Name),
                workspaceBuildForSeveralPrograms: true);

        /// <summary>
        /// The plan of every workspace of the repository (one <c>cargo metadata</c> result each) and of its solution at
        /// <paramref name="solutionPath"/> (<paramref name="existingSolution"/>: its text, or null when it does not exist).
        /// </summary>
        public static (IReadOnlyList<RsprojProjectPlanItem> Projects, RsprojSolutionPlan Solution) Plan(
            string repositoryRoot,
            IReadOnlyList<CargoMetadata> workspaces,
            string sdkVersion,
            Func<string, bool> projectFileExists,
            string solutionPath,
            string? existingSolution)
        {
            var options = Options(sdkVersion);
            var items = new List<RsprojProjectPlanItem>();
            foreach (var metadata in workspaces)
            {
                foreach (var item in RsprojGenerationPlanner.Plan(metadata, options, projectFileExists))
                {
                    if (items.All(existing => !string.Equals(existing.ProjectPath, item.ProjectPath, StringComparison.OrdinalIgnoreCase)))
                    {
                        items.Add(item);
                    }
                }
            }

            var solution = RsprojSlnxGenerator.PlanWithFolders(
                Path.GetDirectoryName(Path.GetFullPath(solutionPath))!,
                existingSolution,
                items,
                item => FolderOf(item, repositoryRoot),
                FolderOrder);
            return (items, solution);
        }

        /// <summary>
        /// "Generate Visual Studio Projects" in the desktop repository: reads every workspace (<paramref name="readMetadata"/>:
        /// <c>cargo metadata --no-deps</c> of a workspace manifest), creates the missing <c>.rsproj</c> files (an existing one is
        /// never touched) and creates or merges the solution at the repository root (an existing <c>Kubuno.*.slnx</c> there,
        /// else <c>Kubuno.Desktop.slnx</c>). Returns the solution's path, or null when the repository has no workspace.
        /// </summary>
        public static string? Generate(string repositoryRoot, Func<string, CargoMetadata> readMetadata, string sdkVersion, Action<string> log)
        {
            if (readMetadata is null)
            {
                throw new ArgumentNullException(nameof(readMetadata));
            }

            log ??= _ => { };
            var root = Path.GetFullPath(repositoryRoot).TrimEnd(Path.DirectorySeparatorChar);
            var manifests = WorkspaceManifests(root);
            if (manifests.Count == 0)
            {
                log("Kubuno: no Cargo workspace in '" + root + "'.");
                return null;
            }

            var workspaces = manifests.Select(manifest =>
            {
                log("Kubuno:   cargo metadata " + manifest);
                return readMetadata(manifest);
            }).ToList();

            var existingSolutions = Directory.GetFiles(root, "Kubuno.*.slnx");
            var solutionPath = existingSolutions.Length == 1 ? existingSolutions[0] : Path.Combine(root, ProjectNaming.DesktopSolutionFileName);
            var existing = File.Exists(solutionPath) ? File.ReadAllText(solutionPath) : null;
            var (projects, solution) = Plan(root, workspaces, sdkVersion, File.Exists, solutionPath, existing);
            foreach (var project in projects)
            {
                if (project.Action == RsprojPlanAction.SkipExisting)
                {
                    log("Kubuno:   kept     " + project.ProjectPath);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(project.ProjectPath)!);
                File.WriteAllText(project.ProjectPath, project.Content);
                log("Kubuno:   created  " + project.ProjectPath);
            }

            if (solution.Changed)
            {
                File.WriteAllText(solutionPath, solution.Content);
                log("Kubuno:   " + (existing is null ? "created  " : "updated  ") + solutionPath + (solution.AddedProjectNames.Count > 0 ? " (+" + string.Join(", ", solution.AddedProjectNames) + ")" : string.Empty));
            }
            else
            {
                log("Kubuno:   current  " + solutionPath);
            }

            return solutionPath;
        }

        /// <summary>
        /// The Cargo workspace roots of the repository: every <c>Cargo.toml</c> with a <c>[workspace]</c> table at the root
        /// or up to two folders below it (<c>windows/Cargo.toml</c>, <c>common/Cargo.toml</c>), build outputs and vendored
        /// crates skipped.
        /// </summary>
        public static IReadOnlyList<string> WorkspaceManifests(string repositoryRoot)
        {
            var result = new List<string>();
            void Visit(string directory, int depth)
            {
                var manifest = Path.Combine(directory, "Cargo.toml");
                try
                {
                    if (File.Exists(manifest) && WorkspaceManifestScanner.IsWorkspaceManifest(File.ReadAllText(manifest)))
                    {
                        result.Add(manifest);
                    }
                }
                catch (IOException)
                {
                    // Unreadable: not a workspace we can generate for.
                }

                if (depth == 2)
                {
                    return;
                }

                string[] children;
                try
                {
                    children = Directory.GetDirectories(directory);
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                {
                    return;
                }

                foreach (var child in children.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    var name = Path.GetFileName(child);
                    if (name.StartsWith(".", StringComparison.Ordinal) || SkippedFolders.Contains(name))
                    {
                        continue;
                    }

                    Visit(child, depth + 1);
                }
            }

            Visit(Path.GetFullPath(repositoryRoot), 0);
            return result;
        }

        private static string Normalize(string path) => (path ?? string.Empty).Replace('\\', '/');
    }
}
