namespace Kubuno.Rust.Launch
{
    /// <summary>
    /// The kind of Cargo target being launched. Mirrors the subset of `cargo metadata`
    /// target kinds phase 1c cares about (see `docs/ARCHITECTURE.md` phase 1c); the crate
    /// providing full `cargo metadata` parsing (Kubuno.Rust.Cargo) is owned by another agent, so
    /// this library defines its own minimal enum instead of depending on it.
    /// </summary>
    public enum LaunchTargetKind
    {
        /// <summary>A `[[bin]]` target, or the implicit `src/main.rs` binary.</summary>
        Bin,

        /// <summary>An `examples/*.rs` target.</summary>
        Example,

        /// <summary>A test binary built by `cargo test --no-run` (unit or integration test).</summary>
        Test,
    }
}
