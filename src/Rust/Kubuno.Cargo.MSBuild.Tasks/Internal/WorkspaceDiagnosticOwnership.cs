using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace Kubuno.Cargo.MSBuild.Tasks.Internal
{
    /// <summary>
    /// Which project of a solution reports a diagnostic of a workspace-wide build (docs/RSPROJ.md, "Cargo workspaces"): every
    /// project of the workspace receives the same list, and each diagnostic must reach the Error List exactly once, under
    /// the project it belongs to. The owner is the project whose folder most closely contains the diagnostic's file (a
    /// generated <c>.rsproj</c> sits next to its crate's <c>Cargo.toml</c>); a diagnostic no project contains (a file outside
    /// every project folder, or cargo's own error output) goes to one designated project: the first, by path, of the
    /// solution's projects inside the workspace.
    /// </summary>
    public static class WorkspaceDiagnosticOwnership
    {
        /// <summary>
        /// The project files named by Visual Studio's (and MSBuild's) <c>$(CurrentSolutionConfigurationContents)</c> - one
        /// <c>ProjectConfiguration</c> element per project, with its <c>AbsolutePath</c> - keeping those with the given
        /// extension. Empty when the property is empty (a project built on its own) or unreadable.
        /// </summary>
        public static IReadOnlyList<string> SolutionProjects(string? solutionConfigurationContents, string extension)
        {
            if (string.IsNullOrWhiteSpace(solutionConfigurationContents))
            {
                return Array.Empty<string>();
            }

            try
            {
                return XDocument.Parse(solutionConfigurationContents!)
                    .Descendants("ProjectConfiguration")
                    .Select(element => (string?)element.Attribute("AbsolutePath"))
                    .Where(path => !string.IsNullOrEmpty(path) && path!.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                    .Select(path => path!)
                    .ToList();
            }
            catch (XmlException)
            {
                return Array.Empty<string>();
            }
        }

        /// <summary>Whether <paramref name="selfProject"/> reports a diagnostic of <paramref name="file"/> (null or empty: no file).</summary>
        /// <param name="solutionProjects">The solution's project files (<see cref="SolutionProjects"/>); <paramref name="selfProject"/> is added when missing.</param>
        public static bool IsOwnedBy(string? file, string selfProject, IReadOnlyList<string> solutionProjects, string workspaceRoot)
        {
            var projects = solutionProjects.Contains(selfProject, StringComparer.OrdinalIgnoreCase)
                ? solutionProjects
                : solutionProjects.Concat(new[] { selfProject }).ToList();

            string? owner = null;
            if (!string.IsNullOrEmpty(file))
            {
                owner = projects
                    .Where(project => IsUnder(file!, Path.GetDirectoryName(project) ?? string.Empty))
                    .OrderByDescending(project => (Path.GetDirectoryName(project) ?? string.Empty).Length)
                    .FirstOrDefault();
            }

            owner ??= projects
                .Where(project => IsUnder(project, workspaceRoot))
                .OrderBy(project => project, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault()
                ?? selfProject;

            return string.Equals(owner, selfProject, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsUnder(string path, string directory)
        {
            if (string.IsNullOrEmpty(directory))
            {
                return false;
            }

            var normalizedDirectory = directory.Replace('/', '\\').TrimEnd('\\') + "\\";
            return path.Replace('/', '\\').StartsWith(normalizedDirectory, StringComparison.OrdinalIgnoreCase);
        }
    }
}
