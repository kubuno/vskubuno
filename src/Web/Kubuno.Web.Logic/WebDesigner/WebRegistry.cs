using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Kubuno.Web.Logic.WebDesigner
{
    /// <summary>
    /// The element registry of a web project as the Visual Studio designer reads it (docs/WEB-VIEWS.md section 4.5): the host
    /// registry (<c>@kubuno/ui</c>'s <c>kbview-registry.web.json</c>, same JSON shape as <c>kubuno/registry</c>) followed by the
    /// project's own registries (<c>kubuno.views.json</c> <c>registries</c>), whose entries are marked <c>"origin":
    /// "project"</c> and whose project-local modules are rewritten to project-root-relative specifiers
    /// (<c>./AppTileGrid</c> beside <c>src/core/shell/menus/kbview-controls.json</c> → <c>/src/core/shell/menus/AppTileGrid</c>),
    /// what <c>projectRegistryJson</c> of <c>project.ts</c> gives the compiler. A project entry never shadows a host one
    /// (the compiler's rule). Pure.
    /// </summary>
    public sealed class WebRegistry
    {
        private WebRegistry(string componentsJson, string projectComponentsJson, string version, int hostCount, int projectCount)
        {
            ComponentsJson = componentsJson;
            ProjectComponentsJson = projectComponentsJson;
            Version = version;
            HostCount = hostCount;
            ProjectCount = projectCount;
        }

        /// <summary>Every element, host first, as the bare JSON array <c>ComponentRegistry.FromJson</c> reads.</summary>
        public string ComponentsJson { get; }

        /// <summary>The project's own elements (rewritten modules, <c>origin: "project"</c>): the <c>projectComponents</c> message of the surface.</summary>
        public string ProjectComponentsJson { get; }

        /// <summary>A hash of every registry's content: changes when one of them does.</summary>
        public string Version { get; }

        public int HostCount { get; }

        public int ProjectCount { get; }

        /// <summary>Builds the registry of <paramref name="project"/> from its files; an unreadable project registry is skipped.</summary>
        public static WebRegistry Load(WebViewProject project, IWebProjectFiles? files = null)
        {
            files ??= PhysicalWebProjectFiles.Instance;
            var host = project.HostRegistryPath is { } hostPath ? files.ReadText(hostPath) : null;
            var projects = project.RegistryPaths
                .Select(p => (Path: project.RelativePath(p), Text: files.ReadText(p)))
                .Where(p => p.Text is not null)
                .Select(p => (p.Path, p.Text!))
                .ToList();
            return Build(host, projects);
        }

        /// <summary>
        /// Builds the registry from the host registry's text and the project registries' texts with their project-relative
        /// paths (<c>src/core/shell/menus/kbview-controls.json</c>). Malformed documents contribute nothing.
        /// </summary>
        public static WebRegistry Build(string? hostJson, IReadOnlyList<(string RelativePath, string Text)> projectRegistries)
        {
            var all = new JsonArray();
            var projectOnly = new JsonArray();
            var names = new HashSet<string>(StringComparer.Ordinal);
            var hostCount = 0;
            foreach (var component in Components(hostJson))
            {
                if (component["name"]?.GetValue<string>() is { Length: > 0 } name && names.Add(name))
                {
                    all.Add(component);
                    hostCount++;
                }
            }

            var projectCount = 0;
            foreach (var (relativePath, text) in projectRegistries ?? Array.Empty<(string, string)>())
            {
                var dir = PosixDirectory(relativePath);
                foreach (var component in Components(text))
                {
                    if (component["name"]?.GetValue<string>() is not { Length: > 0 } name || !names.Add(name))
                    {
                        continue;
                    }

                    component["origin"] = "project";
                    if (component["web"] is JsonObject web)
                    {
                        web["module"] = Rewrite(web["module"], dir);
                        if (web["alternates"] is JsonArray alternates)
                        {
                            foreach (var alternate in alternates.OfType<JsonObject>())
                            {
                                alternate["module"] = Rewrite(alternate["module"], dir);
                            }
                        }
                    }

                    all.Add(component);
                    projectOnly.Add(component.DeepClone());
                    projectCount++;
                }
            }

            var componentsJson = all.ToJsonString();
            return new WebRegistry(componentsJson, projectOnly.ToJsonString(), Hash(componentsJson), hostCount, projectCount);
        }

        private static IEnumerable<JsonObject> Components(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                yield break;
            }

            JsonNode? root;
            try
            {
                root = JsonNode.Parse(json!);
            }
            catch (JsonException)
            {
                yield break;
            }

            var array = root is JsonArray bare ? bare : root?["components"] as JsonArray;
            if (array is null)
            {
                yield break;
            }

            foreach (var item in array.ToList())
            {
                if (item is JsonObject component)
                {
                    array.Remove(component);
                    yield return component;
                }
            }
        }

        private static JsonNode? Rewrite(JsonNode? module, string dir)
        {
            if (module is not JsonValue value || !value.TryGetValue<string>(out var specifier) ||
                !(specifier.StartsWith("./", StringComparison.Ordinal) || specifier.StartsWith("../", StringComparison.Ordinal)))
            {
                return module?.DeepClone();
            }

            return "/" + NormalizePosix(dir.Length == 0 ? specifier : dir + "/" + specifier);
        }

        /// <summary><c>posix.normalize</c> of a relative path: <c>.</c> segments dropped, <c>..</c> folded.</summary>
        public static string NormalizePosix(string path)
        {
            var parts = new List<string>();
            foreach (var part in path.Split('/'))
            {
                if (part.Length == 0 || part == ".")
                {
                    continue;
                }

                if (part == ".." && parts.Count > 0 && parts[parts.Count - 1] != "..")
                {
                    parts.RemoveAt(parts.Count - 1);
                }
                else
                {
                    parts.Add(part);
                }
            }

            return string.Join("/", parts);
        }

        private static string PosixDirectory(string relativePath)
        {
            var path = (relativePath ?? string.Empty).Replace('\\', '/');
            var slash = path.LastIndexOf('/');
            return slash < 0 ? string.Empty : path.Substring(0, slash);
        }

        private static string Hash(string text)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
            return string.Concat(bytes.Take(8).Select(b => b.ToString("x2")));
        }
    }
}
