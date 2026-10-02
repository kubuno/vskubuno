using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kubuno.Views.Logging;
using Kubuno.Views.Logic;
using Newtonsoft.Json.Linq;

namespace Kubuno.Views.Designer.Registry
{
    /// <summary>
    /// The registry a project's design surface exports after a design build (docs/EVENTS.md EVT-7b,
    /// <c>--export-registry</c>, saved as <c>registry.json</c> next to it): its project controls marked <c>linked</c>
    /// are the ones compiled into the surface - what the Toolbox's "&lt;Project&gt; Composants" tab lists.
    /// </summary>
    public static class ProjectComponentsFile
    {
        /// <summary>The linked project controls of the export at <paramref name="registryPath"/> (none when unreadable).</summary>
        public static IReadOnlyList<ComponentMeta> ReadLinked(string registryPath)
        {
            try
            {
                var root = JToken.Parse(File.ReadAllText(registryPath));
                var components = root["components"] as JArray ?? new JArray();
                var project = new JArray(components.Where(c => (string?)c["origin"] == "project" && (bool?)c["linked"] == true));
                return ComponentRegistry.FromJson(project.ToString(Newtonsoft.Json.Formatting.None)).Components;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Newtonsoft.Json.JsonException or System.Text.Json.JsonException or ArgumentException)
            {
                KubunoViewsLogHost.Current.WriteLine("[designer] no registry of the design build at " + registryPath + ": " + ex.Message);
                return Array.Empty<ComponentMeta>();
            }
        }

        /// <summary>A control's key in the Toolbox choices ("Choisir des éléments…"): <c>crate::Name</c>.</summary>
        public static string ChoiceKey(ComponentMeta component) => (component.CrateName ?? string.Empty) + "::" + component.Name;

        /// <summary>
        /// The crates of the application itself that the project at <paramref name="manifestPath"/> depends on:
        /// its <c>path = "…"</c> dependencies of <c>[dependencies]</c>, and its <c>workspace = true</c> ones whose
        /// <c>[workspace.dependencies]</c> entry has a path - except the Kubuno framework's own crates
        /// (<see cref="FrameworkCrates"/>, an explicit list: a <c>kubuno-shell-controls</c> library is the
        /// application's). Their controls go to the project's Toolbox tab like its own (a control library of
        /// the application). Names are normalized (<c>-</c> as <c>_</c>); empty when the manifest is unreadable.
        /// </summary>
        public static HashSet<string> ApplicationDependencyCrates(string? manifestPath)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(manifestPath))
            {
                return result;
            }

            string text;
            try
            {
                text = File.ReadAllText(manifestPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                return result;
            }

            var workspacePaths = new Lazy<HashSet<string>>(() => WorkspacePathDependencies(Path.GetDirectoryName(manifestPath)));
            foreach (var (key, value) in Entries(text, "[dependencies]"))
            {
                var name = PackageName(key, value);
                if (FrameworkCrates.Contains(name))
                {
                    continue;
                }

                var compact = value.Replace(" ", string.Empty);
                var isPath = compact.Contains("path=");
                var isWorkspace = key.EndsWith(".workspace", StringComparison.Ordinal) || compact.Contains("workspace=true");
                if (isPath || (isWorkspace && workspacePaths.Value.Contains(Normalize(key.Replace(".workspace", string.Empty)))))
                {
                    result.Add(name);
                }
            }

            return result;
        }

        /// <summary>A crate name as the registry reports it (<c>foundations-controls</c> and <c>foundations_controls</c> are one crate).</summary>
        public static string Normalize(string? crate) => (crate ?? string.Empty).Trim().Trim('"').Replace('-', '_');

        /// <summary>The package a dependency line names: its <c>package = "…"</c>, else its key.</summary>
        private static string PackageName(string key, string value)
        {
            var at = value.IndexOf("package", StringComparison.Ordinal);
            if (at >= 0)
            {
                var rest = value.Substring(at + "package".Length).TrimStart();
                if (rest.StartsWith("=", StringComparison.Ordinal))
                {
                    var quoted = rest.Substring(1).Trim();
                    if (quoted.StartsWith("\"", StringComparison.Ordinal))
                    {
                        var end = quoted.IndexOf('"', 1);
                        if (end > 1)
                        {
                            return Normalize(quoted.Substring(1, end - 1));
                        }
                    }
                }
            }

            return Normalize(key.Replace(".workspace", string.Empty));
        }

        /// <summary>The <c>key = value</c> lines of one table of a manifest (one dependency per line).</summary>
        private static IEnumerable<(string Key, string Value)> Entries(string manifest, string table)
        {
            var inTable = false;
            foreach (var raw in manifest.Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith("[", StringComparison.Ordinal))
                {
                    inTable = string.Equals(line, table, StringComparison.Ordinal);
                    continue;
                }

                var eq = line.IndexOf('=');
                if (!inTable || eq <= 0 || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                yield return (line.Substring(0, eq).Trim(), line.Substring(eq + 1).Trim());
            }
        }

        /// <summary>The <c>[workspace.dependencies]</c> with a path of the Cargo workspace above <paramref name="packageDir"/>.</summary>
        private static HashSet<string> WorkspacePathDependencies(string? packageDir)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            var dir = packageDir is null ? null : Directory.GetParent(packageDir);
            while (dir is not null)
            {
                var manifest = Path.Combine(dir.FullName, "Cargo.toml");
                try
                {
                    if (File.Exists(manifest))
                    {
                        var text = File.ReadAllText(manifest);
                        if (text.Split('\n').Any(l => string.Equals(l.Trim(), "[workspace]", StringComparison.Ordinal)))
                        {
                            foreach (var (key, value) in Entries(text, "[workspace.dependencies]"))
                            {
                                if (value.Replace(" ", string.Empty).Contains("path="))
                                {
                                    result.Add(Normalize(key));
                                }
                            }

                            return result;
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return result;
                }

                dir = dir.Parent;
            }

            return result;
        }
    }
}
