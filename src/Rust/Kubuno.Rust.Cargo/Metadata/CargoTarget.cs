using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Kubuno.Rust.Cargo.Metadata
{
    /// <summary>
    /// A single build target of a package: a lib, a bin, an example, a test or a bench.
    /// Shared shape between `cargo metadata`'s <c>targets</c> array and the <c>target</c>
    /// field of build-message events (compiler-message / compiler-artifact).
    /// </summary>
    public sealed class CargoTarget
    {
        /// <summary>Target name, e.g. the crate name for a lib, or the binary name for a bin.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// One or more of: "lib", "bin", "example", "test", "bench", "custom-build",
        /// "proc-macro", "cdylib", "dylib", "staticlib", "rlib". See <see cref="CargoTargetKind"/>
        /// for the well-known constants; unrecognized kinds still round-trip here as raw strings.
        /// </summary>
        public IReadOnlyList<string> Kind { get; set; } = Array.Empty<string>();

        public IReadOnlyList<string> CrateTypes { get; set; } = Array.Empty<string>();

        /// <summary>Absolute path to the target's entry source file, as reported by Cargo.</summary>
        public string SrcPath { get; set; } = string.Empty;

        public string? Edition { get; set; }

        /// <summary>
        /// Features that must be enabled for this target to be built (e.g. a `[[bin]]` with
        /// `required-features = ["foo"]`). Empty when the target has no such requirement.
        /// Kebab-case in Cargo's JSON, unlike every other field here, hence the explicit name.
        /// </summary>
        [JsonPropertyName("required-features")]
        public IReadOnlyList<string> RequiredFeatures { get; set; } = Array.Empty<string>();

        public bool Test { get; set; }

        public bool Doctest { get; set; }

        public bool Doc { get; set; }

        public bool IsKind(string kind) => Kind.Contains(kind);

        public override string ToString() => $"{Name} ({string.Join("+", Kind)})";
    }
}
