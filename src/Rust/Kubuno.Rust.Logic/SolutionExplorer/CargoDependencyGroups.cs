using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kubuno.Rust.Cargo.Metadata;

namespace Kubuno.Rust.Logic.SolutionExplorer
{
    /// <summary>The three Cargo dependency tables, in the order Solution Explorer lists them.</summary>
    public enum CargoDependencyGroupKind
    {
        Normal,
        Dev,
        Build,
    }

    /// <summary>One group of the read-only "Dependencies" node of a <c>.rsproj</c> (docs/RSPROJ.md lot 8).</summary>
    public sealed class CargoDependencyGroup
    {
        public CargoDependencyGroup(CargoDependencyGroupKind kind, IReadOnlyList<CargoDependency> dependencies)
        {
            Kind = kind;
            Dependencies = dependencies;
        }

        public CargoDependencyGroupKind Kind { get; }

        public IReadOnlyList<CargoDependency> Dependencies { get; }

        /// <summary>The group's label, named after the Cargo.toml table it comes from.</summary>
        public string DisplayText => Kind switch
        {
            CargoDependencyGroupKind.Dev => "Dev-dependencies",
            CargoDependencyGroupKind.Build => "Build-dependencies",
            _ => "Crates",
        };
    }

    /// <summary>Groups a package's declared dependencies (from <c>cargo metadata --no-deps</c>) for display.</summary>
    public static class CargoDependencyGroups
    {
        /// <summary>
        /// The non-empty groups of <paramref name="package"/>, normal first, each sorted by name. A
        /// dependency declared twice in the same table (e.g. once per target <c>cfg</c>) is listed once.
        /// </summary>
        public static IReadOnlyList<CargoDependencyGroup> Group(CargoPackage package)
        {
            if (package is null)
            {
                throw new ArgumentNullException(nameof(package));
            }

            return package.Dependencies
                .GroupBy(KindOf)
                .OrderBy(g => g.Key)
                .Select(g => new CargoDependencyGroup(
                    g.Key,
                    g.GroupBy(d => d.Rename ?? d.Name, StringComparer.Ordinal)
                        .Select(same => same.First())
                        .OrderBy(d => d.Rename ?? d.Name, StringComparer.OrdinalIgnoreCase)
                        .ToList()))
                .ToList();
        }

        /// <summary>The label of one dependency: <c>serde (^1.0)</c>, <c>core-lib (path)</c>, <c>json (serde_json ^1)</c> for a rename.</summary>
        public static string DisplayText(CargoDependency dependency)
        {
            if (dependency is null)
            {
                throw new ArgumentNullException(nameof(dependency));
            }

            var version = dependency.Path != null && (dependency.Req == "*" || string.IsNullOrEmpty(dependency.Req))
                ? "path"
                : dependency.Req;
            var text = dependency.Rename != null
                ? $"{dependency.Rename} ({dependency.Name} {version})"
                : $"{dependency.Name} ({version})";
            return dependency.Optional ? text + ", optional" : text;
        }

        /// <summary>The package of <paramref name="metadata"/> whose manifest is <paramref name="manifestPath"/>, else the one named <paramref name="packageName"/>.</summary>
        public static CargoPackage? FindPackage(CargoMetadata metadata, string? manifestPath, string? packageName)
        {
            if (metadata is null)
            {
                throw new ArgumentNullException(nameof(metadata));
            }

            if (!string.IsNullOrEmpty(packageName))
            {
                var named = metadata.Packages.FirstOrDefault(p => string.Equals(p.Name, packageName, StringComparison.Ordinal));
                if (named != null)
                {
                    return named;
                }
            }

            if (!string.IsNullOrEmpty(manifestPath))
            {
                var full = Normalize(manifestPath!);
                var byManifest = metadata.Packages.FirstOrDefault(p => string.Equals(Normalize(p.ManifestPath), full, StringComparison.OrdinalIgnoreCase));
                if (byManifest != null)
                {
                    return byManifest;
                }
            }

            return metadata.Packages.Count == 1 ? metadata.Packages[0] : null;
        }

        private static CargoDependencyGroupKind KindOf(CargoDependency dependency) => dependency.Kind switch
        {
            "dev" => CargoDependencyGroupKind.Dev,
            "build" => CargoDependencyGroupKind.Build,
            _ => CargoDependencyGroupKind.Normal,
        };

        private static string Normalize(string path)
        {
            try
            {
                return Path.GetFullPath(path).TrimEnd('\\', '/');
            }
            catch (Exception)
            {
                return path;
            }
        }
    }
}
