using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kubuno.Rust.Cargo.Toml;

namespace Kubuno.Web.Logic.Versions
{
    /// <summary>Severity of a <see cref="VersionFinding"/>, as <c>_tools/check_versions.py</c> prints them.</summary>
    public enum VersionSeverity
    {
        Ok,
        Warn,
        Fail,
    }

    /// <summary>One line of the audit.</summary>
    public sealed class VersionFinding
    {
        public VersionFinding(VersionSeverity severity, string repository, string message)
        {
            Severity = severity;
            Repository = repository;
            Message = message;
        }

        public VersionSeverity Severity { get; }

        public string Repository { get; }

        public string Message { get; }

        public override string ToString() => Severity.ToString().ToUpperInvariant().PadRight(4) + " " + Repository + ": " + Message;
    }

    /// <summary>A text edit the "prepare" actions apply to a file (never committed, never pushed: the user does that).</summary>
    public sealed class VersionEdit
    {
        public VersionEdit(string file, string description, string oldText, string newText, string? anchor = null)
        {
            File = file;
            Description = description;
            OldText = oldText;
            NewText = newText;
            Anchor = anchor;
        }

        /// <summary>When set, only the first <see cref="OldText"/> after this text is replaced (a package.json section).</summary>
        public string? Anchor { get; }

        public string File { get; }

        public string Description { get; }

        public string OldText { get; }

        public string NewText { get; }

        public override string ToString() => File + ": " + Description;
    }

    /// <summary>
    /// The read-only version checks of <c>_tools/check_versions.py</c> that do not need Python or the network
    /// (docs/WEB.md, "Version tools"), and the plans of the two "prepare" actions - the Windows equivalents of
    /// <c>bump_npm_floors.sh</c> and <c>bump_shared_crates.sh</c>, minus their commits.
    /// </summary>
    public static class VersionAudit
    {
        /// <summary>The npm libraries published from <c>core/frontend/packages</c>.</summary>
        public static readonly IReadOnlyList<string> NpmLibraries = new[] { "ui", "sdk", "drive" };

        /// <summary>The shared crates consumed by tag: crate name to tag prefix.</summary>
        public static readonly IReadOnlyDictionary<string, string> SharedCrates = new Dictionary<string, string>
        {
            ["kubuno-seccomp"] = "seccomp",
            ["kubuno-storage"] = "storage",
            ["kubuno-modauth"] = "modauth",
            ["kubuno-db"] = "db",
            ["kubuno-mcp"] = "mcp",
            ["kubuno-drive"] = "drive",
        };

        private static readonly Regex TagReference = new Regex(@"tag\s*=\s*""(?<prefix>[a-z]+)-v(?<version>\d+\.\d+\.\d+[^""]*)""", RegexOptions.CultureInvariant);

        /// <summary>The versions of <c>@kubuno/ui|sdk|drive</c> in the core's sources (CLAUDE.md: the source of truth).</summary>
        public static IReadOnlyDictionary<string, string> SourceNpmVersions(string coreDirectory)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var library in NpmLibraries)
            {
                var version = ReadJsonString(Path.Combine(coreDirectory, "frontend", "packages", library, "package.json"), "version");
                if (version is not null)
                {
                    result["@kubuno/" + library] = version;
                }
            }

            return result;
        }

        /// <summary>Cargo.toml == module.toml == frontend/package.json (check 1 of check_versions.py, without the tags).</summary>
        public static IEnumerable<VersionFinding> CheckAlignment(string repositoryDirectory)
        {
            var name = Path.GetFileName(repositoryDirectory.TrimEnd(Path.DirectorySeparatorChar));
            var versions = new List<(string Source, string Version)>();
            var cargo = Path.Combine(repositoryDirectory, "Cargo.toml");
            if (File.Exists(cargo))
            {
                var document = TomlDocument.Parse(File.ReadAllText(cargo));
                var version = document.GetValue("package", "version")?.AsString() ?? document.GetValue("workspace", "package", "version")?.AsString();
                if (version is not null)
                {
                    versions.Add(("Cargo.toml", version));
                }
            }

            var moduleToml = Path.Combine(repositoryDirectory, "module.toml");
            if (File.Exists(moduleToml))
            {
                var version = TomlDocument.Parse(File.ReadAllText(moduleToml)).GetValue("module", "version")?.AsString();
                if (version is not null)
                {
                    versions.Add(("module.toml", version));
                }
            }

            var packageVersion = ReadJsonString(Path.Combine(repositoryDirectory, "frontend", "package.json"), "version");
            if (packageVersion is not null)
            {
                versions.Add(("frontend/package.json", packageVersion));
            }

            if (versions.Count == 0)
            {
                yield break;
            }

            var distinct = versions.Select(entry => entry.Version).Distinct(StringComparer.Ordinal).ToList();
            yield return distinct.Count == 1
                ? new VersionFinding(VersionSeverity.Ok, name, "versions aligned (" + distinct[0] + ")")
                : new VersionFinding(VersionSeverity.Fail, name, "versions differ: " + string.Join(", ", versions.Select(entry => entry.Source + "=" + entry.Version)) + " (release.sh bumps them together)");
        }

        /// <summary>
        /// A module's <c>@kubuno/*</c> ranges against <paramref name="published"/> (check 2): a floor above the
        /// published version is a FAIL (the module needs an unpublished release), a floor below it a WARN that
        /// <see cref="PlanNpmFloors"/> fixes.
        /// </summary>
        public static IEnumerable<VersionFinding> CheckNpmFloors(string repositoryDirectory, IReadOnlyDictionary<string, string> published)
        {
            var name = Path.GetFileName(repositoryDirectory.TrimEnd(Path.DirectorySeparatorChar));
            foreach (var dependency in NpmDependencies(repositoryDirectory))
            {
                if (!published.TryGetValue(dependency.Package, out var version))
                {
                    continue;
                }

                var floor = Floor(dependency.Range);
                var comparison = CompareVersions(floor, version);
                if (comparison > 0)
                {
                    yield return new VersionFinding(VersionSeverity.Fail, name, dependency.Section + " " + dependency.Package + " " + dependency.Range + " requires more than the published " + version);
                }
                else if (comparison < 0)
                {
                    yield return new VersionFinding(VersionSeverity.Warn, name, dependency.Section + " " + dependency.Package + " " + dependency.Range + " (published " + version + ")");
                }
            }
        }

        /// <summary>The shared crate tags a module uses against the latest tag of each crate (check 3's propagation part).</summary>
        public static IEnumerable<VersionFinding> CheckSharedCrateTags(string repositoryDirectory, IReadOnlyDictionary<string, string> latestTags)
        {
            var name = Path.GetFileName(repositoryDirectory.TrimEnd(Path.DirectorySeparatorChar));
            var cargo = Path.Combine(repositoryDirectory, "Cargo.toml");
            if (!File.Exists(cargo))
            {
                yield break;
            }

            foreach (Match match in TagReference.Matches(File.ReadAllText(cargo)))
            {
                var prefix = match.Groups["prefix"].Value;
                if (!latestTags.TryGetValue(prefix, out var latest))
                {
                    continue;
                }

                var used = prefix + "-v" + match.Groups["version"].Value;
                if (!string.Equals(used, latest, StringComparison.Ordinal))
                {
                    yield return new VersionFinding(VersionSeverity.Warn, name, "uses " + used + ", latest tag is " + latest);
                }
            }
        }

        /// <summary>The latest <c>&lt;prefix&gt;-vX.Y.Z</c> tag of each prefix in <paramref name="tags"/> (the core's <c>git tag</c> list).</summary>
        public static IReadOnlyDictionary<string, string> LatestTags(IEnumerable<string> tags)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var tag in tags.Select(tag => tag.Trim()))
            {
                var match = Regex.Match(tag, @"^(?<prefix>[a-z]+)-v(?<version>\d+\.\d+\.\d+)$", RegexOptions.CultureInvariant);
                if (!match.Success)
                {
                    continue;
                }

                var prefix = match.Groups["prefix"].Value;
                if (!result.TryGetValue(prefix, out var current) || CompareVersions(match.Groups["version"].Value, current.Substring(prefix.Length + 2)) > 0)
                {
                    result[prefix] = tag;
                }
            }

            return result;
        }

        /// <summary><c>bump_npm_floors.sh</c>: every <c>@kubuno/*</c> range set to <c>^&lt;published&gt;</c> (lock files are left to npm).</summary>
        public static IReadOnlyList<VersionEdit> PlanNpmFloors(string repositoryDirectory, IReadOnlyDictionary<string, string> published)
        {
            var edits = new List<VersionEdit>();
            var file = Path.Combine(repositoryDirectory, "frontend", "package.json");
            foreach (var dependency in NpmDependencies(repositoryDirectory))
            {
                if (published.TryGetValue(dependency.Package, out var version) && dependency.Range != "^" + version && CompareVersions(Floor(dependency.Range), version) < 0)
                {
                    edits.Add(new VersionEdit(
                        file,
                        dependency.Section + " " + dependency.Package + " " + dependency.Range + " -> ^" + version,
                        "\"" + dependency.Package + "\": \"" + dependency.Range + "\"",
                        "\"" + dependency.Package + "\": \"^" + version + "\"",
                        "\"" + dependency.Section + "\""));
                }
            }

            return edits;
        }

        /// <summary><c>bump_shared_crates.sh</c>: every <c>tag = "&lt;prefix&gt;-v…"</c> moved to the latest tag (Cargo.lock is refreshed by cargo at the next build).</summary>
        public static IReadOnlyList<VersionEdit> PlanSharedCrateTags(string repositoryDirectory, IReadOnlyDictionary<string, string> latestTags)
        {
            var edits = new List<VersionEdit>();
            var file = Path.Combine(repositoryDirectory, "Cargo.toml");
            if (!File.Exists(file))
            {
                return edits;
            }

            foreach (Match match in TagReference.Matches(File.ReadAllText(file)))
            {
                var prefix = match.Groups["prefix"].Value;
                if (latestTags.TryGetValue(prefix, out var latest) && match.Value != "tag = \"" + latest + "\"" && !match.Value.Contains("\"" + latest + "\""))
                {
                    edits.Add(new VersionEdit(file, prefix + "-v" + match.Groups["version"].Value + " -> " + latest, match.Value, "tag = \"" + latest + "\""));
                }
            }

            return edits.GroupBy(edit => edit.OldText).Select(group => group.First()).ToList();
        }

        /// <summary>Applies edits (each old text replaced everywhere in its file). Returns the files changed.</summary>
        public static IReadOnlyList<string> Apply(IEnumerable<VersionEdit> edits)
        {
            var changed = new List<string>();
            foreach (var group in edits.GroupBy(edit => edit.File, StringComparer.OrdinalIgnoreCase))
            {
                var text = File.ReadAllText(group.Key);
                var updated = group.Aggregate(text, ApplyOne);
                if (!string.Equals(text, updated, StringComparison.Ordinal))
                {
                    File.WriteAllText(group.Key, updated, new System.Text.UTF8Encoding(false));
                    changed.Add(group.Key);
                }
            }

            return changed;
        }

        private static string ApplyOne(string text, VersionEdit edit)
        {
            if (edit.Anchor is null)
            {
                return text.Replace(edit.OldText, edit.NewText);
            }

            var anchor = text.IndexOf(edit.Anchor, StringComparison.Ordinal);
            var at = anchor < 0 ? -1 : text.IndexOf(edit.OldText, anchor, StringComparison.Ordinal);
            return at < 0 ? text : text.Substring(0, at) + edit.NewText + text.Substring(at + edit.OldText.Length);
        }

        /// <summary>Compares two <c>X.Y.Z</c> versions numerically (pre-release suffixes ignored).</summary>
        public static int CompareVersions(string left, string right)
        {
            var a = Parts(left);
            var b = Parts(right);
            for (var index = 0; index < 3; index++)
            {
                var comparison = a[index].CompareTo(b[index]);
                if (comparison != 0)
                {
                    return comparison;
                }
            }

            return 0;
        }

        /// <summary>The lowest version a range accepts: <c>^0.1.7</c> and <c>&gt;=0.1.7</c> give <c>0.1.7</c>.</summary>
        public static string Floor(string range) => Regex.Match(range ?? string.Empty, @"\d+(\.\d+){0,2}").Value;

        private static int[] Parts(string version)
        {
            var parts = (version ?? string.Empty).Split('-', '+')[0].Split('.');
            var result = new int[3];
            for (var index = 0; index < 3 && index < parts.Length; index++)
            {
                int.TryParse(parts[index], out result[index]);
            }

            return result;
        }

        private static IEnumerable<(string Section, string Package, string Range)> NpmDependencies(string repositoryDirectory)
        {
            var path = Path.Combine(repositoryDirectory, "frontend", "package.json");
            if (!File.Exists(path))
            {
                yield break;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var section in new[] { "dependencies", "devDependencies", "peerDependencies" })
            {
                if (!document.RootElement.TryGetProperty(section, out var dependencies) || dependencies.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (var dependency in dependencies.EnumerateObject())
                {
                    // Peer ranges state compatibility with the host, not a build floor: reported, never rewritten by the script either.
                    if (dependency.Name.StartsWith("@kubuno/", StringComparison.Ordinal) && dependency.Value.ValueKind == JsonValueKind.String && section != "peerDependencies")
                    {
                        yield return (section, dependency.Name, dependency.Value.GetString() ?? string.Empty);
                    }
                }
            }
        }

        private static string? ReadJsonString(string path, string property)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        }
    }
}
