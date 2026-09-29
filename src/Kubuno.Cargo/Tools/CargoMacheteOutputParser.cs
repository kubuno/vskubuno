using System;
using System.Collections.Generic;

namespace Kubuno.Cargo.Tools
{
    /// <summary>The unused dependencies <c>cargo machete</c> reported for one package.</summary>
    public sealed class UnusedDependencies
    {
        public UnusedDependencies(string packageName, string manifestPath, IReadOnlyList<string> dependencies)
        {
            PackageName = packageName;
            ManifestPath = manifestPath;
            Dependencies = dependencies;
        }

        public string PackageName { get; }

        /// <summary>As printed by cargo-machete (may be relative to the directory it ran in).</summary>
        public string ManifestPath { get; }

        /// <summary>The dependencies' names as written in Cargo.toml (renames included).</summary>
        public IReadOnlyList<string> Dependencies { get; }
    }

    /// <summary>
    /// Reads <c>cargo machete</c>'s report ("Remove unused dependencies..." on the Dependencies node):
    /// after the "found the following unused dependencies" line, a <c>package -- path/Cargo.toml:</c>
    /// header per package, then one indented dependency name per line, up to a blank line.
    /// Exit code 0 = nothing unused, 1 = something unused, 2 = error.
    /// </summary>
    public static class CargoMacheteOutputParser
    {
        public static IReadOnlyList<UnusedDependencies> Parse(IEnumerable<string> lines)
        {
            if (lines is null)
            {
                throw new ArgumentNullException(nameof(lines));
            }

            var result = new List<UnusedDependencies>();
            string? package = null;
            string? manifest = null;
            List<string>? names = null;

            void Flush()
            {
                if (package != null && names != null && names.Count > 0)
                {
                    result.Add(new UnusedDependencies(package, manifest ?? string.Empty, names));
                }

                package = null;
                manifest = null;
                names = null;
            }

            foreach (var raw in lines)
            {
                var line = raw ?? string.Empty;
                if (line.Trim().Length == 0)
                {
                    Flush();
                    continue;
                }

                var indented = line.StartsWith("\t", StringComparison.Ordinal) || line.StartsWith("  ", StringComparison.Ordinal);
                if (indented && names != null)
                {
                    names.Add(line.Trim());
                    continue;
                }

                var separator = line.IndexOf(" -- ", StringComparison.Ordinal);
                if (!indented && separator > 0 && line.TrimEnd().EndsWith(":", StringComparison.Ordinal))
                {
                    Flush();
                    package = line.Substring(0, separator).Trim();
                    var path = line.Substring(separator + 4).TrimEnd();
                    manifest = path.Substring(0, path.Length - 1);
                    names = new List<string>();
                }
            }

            Flush();
            return result;
        }
    }
}
