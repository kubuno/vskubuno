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

        /// <summary>
        /// The package's <c>[package] default-run</c> - the <c>[[bin]]</c> name <c>cargo run</c>
        /// uses when more than one exists and none is given with <c>--bin</c>. <see
        /// langword="null"/> when unset (most packages), in which case cargo falls back to "the
        /// only bin", then "the bin named after the package", then "the first bin" - see
        /// <c>Kubuno.VisualStudio.Debugging.StartupItemSelector</c>, which mirrors that exact
        /// fallback chain to auto-pick Open Folder's "Select Startup Item".
        /// </summary>
        public string? DefaultRun { get; set; }

        public IReadOnlyList<CargoTarget> Targets { get; set; } = Array.Empty<CargoTarget>();

        /// <summary>The dependencies the package's own Cargo.toml declares (normal, dev and build).</summary>
        public IReadOnlyList<CargoDependency> Dependencies { get; set; } = Array.Empty<CargoDependency>();

        /// <summary>Feature name to the list of features/deps it enables.</summary>
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Features { get; set; }
            = new Dictionary<string, IReadOnlyList<string>>();

        public override string ToString() => $"{Name} {Version}";
    }
}
