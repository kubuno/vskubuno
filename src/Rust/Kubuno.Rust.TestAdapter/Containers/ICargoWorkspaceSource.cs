using System;
using System.Collections.Generic;

namespace Kubuno.Rust.TestAdapter.Containers
{
    /// <summary>
    /// The seam between this library and however the VSIX already knows which Cargo.toml
    /// manifests exist in the open workspace. <see cref="KubunoTestContainerDiscoverer"/>
    /// consumes this rather than talking to Visual Studio services directly, so its own
    /// container-enumeration/change-notification logic is testable without a running Visual
    /// Studio (see the fake implementations under tests/Kubuno.Rust.TestAdapter.Tests/Containers).
    ///
    /// See INTEGRATION.md for exactly what the orchestrator implements against Kubuno.VisualStudio's
    /// existing Open Folder/Cargo workspace plumbing to satisfy this contract, and how it is
    /// wired into <see cref="KubunoTestContainerDiscoverer"/>'s MEF import.
    /// </summary>
    public interface ICargoWorkspaceSource
    {
        /// <summary>
        /// Every Cargo.toml manifest path to offer Test Explorer as a container - normally one
        /// per workspace member package, not just the workspace root's own Cargo.toml:
        /// `cargo test --manifest-path &lt;member&gt;` builds and discovers just that package,
        /// keeping discovery/build incremental per package as the developer edits.
        /// </summary>
        IReadOnlyList<string> GetManifestPaths();

        /// <summary>
        /// Raised whenever the manifest set may have changed (workspace (re)opened, a Cargo.toml
        /// added/removed/edited). <see cref="KubunoTestContainerDiscoverer"/> re-raises this as
        /// its own "TestContainersUpdated" event, which is what makes Test Explorer re-query its
        /// containers and, in turn, re-run discovery.
        /// </summary>
        event EventHandler Changed;
    }
}
