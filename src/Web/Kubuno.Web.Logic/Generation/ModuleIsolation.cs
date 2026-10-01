using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kubuno.Web.Logic.Generation
{
    /// <summary>
    /// Module isolation (docs/VIEWS-SPEC.md, "Module isolation"; docs/WEB.md): a Kubuno web module never uses another
    /// module's code, nor the core's sources. It builds alone against the published <c>@kubuno/*</c> npm packages and
    /// the shared crates' git tags, as in CI; the only shared code at run time is the host's import-map singletons and the
    /// core's extension points. This check reads a module's manifests and reports what reaches outside its repository:
    /// a Cargo <c>path</c> dependency, an npm <c>file:</c>/<c>link:</c>/<c>portal:</c> dependency, a <c>tsconfig.json</c>
    /// path mapping, a Vite alias. The solution generator refuses a multi-repository solution in which a module does so,
    /// and never writes a reference between two repositories itself.
    /// </summary>
    public static class ModuleIsolation
    {
        private static readonly Regex CargoPath = new Regex(@"path\s*=\s*""(?<path>[^""]+)""", RegexOptions.CultureInvariant);
        private static readonly Regex QuotedRelative = new Regex(@"['""](?<path>\.\.?/[^'""]*)['""]", RegexOptions.CultureInvariant);

        /// <summary>The references of <paramref name="repository"/> (a module) that leave it. Empty for the core.</summary>
        public static IReadOnlyList<string> Violations(WebRepository repository)
        {
            var violations = new List<string>();
            if (repository is null || repository.Kind != WebRepositoryKind.Module)
            {
                return violations;
            }

            foreach (var member in repository.Members)
            {
                var manifest = Path.Combine(member.Directory, "Cargo.toml");
                foreach (Match match in CargoPath.Matches(File.ReadAllText(manifest)))
                {
                    var target = match.Groups["path"].Value;
                    // [[bin]] path = "src/main.rs" and friends are files of the package itself.
                    if (Leaves(repository.Root, member.Directory, target))
                    {
                        violations.Add(Rel(repository.Root, manifest) + ": Cargo path dependency '" + target + "' outside the module (use the shared crate's git tag)");
                    }
                }
            }

            foreach (var frontend in repository.Frontends)
            {
                var packageJson = Path.Combine(frontend.Directory, "package.json");
                if (File.Exists(packageJson))
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(packageJson));
                    foreach (var section in new[] { "dependencies", "devDependencies", "peerDependencies", "optionalDependencies" })
                    {
                        if (!document.RootElement.TryGetProperty(section, out var dependencies) || dependencies.ValueKind != JsonValueKind.Object)
                        {
                            continue;
                        }

                        foreach (var dependency in dependencies.EnumerateObject())
                        {
                            var spec = dependency.Value.ValueKind == JsonValueKind.String ? dependency.Value.GetString() ?? string.Empty : string.Empty;
                            var local = Regex.Match(spec, "^(file|link|portal):(?<path>.+)$");
                            if (local.Success && Leaves(repository.Root, frontend.Directory, local.Groups["path"].Value))
                            {
                                violations.Add(Rel(repository.Root, packageJson) + ": " + dependency.Name + " = \"" + spec + "\" outside the module (use the published npm package)");
                            }
                        }
                    }
                }

                foreach (var config in new[] { "tsconfig.json", "tsconfig.app.json", "vite.config.ts", "vite.config.js", "vite.config.mts" })
                {
                    var path = Path.Combine(frontend.Directory, config);
                    if (!File.Exists(path))
                    {
                        continue;
                    }

                    foreach (Match match in QuotedRelative.Matches(File.ReadAllText(path)))
                    {
                        var target = match.Groups["path"].Value;
                        if (Leaves(repository.Root, frontend.Directory, target))
                        {
                            violations.Add(Rel(repository.Root, path) + ": '" + target + "' reaches outside the module (another module or the core's sources)");
                        }
                    }
                }
            }

            return violations;
        }

        /// <summary>Whether <paramref name="relative"/>, from <paramref name="from"/>, ends up outside <paramref name="root"/>.</summary>
        public static bool Leaves(string root, string from, string relative)
        {
            if (string.IsNullOrWhiteSpace(relative) || relative.Contains("://"))
            {
                return false;
            }

            string target;
            try
            {
                target = Path.GetFullPath(Path.IsPathRooted(relative) ? relative : Path.Combine(from, relative.Replace('/', Path.DirectorySeparatorChar)));
            }
            catch (Exception exception) when (exception is ArgumentException || exception is NotSupportedException || exception is PathTooLongException)
            {
                return false;
            }

            var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return !(target + Path.DirectorySeparatorChar).StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Refuses (throws) when a project dependency of the planned solution joins two repositories, or when a module of
        /// a multi-repository solution reaches outside itself. Called by <see cref="WebSolutionGenerator.Plan"/>.
        /// </summary>
        public static void EnsureIsolated(IReadOnlyList<WebRepository> repositories, IEnumerable<(string From, string To)> dependencies)
        {
            string? RepositoryOf(string path) => repositories
                .Where(repository => (Path.GetFullPath(path) + Path.DirectorySeparatorChar).StartsWith(repository.Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                .Select(repository => repository.Root)
                .FirstOrDefault();

            foreach (var (from, to) in dependencies)
            {
                if (!string.Equals(RepositoryOf(from), RepositoryOf(to), StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Module isolation: '" + from + "' would depend on '" + to + "', a project of another repository. Kubuno modules never reference each other nor the core's sources (docs/WEB.md).");
                }
            }

            if (repositories.Count > 1)
            {
                var violations = repositories.SelectMany(repository => Violations(repository).Select(violation => repository.Name + ": " + violation)).ToList();
                if (violations.Count > 0)
                {
                    throw new InvalidOperationException("Module isolation: these references leave their module (docs/WEB.md):\n" + string.Join("\n", violations));
                }
            }
        }

        private static string Rel(string root, string path) => path.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar).Replace('\\', '/');
    }
}
