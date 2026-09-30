using System;
using System.Collections.Generic;

namespace Kubuno.Rust.Cargo.Metadata
{
    /// <summary>
    /// Deserialized output of `cargo metadata --format-version 1 --no-deps`. Only the fields
    /// the extension needs are modeled; unknown JSON members are ignored (System.Text.Json's
    /// default behavior) so newer Cargo versions never break parsing.
    /// </summary>
    public sealed class CargoMetadata
    {
        public int Version { get; set; }

        /// <summary>Absolute path to the workspace root (the directory containing the root Cargo.toml).</summary>
        public string WorkspaceRoot { get; set; } = string.Empty;

        /// <summary>
        /// Absolute path to the build output directory. Reflects Cargo's own resolution of
        /// <c>CARGO_TARGET_DIR</c> (env var), <c>build.target-dir</c> (config) or the
        /// workspace-relative default ("target") — nothing needs to be re-derived here.
        /// </summary>
        public string TargetDirectory { get; set; } = string.Empty;

        public IReadOnlyList<CargoPackage> Packages { get; set; } = Array.Empty<CargoPackage>();

        /// <summary>Package ids that are members of the workspace (as opposed to external dependencies).</summary>
        public IReadOnlyList<string> WorkspaceMembers { get; set; } = Array.Empty<string>();

        public IReadOnlyList<string> WorkspaceDefaultMembers { get; set; } = Array.Empty<string>();

        /// <summary>The resolved dependency graph; <see langword="null"/> for <c>--no-deps</c> output.</summary>
        public CargoResolve? Resolve { get; set; }
    }
}
