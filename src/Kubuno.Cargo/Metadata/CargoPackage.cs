using System;
using System.Collections.Generic;

namespace Kubuno.Cargo.Metadata
{
    /// <summary>One package (crate) as reported by `cargo metadata`.</summary>
    public sealed class CargoPackage
    {
        /// <summary>Cargo's opaque package id, e.g. "path+file:///…/app#0.2.0".</summary>
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Version { get; set; } = string.Empty;

        /// <summary>Absolute path to this package's Cargo.toml.</summary>
        public string ManifestPath { get; set; } = string.Empty;

        public IReadOnlyList<CargoTarget> Targets { get; set; } = Array.Empty<CargoTarget>();

        /// <summary>Feature name to the list of features/deps it enables.</summary>
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Features { get; set; }
            = new Dictionary<string, IReadOnlyList<string>>();

        public override string ToString() => $"{Name} {Version}";
    }
}
