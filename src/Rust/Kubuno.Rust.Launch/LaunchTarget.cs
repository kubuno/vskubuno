namespace Kubuno.Rust.Launch
{
    /// <summary>
    /// The minimal description of a Cargo target this library needs to compute an
    /// executable path, an environment, and a launch description for it.
    ///
    /// This is intentionally a self-contained, dependency-free type rather than something
    /// shared with Kubuno.Rust.Cargo (owned by another agent, built in parallel): the cargo
    /// metadata/diagnostics crate can map its richer target model onto this one once it
    /// exists, but Kubuno.Rust.Launch must not take a project or assembly dependency on it.
    /// </summary>
    /// <param name="Package">The owning package name, as reported by `cargo metadata` (e.g. "kubuno-desktop-shell").</param>
    /// <param name="Name">The target name (binary/example/test name), without extension.</param>
    /// <param name="Kind">Whether this is a bin, example, or test target.</param>
    /// <param name="Profile">
    /// The Cargo profile used to build it: "dev"/"debug" and "release" are the built-in
    /// profiles (both map to the "debug" and "release" output directories respectively —
    /// see <see cref="ExecutableResolver.ResolveProfileDirectoryName"/>); anything else is
    /// a custom profile name, whose output directory is the profile name itself.
    /// </param>
    /// <param name="TargetDir">
    /// The resolved Cargo target directory (i.e. what `CARGO_TARGET_DIR` points to, or
    /// `&lt;workspace root&gt;/target` when unset — see <see cref="CargoLayout.ResolveTargetDir"/>).
    /// Does NOT include the profile or target-triple subdirectory.
    /// </param>
    /// <param name="WorkspaceRoot">
    /// The Cargo workspace root (directory containing the workspace `Cargo.toml`). Used as
    /// the default debuggee working directory (overridable — see <see cref="RustDebugEnvironment"/>).
    /// </param>
    /// <param name="PackageRoot">
    /// The owning package's root directory (directory containing the package's `Cargo.toml`).
    /// This is the default working directory for the launched process (`docs` phase 1c: "working
    /// directory = package root (configurable)"). Falls back to <see cref="WorkspaceRoot"/> when null.
    /// </param>
    /// <param name="TargetTriple">The `--target &lt;triple&gt;` used to build it, if cross-compiled; null for the host triple.</param>
    /// <param name="CargoExecutablePath">
    /// The `executable` field reported by cargo's `compiler-artifact` JSON message for this
    /// target, when available. Authoritative when present: it is what Cargo actually
    /// produced, so it is preferred over path-convention resolution.
    /// </param>
    /// <param name="TestBinaryPath">
    /// The test binary path reported by `cargo test --no-run --message-format=json` for a
    /// <see cref="LaunchTargetKind.Test"/> target, when available. Authoritative when present,
    /// for the same reason as <see cref="CargoExecutablePath"/>.
    /// </param>
    public sealed record LaunchTarget(
        string Package,
        string Name,
        LaunchTargetKind Kind,
        string Profile,
        string TargetDir,
        string WorkspaceRoot,
        string? PackageRoot = null,
        string? TargetTriple = null,
        string? CargoExecutablePath = null,
        string? TestBinaryPath = null)
    {
        /// <summary>The default debuggee working directory: the package root, falling back to the workspace root.</summary>
        public string EffectivePackageRoot => PackageRoot ?? WorkspaceRoot;
    }
}
