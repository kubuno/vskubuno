using System;
using System.Collections.Generic;
using System.Linq;

namespace Kubuno.Rust.Cargo.Metadata
{
    /// <summary>
    /// The <c>resolve</c> object of <c>cargo metadata</c> (absent with <c>--no-deps</c>): the resolved
    /// dependency graph, one node per package actually in the build.
    /// </summary>
    public sealed class CargoResolve
    {
        public IReadOnlyList<CargoResolveNode> Nodes { get; set; } = Array.Empty<CargoResolveNode>();

        /// <summary>The package id <c>cargo metadata</c> was run for, or <see langword="null"/> for a virtual workspace.</summary>
        public string? Root { get; set; }

        /// <summary>The node of <paramref name="packageId"/>, if it is in the graph.</summary>
        public CargoResolveNode? Find(string packageId) => Nodes.FirstOrDefault(n => string.Equals(n.Id, packageId, StringComparison.Ordinal));
    }

    /// <summary>One package of the resolved graph and its direct, resolved dependencies.</summary>
    public sealed class CargoResolveNode
    {
        public string Id { get; set; } = string.Empty;

        public IReadOnlyList<CargoResolveDep> Deps { get; set; } = Array.Empty<CargoResolveDep>();

        /// <summary>The features activated for this package in this resolution.</summary>
        public IReadOnlyList<string> Features { get; set; } = Array.Empty<string>();
    }

    /// <summary>One resolved edge: the extern crate name the dependent sees and the package it resolves to.</summary>
    public sealed class CargoResolveDep
    {
        /// <summary>The extern crate name (the rename if any, with <c>-</c> turned into <c>_</c>).</summary>
        public string Name { get; set; } = string.Empty;

        public string Pkg { get; set; } = string.Empty;

        public IReadOnlyList<CargoDepKindInfo> DepKinds { get; set; } = Array.Empty<CargoDepKindInfo>();

        /// <summary>Whether one of the edge's kinds is <paramref name="kind"/> (<see langword="null"/> = normal).</summary>
        public bool HasKind(string? kind) => DepKinds.Count == 0
            ? kind is null
            : DepKinds.Any(k => string.Equals(k.Kind, kind, StringComparison.Ordinal));
    }

    /// <summary>One <c>dep_kinds</c> entry: <c>kind</c> is <see langword="null"/> (normal), <c>"dev"</c> or <c>"build"</c>.</summary>
    public sealed class CargoDepKindInfo
    {
        public string? Kind { get; set; }

        public string? Target { get; set; }
    }
}
