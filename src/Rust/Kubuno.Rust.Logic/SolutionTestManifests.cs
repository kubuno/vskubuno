using System;
using System.Collections.Generic;
using System.IO;

namespace Kubuno.Rust.Logic
{
    /// <summary>One <c>.rsproj</c> of the open solution, as Test Explorer's container source sees it.</summary>
    public sealed class RsprojTestProject
    {
        public RsprojTestProject(string manifestPath, string? workspaceRoot, bool workspaceBuild)
        {
            ManifestPath = manifestPath ?? throw new ArgumentNullException(nameof(manifestPath));
            WorkspaceRoot = workspaceRoot;
            WorkspaceBuild = workspaceBuild;
        }

        /// <summary>The project's <c>$(CargoManifestPath)</c>.</summary>
        public string ManifestPath { get; }

        /// <summary>The project's <c>$(CargoWorkspaceRoot)</c>, when known.</summary>
        public string? WorkspaceRoot { get; }

        /// <summary><c>$(CargoBuildScope)</c> is <c>Workspace</c>.</summary>
        public bool WorkspaceBuild { get; }
    }

    /// <summary>
    /// Which <c>Cargo.toml</c> manifests Test Explorer gets as containers for an open solution (docs/RSPROJ.md, "Cargo
    /// workspaces"): normally each project's own manifest (<c>cargo test</c> of that package only); for a project that
    /// builds its whole workspace (a workspace sharing a Rust dylib), the workspace's root manifest instead, once - all its
    /// test programs are then built by one command, against the same build of the dylib, rather than one command per package
    /// rebuilding the dylib with that package's features and leaving the other test programs linked against a DLL that no
    /// longer matches them.
    /// </summary>
    public static class SolutionTestManifests
    {
        public static IReadOnlyList<string> Select(IEnumerable<RsprojTestProject> projects)
        {
            if (projects is null)
            {
                throw new ArgumentNullException(nameof(projects));
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var workspaceRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();
            var perPackage = new List<string>();

            foreach (var project in projects)
            {
                if (project.WorkspaceBuild && !string.IsNullOrEmpty(project.WorkspaceRoot))
                {
                    var root = Normalize(project.WorkspaceRoot!);
                    workspaceRoots.Add(root);
                    var rootManifest = Path.Combine(root, "Cargo.toml");
                    if (seen.Add(rootManifest))
                    {
                        result.Add(rootManifest);
                    }
                }
                else
                {
                    perPackage.Add(project.ManifestPath);
                }
            }

            foreach (var manifest in perPackage)
            {
                // A package of a workspace already tested as a whole would only be built and listed twice.
                var directory = Path.GetDirectoryName(manifest);
                if (directory is not null && IsUnderAny(Normalize(directory), workspaceRoots))
                {
                    continue;
                }

                if (seen.Add(manifest))
                {
                    result.Add(manifest);
                }
            }

            return result;
        }

        private static bool IsUnderAny(string directory, HashSet<string> roots)
        {
            foreach (var root in roots)
            {
                if (string.Equals(directory, root, StringComparison.OrdinalIgnoreCase)
                    || directory.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string Normalize(string path) => path.Replace('/', '\\').TrimEnd('\\');
    }
}
