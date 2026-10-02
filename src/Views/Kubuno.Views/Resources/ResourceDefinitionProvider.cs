using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Desktop.Logic.Resources;
using Kubuno.Rust.Extensibility;
using Newtonsoft.Json.Linq;

namespace Kubuno.Desktop.Resources
{
    /// <summary>
    /// F12 on a <c>resources!</c> accessor (<c>Resources::logo()</c>) opens its entry in the <c>.kbres</c> file, and F12 on
    /// the generated type opens the file (docs/RESOURCES.md) - rust-analyzer alone would stop at the macro call. Uses
    /// rust-analyzer's own answer (the macro call it points at) when it has one, else the crate's <c>resources!</c> calls.
    /// </summary>
    [Export(typeof(IRustDefinitionProvider))]
    internal sealed class ResourceDefinitionProvider : IRustDefinitionProvider
    {
        public Task<JToken?> GetDefinitionAsync(JToken requestParameters, JToken? rustAnalyzerResponse, CancellationToken cancellationToken) =>
            Task.FromResult(Resolve(requestParameters, rustAnalyzerResponse));

        private static JToken? Resolve(JToken request, JToken? response)
        {
            var uri = (string?)request["textDocument"]?["uri"];
            var line = (int?)request["position"]?["line"];
            var character = (int?)request["position"]?["character"];
            if (uri is null || line is null || character is null)
            {
                return null;
            }

            var path = new Uri(Uri.UnescapeDataString(uri)).LocalPath;
            if (!File.Exists(path))
            {
                return null;
            }

            var text = File.ReadAllText(path);
            if (ResourceNavigation.IdentifierAt(text, Offset(text, line.Value, character.Value)) is not { } ident)
            {
                return null;
            }

            // The resources! call rust-analyzer points at, else every call of the crate whose type is the qualifier.
            foreach (var (callFile, call) in CallsFrom(response).Concat(CallsOfCrate(path, ident.Qualifier ?? ident.Identifier)))
            {
                if (ResourceNavigation.ResolveFile(callFile, call.Path) is not { } kbres)
                {
                    continue;
                }

                var target = ResourceNavigation.Target(call, File.ReadAllText(kbres), ident.Identifier, ident.Qualifier);
                if (target is { } t)
                {
                    var position = new JObject { ["line"] = t.Line, ["character"] = t.Character };
                    return new JArray(new JObject { ["uri"] = new Uri(kbres).AbsoluteUri, ["range"] = new JObject { ["start"] = position, ["end"] = position.DeepClone() } });
                }
            }

            return null;
        }

        /// <summary>The <c>resources!</c> calls at the locations of rust-analyzer's answer (Location, Location[] or LocationLink[]).</summary>
        private static System.Collections.Generic.IEnumerable<(string File, ResourceNavigation.MacroCall Call)> CallsFrom(JToken? response)
        {
            var items = response switch
            {
                JArray array => array.OfType<JObject>(),
                JObject single => new[] { single },
                _ => Enumerable.Empty<JObject>(),
            };
            foreach (var item in items)
            {
                var targetUri = (string?)item["uri"] ?? (string?)item["targetUri"];
                var range = item["range"] ?? item["targetSelectionRange"] ?? item["targetRange"];
                var startLine = (int?)range?["start"]?["line"];
                if (targetUri is null || startLine is null)
                {
                    continue;
                }

                var file = new Uri(Uri.UnescapeDataString(targetUri)).LocalPath;
                if (!file.EndsWith(".rs", StringComparison.OrdinalIgnoreCase) || !File.Exists(file))
                {
                    continue;
                }

                var lines = File.ReadAllLines(file);
                var snippet = string.Join(" ", lines.Skip(startLine.Value).Take(4));
                if (ResourceNavigation.ParseCall(snippet) is { } call)
                {
                    yield return (file, call);
                }
            }
        }

        /// <summary>The <c>resources!</c> calls of the crate of <paramref name="rustFile"/> generating the type <paramref name="typeName"/>.</summary>
        private static System.Collections.Generic.IEnumerable<(string File, ResourceNavigation.MacroCall Call)> CallsOfCrate(string rustFile, string typeName)
        {
            var src = Path.Combine(ProjectResources.ProjectRoot(rustFile), "src");
            if (!Directory.Exists(src))
            {
                yield break;
            }

            foreach (var file in Directory.EnumerateFiles(src, "*.rs", SearchOption.AllDirectories))
            {
                string text;
                try
                {
                    text = File.ReadAllText(file);
                }
                catch (IOException)
                {
                    continue;
                }

                if (text.IndexOf("resources!", StringComparison.Ordinal) < 0)
                {
                    continue;
                }

                foreach (var line in text.Split('\n'))
                {
                    if (ResourceNavigation.ParseCall(line) is { } call && call.TypeName == typeName)
                    {
                        yield return (file, call);
                    }
                }
            }
        }

        /// <summary>The offset of an LSP position (0-based line, UTF-16 column).</summary>
        private static int Offset(string text, int line, int character)
        {
            var offset = 0;
            for (var i = 0; i < line && offset < text.Length; i++)
            {
                var next = text.IndexOf('\n', offset);
                if (next < 0)
                {
                    return text.Length;
                }

                offset = next + 1;
            }

            return Math.Min(text.Length, offset + character);
        }
    }
}
