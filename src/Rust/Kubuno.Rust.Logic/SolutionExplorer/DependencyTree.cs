using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kubuno.Cargo.Metadata;
using Kubuno.Cargo.Registry;
using Kubuno.Cargo.Toolchain;

namespace Kubuno.VisualStudio.Core.SolutionExplorer
{
    /// <summary>
    /// The category nodes under "Dependencies" - the Cargo counterparts of the .NET project system's
    /// groups: procedural macros ~ Analyzers, the Rust toolchain ~ Frameworks, registry crates ~
    /// Packages, path dependencies ~ Projects, plus git dependencies (no .NET equivalent).
    /// </summary>
    public enum DependencyCategory
    {
        ProcMacros,
        Toolchain,
        Crates,
        Projects,
        Git,
    }

    /// <summary>The Cargo.toml tables a dependency is declared in.</summary>
    [Flags]
    public enum DependencyKinds
    {
        None = 0,
        Normal = 1,
        Dev = 2,
        Build = 4,
    }

    public enum DependencyState
    {
        /// <summary>Found in cargo's resolve graph.</summary>
        Resolved,

        /// <summary>The resolve graph is still being computed (only the declaration is known).</summary>
        Pending,

        /// <summary>Declared, not optional, but not in the graph (or the graph could not be computed): the yellow triangle.</summary>
        Unresolved,

        /// <summary>An optional dependency no active feature enables: shown dimmed, not as an error.</summary>
        Inactive,
    }

    public enum DependencyItemKind
    {
        /// <summary>A crate: declared by Cargo.toml, or (<see cref="DependencyItem.IsTransitive"/>) pulled in by one.</summary>
        Crate,

        /// <summary>The Rust toolchain node under "Toolchain".</summary>
        Toolchain,

        /// <summary>A crate of the toolchain's sysroot (<c>std</c>, <c>core</c>...), under the toolchain node.</summary>
        SysrootCrate,
    }

    /// <summary>One node under a category (or under another dependency, for transitive ones).</summary>
    public sealed class DependencyItem
    {
        public DependencyItem(string name, DependencyItemKind itemKind, DependencyCategory category)
        {
            Name = name;
            ItemKind = itemKind;
            Category = category;
        }

        /// <summary>The package's real name (for the toolchain node: <c>Rust</c>).</summary>
        public string Name { get; }

        public DependencyItemKind ItemKind { get; }

        public DependencyCategory Category { get; }

        /// <summary>The name the package is imported under, when renamed (<c>json = { package = "serde_json" }</c>).</summary>
        public string? Rename { get; set; }

        public string DisplayName => Rename ?? Name;

        public DependencyKinds Kinds { get; set; }

        /// <summary>A dependency of a dependency (from the resolve graph), not declared by this Cargo.toml.</summary>
        public bool IsTransitive { get; set; }

        /// <summary>Every declaration of this dependency in Cargo.toml (per table and per target <c>cfg</c>).</summary>
        public IReadOnlyList<CargoDependency> Declarations { get; set; } = Array.Empty<CargoDependency>();

        /// <summary>The package it resolved to, if any.</summary>
        public CargoPackage? Package { get; set; }

        /// <summary>The features cargo activated for the package in this resolution.</summary>
        public IReadOnlyList<string> ActivatedFeatures { get; set; } = Array.Empty<string>();

        public DependencyState State { get; set; }

        /// <summary>What is wrong, for <see cref="DependencyState.Unresolved"/> (also the tooltip).</summary>
        public string? Problem { get; set; }

        /// <summary>For <see cref="DependencyItemKind.Toolchain"/> and <see cref="DependencyItemKind.SysrootCrate"/>.</summary>
        public RustcVersionInfo? Toolchain { get; set; }

        /// <summary>For <see cref="DependencyItemKind.SysrootCrate"/>: its source folder, when <c>rust-src</c> is installed.</summary>
        public string? SysrootPath { get; set; }

        /// <summary>The latest stable version on crates.io, once known (registry crates only).</summary>
        public string? LatestVersion { get; set; }

        /// <summary>Whether the resolved version has been yanked from crates.io.</summary>
        public bool IsYanked { get; set; }

        public bool IsOptional => Declarations.Count > 0 && Declarations.All(d => d.Optional);

        public CargoDependency? PrimaryDeclaration => Declarations.FirstOrDefault(d => d.Kind is null) ?? Declarations.FirstOrDefault();

        /// <summary>The version requirement of Cargo.toml (<c>^1.0</c>), <see langword="null"/> for a bare path/git dependency.</summary>
        public string? RequestedVersion
        {
            get
            {
                var req = PrimaryDeclaration?.Req;
                return string.IsNullOrEmpty(req) || req == "*" ? null : req;
            }
        }

        public string? ResolvedVersion => ItemKind == DependencyItemKind.Crate ? Package?.Version : Toolchain?.Release;

        /// <summary>The <c>cfg(...)</c>/triple of target-specific declarations, if every declaration has one.</summary>
        public string? Target
        {
            get
            {
                var targets = Declarations.Select(d => d.Target).Distinct().ToList();
                return targets.Count > 0 && targets.All(t => t != null) ? string.Join(", ", targets) : null;
            }
        }

        /// <summary>The package's source (resolved, else declared): <c>registry+</c>/<c>sparse+</c>, <c>git+</c>, or <see langword="null"/> for a path.</summary>
        public string? Source => ItemKind != DependencyItemKind.Crate ? null : Package != null ? Package.Source : PrimaryDeclaration?.Source;

        public bool IsRegistry => Source != null
            && (Source.StartsWith("registry+", StringComparison.Ordinal) || Source.StartsWith("sparse+", StringComparison.Ordinal));

        public bool IsGit => Source != null && Source.StartsWith("git+", StringComparison.Ordinal);

        public bool IsCratesIo => IsRegistry
            && (Source!.IndexOf("github.com/rust-lang/crates.io-index", StringComparison.Ordinal) >= 0
                || Source.IndexOf("index.crates.io", StringComparison.Ordinal) >= 0);

        /// <summary>The folder holding the package: its manifest's folder once resolved, else the declared path.</summary>
        public string? Directory
        {
            get
            {
                if (ItemKind == DependencyItemKind.SysrootCrate)
                {
                    return SysrootPath;
                }

                if (ItemKind == DependencyItemKind.Toolchain)
                {
                    return Toolchain?.Sysroot;
                }

                if (!string.IsNullOrEmpty(Package?.ManifestPath))
                {
                    return Path.GetDirectoryName(Package!.ManifestPath);
                }

                return PrimaryDeclaration?.Path;
            }
        }

        /// <summary>The crate's library root (<c>src/lib.rs</c>), the file "Open source code" opens.</summary>
        public string? LibrarySourcePath
        {
            get
            {
                if (ItemKind == DependencyItemKind.SysrootCrate)
                {
                    return SysrootPath is null ? null : Path.Combine(SysrootPath, "src", "lib.rs");
                }

                var lib = Package?.Targets.FirstOrDefault(t => t.IsKind(CargoTargetKind.Lib) || t.IsKind(CargoTargetKind.ProcMacro) || t.IsKind(CargoTargetKind.Rlib) || t.IsKind(CargoTargetKind.Dylib))
                    ?? Package?.Targets.FirstOrDefault();
                return string.IsNullOrEmpty(lib?.SrcPath) ? null : lib!.SrcPath;
            }
        }

        public bool IsOutdated
        {
            get
            {
                var latest = SemanticVersion.ParseOrNull(LatestVersion);
                var resolved = SemanticVersion.ParseOrNull(ResolvedVersion);
                return latest != null && resolved != null && latest.CompareTo(resolved) > 0;
            }
        }

        /// <summary>Solution Explorer's label, shaped like .NET's <c>Newtonsoft.Json (13.0.3)</c>.</summary>
        public string Text
        {
            get
            {
                switch (ItemKind)
                {
                    case DependencyItemKind.Toolchain:
                        return Toolchain is null
                            ? "Rust"
                            : $"Rust {Toolchain.Version} ({Toolchain.Channel}, {Toolchain.Host})";
                    case DependencyItemKind.SysrootCrate:
                        return Name;
                }

                string? version = ResolvedVersion ?? RequestedVersion;
                string text;
                if (Category == DependencyCategory.Projects && !IsTransitive)
                {
                    // Like a .NET project reference: the project's name only.
                    text = Rename != null ? $"{Rename} ({Name})" : Name;
                }
                else if (Rename != null)
                {
                    text = version is null ? $"{Rename} ({Name})" : $"{Rename} ({Name} {version})";
                }
                else
                {
                    text = version is null ? Name : $"{Name} ({version})";
                }

                var badges = new List<string>();
                if (!IsTransitive && (Kinds & DependencyKinds.Normal) == 0)
                {
                    if ((Kinds & DependencyKinds.Dev) != 0)
                    {
                        badges.Add(DependenciesText.DevBadge);
                    }

                    if ((Kinds & DependencyKinds.Build) != 0)
                    {
                        badges.Add(DependenciesText.BuildBadge);
                    }
                }

                return badges.Count == 0 ? text : $"{text} [{string.Join(", ", badges)}]";
            }
        }

        public string ToolTip
        {
            get
            {
                switch (State)
                {
                    case DependencyState.Unresolved:
                        return Problem ?? DependenciesText.UnresolvedToolTip;
                    case DependencyState.Inactive:
                        return Problem ?? DependenciesText.InactiveToolTip;
                }

                if (IsYanked && ResolvedVersion != null)
                {
                    return DependenciesText.YankedToolTip(ResolvedVersion);
                }

                if (IsOutdated)
                {
                    return DependenciesText.UpdateAvailableToolTip(LatestVersion!);
                }

                return Package?.Description ?? Directory ?? Text;
            }
        }

        /// <summary>Records the crates.io status of a registry crate (latest stable version, yanked flag).</summary>
        public void ApplyRegistryStatus(CrateVersionStatus status)
        {
            if (status is null)
            {
                throw new ArgumentNullException(nameof(status));
            }

            LatestVersion = status.LatestStable ?? status.Latest;
            IsYanked = status.IsYanked;
        }

        /// <summary>A key identifying "the same node" across refreshes, so expanded nodes stay expanded.</summary>
        public string MergeKey => $"{ItemKind}|{Category}|{DisplayName}|{Name}|{IsTransitive}";

        public override string ToString() => Text;
    }

    /// <summary>One category node and its items.</summary>
    public sealed class DependencyGroup
    {
        public DependencyGroup(DependencyCategory category, IReadOnlyList<DependencyItem> items)
        {
            Category = category;
            Items = items;
        }

        public DependencyCategory Category { get; }

        public IReadOnlyList<DependencyItem> Items { get; }

        public string Text => DependencyTreeBuilder.CategoryText(Category);

        public bool HasProblem => Items.Any(i => i.State == DependencyState.Unresolved || i.IsYanked);
    }

    /// <summary>The whole "Dependencies" subtree of one package, plus what is needed to expand transitive nodes lazily.</summary>
    public sealed class DependencyTreeModel
    {
        private readonly CargoMetadata? _resolved;
        private readonly Dictionary<string, CargoPackage> _packagesById;

        public DependencyTreeModel(IReadOnlyList<DependencyGroup> groups, IReadOnlyList<string> diagnostics, CargoMetadata? resolved)
        {
            Groups = groups;
            Diagnostics = diagnostics;
            _resolved = resolved;
            _packagesById = resolved?.Packages.GroupBy(p => p.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal)
                ?? new Dictionary<string, CargoPackage>(StringComparer.Ordinal);
        }

        public static DependencyTreeModel Empty { get; } = new DependencyTreeModel(Array.Empty<DependencyGroup>(), Array.Empty<string>(), null);

        /// <summary>The same tree with other diagnostics (a failed refresh keeps showing the last good tree, plus the error).</summary>
        public DependencyTreeModel WithDiagnostics(IReadOnlyList<string> diagnostics) => new DependencyTreeModel(Groups, diagnostics, _resolved);

        public IReadOnlyList<DependencyGroup> Groups { get; }

        /// <summary>Errors to show as child nodes of "Dependencies" (like .NET's diagnostic nodes).</summary>
        public IReadOnlyList<string> Diagnostics { get; }

        /// <summary>Whether the model came from a resolve graph (else only declarations are known).</summary>
        public bool IsResolved => _resolved?.Resolve != null;

        public bool HasProblem => Diagnostics.Count > 0 || Groups.Any(g => g.HasProblem);

        public IEnumerable<DependencyItem> AllItems => Groups.SelectMany(g => g.Items);

        /// <summary>Whether <paramref name="item"/> has children (transitive crates, or the toolchain's sysroot crates).</summary>
        public bool HasChildren(DependencyItem item)
        {
            if (item.ItemKind == DependencyItemKind.Toolchain)
            {
                return true;
            }

            if (item.ItemKind != DependencyItemKind.Crate || item.Package is null)
            {
                return false;
            }

            var node = _resolved?.Resolve?.Find(item.Package.Id);
            return node != null && node.Deps.Any(IsTransitiveEdge);
        }

        /// <summary>
        /// The children of <paramref name="item"/>: the normal and build dependencies the resolve graph
        /// gives its package (dev-dependencies of a dependency are never built), or the sysroot crates of
        /// the toolchain node.
        /// </summary>
        public IReadOnlyList<DependencyItem> ChildrenOf(DependencyItem item)
        {
            if (item.ItemKind == DependencyItemKind.Toolchain)
            {
                return DependencyTreeBuilder.SysrootCrates(item.Toolchain);
            }

            if (item.ItemKind != DependencyItemKind.Crate || item.Package is null)
            {
                return Array.Empty<DependencyItem>();
            }

            var node = _resolved?.Resolve?.Find(item.Package.Id);
            if (node is null)
            {
                return Array.Empty<DependencyItem>();
            }

            var children = new List<DependencyItem>();
            foreach (var edge in node.Deps.Where(IsTransitiveEdge))
            {
                if (!_packagesById.TryGetValue(edge.Pkg, out var package))
                {
                    continue;
                }

                var childNode = _resolved!.Resolve!.Find(package.Id);
                var kinds = DependencyKinds.None;
                foreach (var kind in edge.DepKinds)
                {
                    kinds |= DependencyTreeBuilder.KindOf(kind.Kind);
                }

                children.Add(new DependencyItem(package.Name, DependencyItemKind.Crate, DependencyTreeBuilder.CategoryOf(package, null))
                {
                    IsTransitive = true,
                    Package = package,
                    Kinds = kinds == DependencyKinds.None ? DependencyKinds.Normal : kinds,
                    ActivatedFeatures = childNode?.Features ?? Array.Empty<string>(),
                    State = DependencyState.Resolved,
                });
            }

            return children
                .GroupBy(c => c.Package!.Id, StringComparer.Ordinal)
                .Select(g => g.First())
                .OrderBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool IsTransitiveEdge(CargoResolveDep edge) => edge.DepKinds.Count == 0 || edge.DepKinds.Any(k => k.Kind != "dev");
    }

    /// <summary>
    /// Maps <c>cargo metadata</c> to the Dependencies tree: the package's declarations (always
    /// available, from <c>--no-deps</c>) matched against the resolve graph (when it could be computed),
    /// then split into categories like the .NET project system's groups.
    /// </summary>
    public static class DependencyTreeBuilder
    {
        /// <summary>The crates listed under the toolchain node, in this order.</summary>
        public static readonly IReadOnlyList<string> SysrootCrateNames = new[] { "std", "core", "alloc", "proc_macro", "test" };

        private static readonly DependencyCategory[] CategoryOrder =
        {
            DependencyCategory.ProcMacros,
            DependencyCategory.Toolchain,
            DependencyCategory.Crates,
            DependencyCategory.Projects,
            DependencyCategory.Git,
        };

        public static string CategoryText(DependencyCategory category) => category switch
        {
            DependencyCategory.ProcMacros => DependenciesText.ProcMacros,
            DependencyCategory.Toolchain => DependenciesText.Toolchain,
            DependencyCategory.Projects => DependenciesText.Projects,
            DependencyCategory.Git => DependenciesText.Git,
            _ => DependenciesText.Crates,
        };

        /// <param name="package">The package whose dependencies are shown (from either metadata).</param>
        /// <param name="resolved">Full <c>cargo metadata</c> output (with <c>resolve</c>), or <see langword="null"/> when it is not (yet) available.</param>
        /// <param name="toolchain">The toolchain, or <see langword="null"/> when <c>rustc -vV</c> could not run.</param>
        /// <param name="resolveError">Why the resolve graph is missing, or <see langword="null"/> while it is still being computed.</param>
        public static DependencyTreeModel Build(CargoPackage package, CargoMetadata? resolved, RustcVersionInfo? toolchain, string? resolveError)
        {
            if (package is null)
            {
                throw new ArgumentNullException(nameof(package));
            }

            var diagnostics = new List<string>();
            if (resolveError != null)
            {
                diagnostics.Add(DependenciesText.MetadataFailed(resolveError));
            }

            var graph = resolved?.Resolve;
            var resolvedPackage = resolved != null ? CargoDependencyGroups.FindPackage(resolved, package.ManifestPath, package.Name) : null;
            var rootNode = resolvedPackage != null ? graph?.Find(resolvedPackage.Id) : null;
            var packagesById = resolved?.Packages.GroupBy(p => p.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal)
                ?? new Dictionary<string, CargoPackage>(StringComparer.Ordinal);

            // The declarations come from the resolved metadata when present (same data, one source).
            var declarations = (resolvedPackage ?? package).Dependencies;
            var items = new List<DependencyItem>();
            foreach (var same in declarations.GroupBy(d => (d.LocalName, d.Name)))
            {
                var decls = same.ToList();
                var first = decls[0];
                var kinds = DependencyKinds.None;
                foreach (var decl in decls)
                {
                    kinds |= KindOf(decl.Kind);
                }

                CargoPackage? target = null;
                IReadOnlyList<string> features = Array.Empty<string>();
                if (rootNode != null)
                {
                    var externName = first.LocalName.Replace('-', '_');
                    var edges = rootNode.Deps
                        .Where(e => packagesById.TryGetValue(e.Pkg, out var p) && string.Equals(p.Name, first.Name, StringComparison.Ordinal))
                        .ToList();
                    var edge = edges.FirstOrDefault(e => string.Equals(e.Name, externName, StringComparison.Ordinal)) ?? edges.FirstOrDefault();
                    if (edge != null)
                    {
                        target = packagesById[edge.Pkg];
                        features = graph!.Find(edge.Pkg)?.Features ?? Array.Empty<string>();
                    }
                }

                var item = new DependencyItem(first.Name, DependencyItemKind.Crate, target != null ? CategoryOf(target, first) : CategoryOf(null, first))
                {
                    Rename = first.Rename,
                    Kinds = kinds,
                    Declarations = decls,
                    Package = target,
                    ActivatedFeatures = features,
                };

                if (target != null)
                {
                    item.State = DependencyState.Resolved;
                }
                else if (first.Path != null && !System.IO.Directory.Exists(first.Path))
                {
                    item.State = DependencyState.Unresolved;
                    item.Problem = DependenciesText.MissingPathToolTip(first.Path);
                }
                else if (rootNode != null && decls.All(d => d.Optional))
                {
                    item.State = DependencyState.Inactive;
                }
                else if (rootNode != null && decls.All(d => d.Target != null))
                {
                    // Filtered out by --filter-platform: only built for another platform.
                    item.State = DependencyState.Inactive;
                    item.Problem = DependenciesText.OtherPlatformToolTip(string.Join(", ", decls.Select(d => d.Target).Distinct()));
                }
                else if (rootNode != null || resolveError != null)
                {
                    item.State = DependencyState.Unresolved;
                }
                else
                {
                    item.State = DependencyState.Pending;
                }

                items.Add(item);
            }

            // Like the analyzers a .NET project gets from its packages: every procedural macro the build
            // uses, including those a dependency brings in (serde's "derive" feature -> serde_derive).
            if (rootNode != null)
            {
                var direct = new HashSet<string>(items.Where(i => i.Package != null).Select(i => i.Package!.Id), StringComparer.Ordinal);
                var visited = new HashSet<string>(StringComparer.Ordinal);
                var queue = new Queue<string>(rootNode.Deps.Select(d => d.Pkg));
                while (queue.Count > 0)
                {
                    var id = queue.Dequeue();
                    if (!visited.Add(id) || !packagesById.TryGetValue(id, out var package2))
                    {
                        continue;
                    }

                    var node = graph!.Find(id);
                    if (package2.IsProcMacro && !direct.Contains(id))
                    {
                        items.Add(new DependencyItem(package2.Name, DependencyItemKind.Crate, DependencyCategory.ProcMacros)
                        {
                            IsTransitive = true,
                            Package = package2,
                            Kinds = DependencyKinds.Normal,
                            ActivatedFeatures = node?.Features ?? Array.Empty<string>(),
                            State = DependencyState.Resolved,
                        });
                    }

                    foreach (var edge in node?.Deps ?? Array.Empty<CargoResolveDep>())
                    {
                        if (edge.DepKinds.Count == 0 || edge.DepKinds.Any(k => k.Kind != "dev"))
                        {
                            queue.Enqueue(edge.Pkg);
                        }
                    }
                }
            }

            var groups = new List<DependencyGroup>();
            foreach (var category in CategoryOrder)
            {
                if (category == DependencyCategory.Toolchain)
                {
                    if (toolchain != null)
                    {
                        groups.Add(new DependencyGroup(category, new[]
                        {
                            new DependencyItem("Rust", DependencyItemKind.Toolchain, DependencyCategory.Toolchain)
                            {
                                Toolchain = toolchain,
                                State = DependencyState.Resolved,
                                Kinds = DependencyKinds.Normal,
                            },
                        }));
                    }

                    continue;
                }

                var inCategory = items
                    .Where(i => i.Category == category)
                    .OrderBy(i => i.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (inCategory.Count > 0)
                {
                    groups.Add(new DependencyGroup(category, inCategory));
                }
            }

            return new DependencyTreeModel(groups, diagnostics, graph != null ? resolved : null);
        }

        /// <summary>The sysroot crates under the toolchain node, with their source folder when <c>rust-src</c> is installed.</summary>
        public static IReadOnlyList<DependencyItem> SysrootCrates(RustcVersionInfo? toolchain)
        {
            var library = toolchain?.Sysroot is null ? null : Path.Combine(toolchain.Sysroot, "lib", "rustlib", "src", "rust", "library");
            return SysrootCrateNames
                .Select(name =>
                {
                    var folder = library is null ? null : Path.Combine(library, name);
                    return new DependencyItem(name, DependencyItemKind.SysrootCrate, DependencyCategory.Toolchain)
                    {
                        Toolchain = toolchain,
                        SysrootPath = folder != null && System.IO.Directory.Exists(folder) ? folder : null,
                        State = DependencyState.Resolved,
                        Kinds = DependencyKinds.Normal,
                        IsTransitive = true,
                    };
                })
                .ToList();
        }

        public static DependencyKinds KindOf(string? kind) => kind switch
        {
            "dev" => DependencyKinds.Dev,
            "build" => DependencyKinds.Build,
            _ => DependencyKinds.Normal,
        };

        /// <summary>Proc-macros first (whatever their source), then by source: path, git, registry.</summary>
        public static DependencyCategory CategoryOf(CargoPackage? package, CargoDependency? declaration)
        {
            if (package != null)
            {
                if (package.IsProcMacro)
                {
                    return DependencyCategory.ProcMacros;
                }

                if (package.Source is null)
                {
                    return DependencyCategory.Projects;
                }

                return package.Source.StartsWith("git+", StringComparison.Ordinal) ? DependencyCategory.Git : DependencyCategory.Crates;
            }

            if (declaration?.Path != null)
            {
                return DependencyCategory.Projects;
            }

            return declaration?.Source != null && declaration.Source.StartsWith("git+", StringComparison.Ordinal)
                ? DependencyCategory.Git
                : DependencyCategory.Crates;
        }
    }
}
