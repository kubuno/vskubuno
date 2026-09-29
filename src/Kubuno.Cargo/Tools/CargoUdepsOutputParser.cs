using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Kubuno.Cargo.Tools
{
    /// <summary>
    /// Reads <c>cargo +nightly udeps --output json</c> (cargo-udeps' <c>Outcome</c>: <c>success</c>,
    /// and <c>unused_deps</c> keyed by package id, each with <c>manifest_path</c> and the
    /// <c>normal</c>/<c>development</c>/<c>build</c> name sets), the alternative to cargo-machete.
    /// </summary>
    public static class CargoUdepsOutputParser
    {
        public static IReadOnlyList<UnusedDependencies> Parse(string json)
        {
            var result = new List<UnusedDependencies>();
            if (string.IsNullOrWhiteSpace(json))
            {
                return result;
            }

            // Cargo's progress lines may precede the JSON object on the same stream.
            var start = json.IndexOf('{');
            if (start < 0)
            {
                return result;
            }

            using var document = JsonDocument.Parse(json.Substring(start));
            if (!document.RootElement.TryGetProperty("unused_deps", out var unused) || unused.ValueKind != JsonValueKind.Object)
            {
                return result;
            }

            foreach (var package in unused.EnumerateObject())
            {
                var manifest = package.Value.TryGetProperty("manifest_path", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() ?? string.Empty : string.Empty;
                var names = new List<string>();
                foreach (var table in new[] { "normal", "development", "build" })
                {
                    if (package.Value.TryGetProperty(table, out var set) && set.ValueKind == JsonValueKind.Array)
                    {
                        names.AddRange(set.EnumerateArray().Where(n => n.ValueKind == JsonValueKind.String).Select(n => n.GetString()!));
                    }
                }

                if (names.Count > 0)
                {
                    // The key is "name version (source)"; the name is its first word.
                    var name = package.Name.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? package.Name;
                    result.Add(new UnusedDependencies(name, manifest, names.Distinct(StringComparer.Ordinal).ToList()));
                }
            }

            return result;
        }
    }
}
