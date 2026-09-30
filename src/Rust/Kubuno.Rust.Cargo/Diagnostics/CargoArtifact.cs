using System;
using System.Collections.Generic;
using Kubuno.Rust.Cargo.Metadata;

namespace Kubuno.Rust.Cargo.Diagnostics
{
    /// <summary>A "compiler-artifact" build event: one target finished compiling (or was already up to date).</summary>
    public sealed class CargoArtifact
    {
        public string PackageId { get; set; } = string.Empty;

        public CargoTarget Target { get; set; } = new CargoTarget();

        /// <summary>Absolute paths of every file produced for this target (rlib, exe, pdb, ...).</summary>
        public IReadOnlyList<string> Filenames { get; set; } = Array.Empty<string>();

        /// <summary>Absolute path of the produced executable, or null for non-executable targets (e.g. a lib).</summary>
        public string? Executable { get; set; }

        /// <summary>True when Cargo reused a cached build instead of recompiling.</summary>
        public bool Fresh { get; set; }

        public override string ToString() => $"{Target.Name} (fresh={Fresh})";
    }
}
