using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Kubuno.Web.Logic.Modules
{
    /// <summary>
    /// Source maps of a module's frontend as deployed into the dev core (docs/WEB.md, "Frontend debugging"). Vite writes
    /// <c>dist/entry.js.map</c> with sources relative to <c>dist</c> (<c>../src/entry.ts</c>); the core serves the bundle at
    /// <c>/modules/&lt;id&gt;/entry.js</c>, so a browser resolves them to <c>/modules/src/entry.ts</c> - one URL for the
    /// <c>src/entry.ts</c> of EVERY module, which no <c>sourceMapPathOverrides</c> can tell apart, and Visual Studio's
    /// script debugger never bound a breakpoint of the module's TypeScript (found live on drive). The deployed copy of a
    /// map therefore names its sources by their absolute paths in the module's checkout, which the script debugger maps
    /// to the files open in Visual Studio directly (a watching Vite build into the dev core writes such maps already).
    /// </summary>
    public static class SourceMapPaths
    {
        /// <summary>Whether <paramref name="path"/> is a source map (<c>.map</c>).</summary>
        public static bool IsSourceMap(string path) => path.EndsWith(".map", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// <paramref name="mapJson"/> with every relative entry of <c>sources</c> resolved against
        /// <paramref name="mapDirectory"/> (the folder the map was built in) and <c>sourceRoot</c>, as an absolute path with
        /// forward slashes; null when nothing changes or the text is not a source map. URLs (<c>webpack://</c>,
        /// <c>http://</c>...) and absolute paths are kept.
        /// </summary>
        public static string? MakeSourcesAbsolute(string mapJson, string mapDirectory)
        {
            if (string.IsNullOrWhiteSpace(mapJson) || string.IsNullOrWhiteSpace(mapDirectory))
            {
                return null;
            }

            JsonNode? root;
            try
            {
                root = JsonNode.Parse(mapJson);
            }
            catch (JsonException)
            {
                return null;
            }

            if (root is not JsonObject map || map["sources"] is not JsonArray sources)
            {
                return null;
            }

            var sourceRoot = map["sourceRoot"] is JsonValue rootValue && rootValue.TryGetValue<string>(out var text) ? text : string.Empty;
            if (IsUrl(sourceRoot))
            {
                return null; // Sources relative to a URL: the browser resolves them, not the file system.
            }

            var baseDirectory = string.IsNullOrEmpty(sourceRoot) || sourceRoot == "./" ? mapDirectory : Path.Combine(mapDirectory, sourceRoot);
            var changed = false;
            for (var index = 0; index < sources.Count; index++)
            {
                if (sources[index] is not JsonValue value || !value.TryGetValue<string>(out var source) || string.IsNullOrEmpty(source)
                    || IsUrl(source) || Path.IsPathRooted(source))
                {
                    continue;
                }

                sources[index] = Path.GetFullPath(Path.Combine(baseDirectory, source.Replace('/', Path.DirectorySeparatorChar))).Replace('\\', '/');
                changed = true;
            }

            if (!changed)
            {
                return null;
            }

            if (map.ContainsKey("sourceRoot"))
            {
                map["sourceRoot"] = string.Empty;
            }

            return map.ToJsonString();
        }

        private static bool IsUrl(string text) => text.IndexOf("://", StringComparison.Ordinal) > 0;
    }
}
