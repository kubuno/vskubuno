using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Kubuno.Web.Logic.Generation
{
    /// <summary>
    /// The script debugger configurations of a frontend project (docs/WEB.md, "Solutions"). The generator writes them to
    /// <c>frontend\.kubuno\launch.json</c> and names that folder in the <c>.esproj</c> (<c>LaunchJsonFolder</c>), because
    /// the repositories' <c>.gitignore</c> excludes <c>.vscode</c>. Visual Studio 18's JavaScript project system was found
    /// live to ignore <c>LaunchJsonFolder</c> (relative, absolute, dotted or not): it only reads
    /// <c>.vscode\launch.json</c>, and without it F5 of the frontend fails with "Unable to start the previously selected
    /// debugger" - no browser, no TypeScript breakpoint. The committed file is therefore mirrored to the (ignored)
    /// <c>.vscode\launch.json</c> when that one does not exist; a developer's own <c>.vscode\launch.json</c> is never touched.
    /// The mirror lists the Chrome configurations first: Chrome is the browser F5 opens by default, Edge only a fallback
    /// one pick away (product owner's rule, 2026-10-02).
    /// </summary>
    public static class LaunchJsonMirror
    {
        /// <summary>The <c>LaunchJsonFolder</c> of a project file, or null (unset, or the project cannot be read).</summary>
        public static string? LaunchJsonFolder(string projectPath)
        {
            try
            {
                var value = XDocument.Load(projectPath).Descendants()
                    .Where(element => element.Name.LocalName == "LaunchJsonFolder")
                    .Select(element => element.Value.Trim())
                    .LastOrDefault(text => text.Length > 0);
                return value;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is System.Xml.XmlException)
            {
                return null;
            }
        }

        /// <summary>
        /// The copy to make for <paramref name="projectPath"/>: (source, target) when its <c>LaunchJsonFolder</c> holds a
        /// <c>launch.json</c> and <c>.vscode\launch.json</c> does not exist yet; null otherwise.
        /// </summary>
        public static (string Source, string Target)? Plan(string projectPath)
        {
            var folder = LaunchJsonFolder(projectPath);
            if (folder is null || folder.IndexOf("$(", StringComparison.Ordinal) >= 0)
            {
                return null; // Unset, or an MSBuild expression this reader does not evaluate.
            }

            var projectDirectory = Path.GetDirectoryName(Path.GetFullPath(projectPath))!;
            var source = Path.GetFullPath(Path.Combine(projectDirectory, folder, "launch.json"));
            var target = Path.Combine(projectDirectory, ".vscode", "launch.json");
            if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase) || !File.Exists(source) || File.Exists(target))
            {
                return null;
            }

            return (source, target);
        }

        /// <summary>Makes the copies <see cref="Plan"/> asks for, for every <c>.esproj</c> of a <c>.slnx</c>. Returns the files written.</summary>
        public static IReadOnlyList<string> EnsureForSolution(string solutionPath)
        {
            var written = new List<string>();
            var solutionDirectory = Path.GetDirectoryName(Path.GetFullPath(solutionPath))!;
            foreach (var project in StartupProjectPolicy.ProjectPaths(solutionPath)
                .Where(path => path.EndsWith(".esproj", StringComparison.OrdinalIgnoreCase)))
            {
                var projectPath = Path.Combine(solutionDirectory, project.Replace('/', Path.DirectorySeparatorChar));
                if (Plan(projectPath) is not { } copy)
                {
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(copy.Target)!);
                File.WriteAllText(copy.Target, ChromeFirst(File.ReadAllText(copy.Source)), new System.Text.UTF8Encoding(false));
                written.Add(copy.Target);
            }

            return written;
        }

        /// <summary>
        /// <paramref name="launchJson"/> with its Chrome configurations first (stable otherwise): the script debugger
        /// preselects the first configuration, and the product owner's rule is Chrome by default, Edge only as a fallback.
        /// Text that is not a launch.json object is returned unchanged.
        /// </summary>
        public static string ChromeFirst(string launchJson)
        {
            System.Text.Json.Nodes.JsonNode? root;
            try
            {
                root = System.Text.Json.Nodes.JsonNode.Parse(launchJson, documentOptions: new System.Text.Json.JsonDocumentOptions { CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true });
            }
            catch (System.Text.Json.JsonException)
            {
                return launchJson;
            }

            if (root is not System.Text.Json.Nodes.JsonObject launch || launch["configurations"] is not System.Text.Json.Nodes.JsonArray configurations)
            {
                return launchJson;
            }

            var ordered = configurations.Select((node, index) => (node, index))
                .OrderBy(entry => IsChrome(entry.node) ? 0 : 1)
                .ThenBy(entry => entry.index)
                .Select(entry => entry.node)
                .ToList();
            if (ordered.SequenceEqual(configurations))
            {
                return launchJson;
            }

            configurations.Clear();
            foreach (var node in ordered)
            {
                configurations.Add(node);
            }

            return launch.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n";
        }

        private static bool IsChrome(System.Text.Json.Nodes.JsonNode? configuration) =>
            configuration is System.Text.Json.Nodes.JsonObject entry && entry["type"] is System.Text.Json.Nodes.JsonValue type
            && type.TryGetValue<string>(out var text) && string.Equals(text, "chrome", StringComparison.OrdinalIgnoreCase);
    }
}
