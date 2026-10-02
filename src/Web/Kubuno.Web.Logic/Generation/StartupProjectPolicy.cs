using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Kubuno.Web.Logic.Generation
{
    /// <summary>
    /// The startup project of a Kubuno web solution opened WITHOUT user options yet (docs/WEB.md, "Solutions"). Visual
    /// Studio keeps the startup project per user, in <c>.vs\&lt;solution file&gt;\v18\.suo</c>; with none it picks a project
    /// itself, and for a module's solution it was found live to pick the frontend <c>.esproj</c> - whose F5 only runs a
    /// watching Vite build and a browser, with no core and no module. Such a solution gets the program F5 is for
    /// (<see cref="WebSolutionGenerator.StartupProject"/>: <c>kubuno-core</c>, else the module's backend); a solution that
    /// already has user options keeps the developer's choice.
    /// </summary>
    public static class StartupProjectPolicy
    {
        /// <summary>The user options file Visual Studio 18 writes for <paramref name="solutionPath"/>.</summary>
        public static string UserOptionsFile(string solutionPath)
        {
            if (string.IsNullOrWhiteSpace(solutionPath))
            {
                throw new ArgumentException("No solution path.", nameof(solutionPath));
            }

            var full = Path.GetFullPath(solutionPath);
            return Path.Combine(Path.GetDirectoryName(full)!, ".vs", Path.GetFileName(full), "v18", ".suo");
        }

        /// <summary>
        /// The startup project to set when <paramref name="solutionPath"/> is opened with no user options
        /// (<paramref name="hadUserOptions"/> false): the relative path <c>SolutionBuild.StartupProjects</c> takes, or null
        /// to leave Visual Studio's choice (user options exist, or the solution is not a Kubuno web solution).
        /// </summary>
        public static string? ForFreshSolution(string solutionPath, bool hadUserOptions)
        {
            if (hadUserOptions || string.IsNullOrWhiteSpace(solutionPath) || !File.Exists(solutionPath))
            {
                return null;
            }

            var repositories = Repositories(solutionPath);
            return repositories.Count == 0 ? null : WebSolutionGenerator.StartupProject(repositories, solutionPath);
        }

        /// <summary>
        /// The Kubuno repositories of a solution: its own folder (a single-repository solution), else the repositories
        /// its projects live in (a multi-repository <c>Kubuno.Web.slnx</c> next to them), in the order they appear.
        /// </summary>
        public static IReadOnlyList<WebRepository> Repositories(string solutionPath)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(solutionPath))!;
            var own = WebRepository.Detect(directory, out _);
            if (own is not null)
            {
                return new[] { own };
            }

            var repositories = new List<WebRepository>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var projectPath in ProjectPaths(solutionPath))
            {
                var first = projectPath.Replace('\\', '/').Split('/').FirstOrDefault();
                if (string.IsNullOrEmpty(first) || first == ".." || !seen.Add(first!))
                {
                    continue;
                }

                var repository = WebRepository.Detect(Path.Combine(directory, first!), out _);
                if (repository is not null)
                {
                    repositories.Add(repository);
                }
            }

            return repositories;
        }

        /// <summary>The <c>Path</c> of every <c>&lt;Project&gt;</c> of a <c>.slnx</c> (empty for another format or an unreadable file).</summary>
        public static IReadOnlyList<string> ProjectPaths(string solutionPath)
        {
            if (!string.Equals(Path.GetExtension(solutionPath), ".slnx", StringComparison.OrdinalIgnoreCase))
            {
                return Array.Empty<string>();
            }

            try
            {
                return XDocument.Load(solutionPath).Descendants("Project")
                    .Select(project => (string?)project.Attribute("Path"))
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Select(path => path!)
                    .ToList();
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is System.Xml.XmlException)
            {
                return Array.Empty<string>();
            }
        }
    }
}
