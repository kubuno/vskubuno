using System;

namespace Kubuno.Rust.TestAdapter.Discovery
{
    /// <summary>Which kind of Cargo target a built test binary came from.</summary>
    public enum CargoTestBinaryKind
    {
        /// <summary>The crate's own `#[cfg(test)]` unit tests (target.kind = "lib").</summary>
        Lib,

        /// <summary>A `[[bin]]` target's own `#[cfg(test)]` unit tests (target.kind = "bin").</summary>
        Bin,

        /// <summary>A `tests/*.rs` integration test file (target.kind = "test").</summary>
        Integration,

        /// <summary>A `benches/*.rs` benchmark harness built under `cargo test` (target.kind = "bench").</summary>
        Bench,
    }

    /// <summary>
    /// One built test executable, as reported by `cargo test --no-run --message-format=json`
    /// (see <see cref="CargoTestBuild"/>) - not yet the individual tests inside it, which come
    /// from `&lt;ExecutablePath&gt; --list --format terse` (see <see cref="LibtestListParser"/>).
    /// </summary>
    public sealed class CargoTestBinary
    {
        public CargoTestBinary(
            string targetName,
            CargoTestBinaryKind kind,
            string sourcePath,
            string executablePath,
            string packageManifestPath,
            string packageRoot,
            string targetDirectory)
        {
            TargetName = targetName ?? throw new ArgumentNullException(nameof(targetName));
            Kind = kind;
            SourcePath = sourcePath ?? throw new ArgumentNullException(nameof(sourcePath));
            ExecutablePath = executablePath ?? throw new ArgumentNullException(nameof(executablePath));
            PackageManifestPath = packageManifestPath ?? throw new ArgumentNullException(nameof(packageManifestPath));
            PackageRoot = packageRoot ?? throw new ArgumentNullException(nameof(packageRoot));
            TargetDirectory = targetDirectory ?? throw new ArgumentNullException(nameof(targetDirectory));
        }

        /// <summary>
        /// The Cargo target name: the crate name for a lib (Rust-identifier form, e.g.
        /// "hello_rust"), the `[[bin]]` name as written in Cargo.toml (e.g. "hello-rust", kebab
        /// allowed), or the integration test file's stem (e.g. "basic").
        /// </summary>
        public string TargetName { get; }

        public CargoTestBinaryKind Kind { get; }

        /// <summary>Absolute path to the target's entry source file (lib.rs / main.rs / tests/basic.rs / ...), used for the "cheap" `fn name` lookup (see <see cref="TestSourceLocator"/>).</summary>
        public string SourcePath { get; }

        /// <summary>Absolute path to the built, hash-suffixed test binary (`cargo`'s own `executable` field - authoritative, see Kubuno.Rust.Launch's `ExecutableResolver` doc comment).</summary>
        public string ExecutablePath { get; }

        /// <summary>Absolute path to the owning package's Cargo.toml - this is the discovery/execution "container" (see Containers/).</summary>
        public string PackageManifestPath { get; }

        /// <summary>Directory containing <see cref="PackageManifestPath"/> - the default working/debuggee directory.</summary>
        public string PackageRoot { get; }

        /// <summary>
        /// The resolved Cargo target directory (`&lt;target-dir&gt;`, i.e. what `CARGO_TARGET_DIR`
        /// points to), derived from <see cref="ExecutablePath"/> - see the remarks on
        /// <see cref="CargoTestBuild"/> for exactly how and its one documented limitation
        /// (host-triple builds only; a cross-compiled `--target` isn't accounted for).
        /// </summary>
        public string TargetDirectory { get; }
    }
}
