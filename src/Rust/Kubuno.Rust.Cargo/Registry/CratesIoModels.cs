using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Kubuno.Rust.Cargo.Internal;

namespace Kubuno.Rust.Cargo.Registry
{
    /// <summary>One crate of a crates.io search (<c>GET /api/v1/crates?q=</c>).</summary>
    public sealed class CrateSearchResult
    {
        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        public string? MaxVersion { get; set; }

        public string? MaxStableVersion { get; set; }

        public string? NewestVersion { get; set; }

        /// <summary>The version crates.io shows by default (the latest non-yanked stable one, when there is one).</summary>
        public string? DefaultVersion { get; set; }

        public long Downloads { get; set; }

        public long? RecentDownloads { get; set; }

        public string? Repository { get; set; }

        public string? Homepage { get; set; }

        public string? Documentation { get; set; }

        public bool ExactMatch { get; set; }

        /// <summary>The version to offer first: the default one, else the latest stable, else the latest.</summary>
        public string? PreferredVersion => DefaultVersion ?? MaxStableVersion ?? MaxVersion ?? NewestVersion;
    }

    /// <summary>A page of search results and the total number of matches.</summary>
    public sealed class CrateSearchPage
    {
        public CrateSearchPage(IReadOnlyList<CrateSearchResult> crates, long total)
        {
            Crates = crates;
            Total = total;
        }

        public IReadOnlyList<CrateSearchResult> Crates { get; }

        public long Total { get; }
    }

    /// <summary>
    /// One published version of a crate, from its sparse-index file (<c>https://index.crates.io/...</c>):
    /// one JSON object per line, the same data cargo resolves with.
    /// </summary>
    public sealed class CrateIndexVersion
    {
        public string Name { get; set; } = string.Empty;

        public string Vers { get; set; } = string.Empty;

        public bool Yanked { get; set; }

        public string? RustVersion { get; set; }

        /// <summary>Feature name to what it enables - <c>features</c> and <c>features2</c> (the <c>dep:</c> syntax) merged.</summary>
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Features { get; set; } = new Dictionary<string, IReadOnlyList<string>>();

        /// <summary>The optional dependencies that are implicit features (no <c>dep:</c> reference to them anywhere).</summary>
        public IReadOnlyList<string> ImplicitFeatures { get; set; } = Array.Empty<string>();

        public SemanticVersion? Version => SemanticVersion.ParseOrNull(Vers);

        /// <summary>Every feature a <c>cargo add --features</c> may name, <c>default</c> excluded, sorted.</summary>
        public IReadOnlyList<string> SelectableFeatures => Features.Keys
            .Concat(ImplicitFeatures)
            .Where(f => !string.Equals(f, "default", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The crate page data (<c>GET /api/v1/crates/{name}</c>) the crate manager's details pane shows.</summary>
    public sealed class CrateDetails
    {
        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        public string? Repository { get; set; }

        public string? Homepage { get; set; }

        public string? Documentation { get; set; }

        public long Downloads { get; set; }

        public string? MaxStableVersion { get; set; }

        public string? MaxVersion { get; set; }

        /// <summary>Version number to its license expression, for the versions the response listed.</summary>
        public IReadOnlyDictionary<string, string> Licenses { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>Where a resolved version stands against the published ones: the Dependencies node's update/yanked markers.</summary>
    public sealed class CrateVersionStatus
    {
        private CrateVersionStatus(string? latestStable, string? latest, bool isYanked)
        {
            LatestStable = latestStable;
            Latest = latest;
            IsYanked = isYanked;
        }

        /// <summary>The greatest non-yanked, non-pre-release version (<see langword="null"/> if there is none).</summary>
        public string? LatestStable { get; }

        /// <summary>The greatest non-yanked version, pre-releases included.</summary>
        public string? Latest { get; }

        /// <summary>Whether <c>resolvedVersion</c> is published and yanked.</summary>
        public bool IsYanked { get; }

        public static CrateVersionStatus Compute(string? resolvedVersion, IReadOnlyList<CrateIndexVersion> versions)
        {
            if (versions is null)
            {
                throw new ArgumentNullException(nameof(versions));
            }

            var live = versions.Where(v => !v.Yanked).Select(v => v.Vers).ToList();
            var stable = SemanticVersion.Max(live, includePreRelease: false);
            var any = SemanticVersion.Max(live, includePreRelease: true);
            var resolved = SemanticVersion.ParseOrNull(resolvedVersion);
            var yanked = resolved != null && versions.Any(v => v.Yanked && resolved.Equals(v.Version));
            return new CrateVersionStatus(stable?.Original, any?.Original, yanked);
        }
    }

    /// <summary>Parsers for crates.io's two JSON shapes - pure functions, fed recorded responses by the tests.</summary>
    public static class CratesIoParser
    {
        public static CrateSearchPage ParseSearch(string json)
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var crates = new List<CrateSearchResult>();
            if (root.TryGetProperty("crates", out var array) && array.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in array.EnumerateArray())
                {
                    var crate = element.Deserialize<CrateSearchResult>(CargoJsonOptions.Default);
                    if (crate != null && !string.IsNullOrEmpty(crate.Name))
                    {
                        crates.Add(crate);
                    }
                }
            }

            long total = crates.Count;
            if (root.TryGetProperty("meta", out var meta) && meta.TryGetProperty("total", out var totalElement) && totalElement.TryGetInt64(out var t))
            {
                total = t;
            }

            return new CrateSearchPage(crates, total);
        }

        /// <summary>Parses a sparse-index file; malformed lines are skipped. Versions come back oldest first, as published.</summary>
        public static IReadOnlyList<CrateIndexVersion> ParseIndex(string text)
        {
            var result = new List<CrateIndexVersion>();
            foreach (var raw in (text ?? string.Empty).Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                try
                {
                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;
                    var version = new CrateIndexVersion
                    {
                        Name = GetString(root, "name") ?? string.Empty,
                        Vers = GetString(root, "vers") ?? string.Empty,
                        Yanked = root.TryGetProperty("yanked", out var y) && y.ValueKind == JsonValueKind.True,
                        RustVersion = GetString(root, "rust_version"),
                    };

                    var features = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
                    ReadFeatures(root, "features", features);
                    ReadFeatures(root, "features2", features);
                    version.Features = features;

                    // An optional dependency is a feature of its own unless some feature names it with "dep:".
                    var explicitDeps = new HashSet<string>(
                        features.Values.SelectMany(v => v).Where(v => v.StartsWith("dep:", StringComparison.Ordinal)).Select(v => v.Substring(4)),
                        StringComparer.Ordinal);
                    var implicitFeatures = new List<string>();
                    if (root.TryGetProperty("deps", out var deps) && deps.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var dep in deps.EnumerateArray())
                        {
                            var optional = dep.TryGetProperty("optional", out var o) && o.ValueKind == JsonValueKind.True;
                            var kind = GetString(dep, "kind");
                            var name = GetString(dep, "name");
                            if (optional && name != null && kind != "dev" && !explicitDeps.Contains(name) && !features.ContainsKey(name))
                            {
                                implicitFeatures.Add(name);
                            }
                        }
                    }

                    version.ImplicitFeatures = implicitFeatures;
                    if (version.Vers.Length > 0)
                    {
                        result.Add(version);
                    }
                }
                catch (JsonException)
                {
                    // A line this parser does not understand only loses that version.
                }
            }

            return result;
        }

        public static CrateDetails ParseCrate(string json)
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var details = new CrateDetails();
            if (root.TryGetProperty("crate", out var crate) && crate.ValueKind == JsonValueKind.Object)
            {
                details.Name = GetString(crate, "name") ?? string.Empty;
                details.Description = GetString(crate, "description");
                details.Repository = GetString(crate, "repository");
                details.Homepage = GetString(crate, "homepage");
                details.Documentation = GetString(crate, "documentation");
                details.MaxStableVersion = GetString(crate, "max_stable_version");
                details.MaxVersion = GetString(crate, "max_version");
                if (crate.TryGetProperty("downloads", out var downloads) && downloads.TryGetInt64(out var d))
                {
                    details.Downloads = d;
                }
            }

            var licenses = new Dictionary<string, string>(StringComparer.Ordinal);
            if (root.TryGetProperty("versions", out var versions) && versions.ValueKind == JsonValueKind.Array)
            {
                foreach (var version in versions.EnumerateArray())
                {
                    var num = GetString(version, "num");
                    var license = GetString(version, "license");
                    if (num != null && license != null)
                    {
                        licenses[num] = license;
                    }
                }
            }

            details.Licenses = licenses;
            return details;
        }

        /// <summary>
        /// The sparse-index path of a crate (<c>https://index.crates.io/</c> + this): <c>1/a</c>,
        /// <c>2/ab</c>, <c>3/a/abc</c>, else <c>ab/cd/abcd…</c> - lower-cased, as cargo does.
        /// </summary>
        public static string IndexPath(string crateName)
        {
            if (string.IsNullOrEmpty(crateName))
            {
                throw new ArgumentException("A crate name is required.", nameof(crateName));
            }

            var name = crateName.ToLowerInvariant();
            switch (name.Length)
            {
                case 1:
                    return "1/" + name;
                case 2:
                    return "2/" + name;
                case 3:
                    return "3/" + name.Substring(0, 1) + "/" + name;
                default:
                    return name.Substring(0, 2) + "/" + name.Substring(2, 2) + "/" + name;
            }
        }

        private static void ReadFeatures(JsonElement root, string property, Dictionary<string, IReadOnlyList<string>> into)
        {
            if (!root.TryGetProperty(property, out var features) || features.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            foreach (var feature in features.EnumerateObject())
            {
                var values = feature.Value.ValueKind == JsonValueKind.Array
                    ? feature.Value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!).ToList()
                    : new List<string>();
                into[feature.Name] = values;
            }
        }

        private static string? GetString(JsonElement element, string property) =>
            element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}
