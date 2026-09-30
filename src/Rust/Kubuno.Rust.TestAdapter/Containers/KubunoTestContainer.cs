using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestWindow.Extensibility;
using Microsoft.VisualStudio.TestWindow.Extensibility.Model;

namespace Kubuno.TestAdapter.Containers
{
    /// <summary>
    /// One Cargo.toml manifest, wrapped as a Test Explorer container. <see cref="Source"/> is
    /// the manifest path itself (see INTEGRATION.md and Discovery/KubunoTestDiscoverer.cs for why
    /// - not the built test executable, whose hash-suffixed name/path only exists after a build
    /// and isn't stable across cargo invocations).
    /// </summary>
    internal sealed class KubunoTestContainer : ITestContainer
    {
        public KubunoTestContainer(ITestContainerDiscoverer discoverer, string manifestPath)
        {
            Discoverer = discoverer ?? throw new ArgumentNullException(nameof(discoverer));
            Source = manifestPath ?? throw new ArgumentNullException(nameof(manifestPath));
        }

        public ITestContainerDiscoverer Discoverer { get; }

        public string Source { get; }

        /// <summary>
        /// Never used: this adapter attaches the debugger itself, per test, via
        /// <c>IFrameworkHandle.LaunchProcessWithDebuggerAttached</c> (see
        /// Execution/KubunoTestExecutor.cs) rather than through this legacy per-container debug
        /// engine selection hook.
        /// </summary>
        public IEnumerable<Guid> DebugEngines => Array.Empty<Guid>();

        /// <summary>Not a managed-framework concept for a Cargo/Rust binary; "None" is what every other non-.NET container adapter (e.g. the CMake Linux/Mac Test Adapter) reports too.</summary>
        public FrameworkVersion TargetFramework => FrameworkVersion.None;

        /// <summary>
        /// Best-effort default (x64): Test Explorer uses this only to label/group containers, it
        /// never gates discovery/execution on it, and this project does not currently plumb the
        /// actual host/target triple through to container-selection time. See INTEGRATION.md for
        /// upgrading this once the orchestrator's <see cref="ICargoWorkspaceSource"/> can report
        /// the workspace's real target architecture.
        /// </summary>
        public Architecture TargetPlatform => Architecture.X64;

        public bool IsAppContainerTestContainer => false;

        public IDeploymentData DeployAppContainer() =>
            throw new NotSupportedException("Kubuno test containers are never app containers (IsAppContainerTestContainer is always false).");

        public int CompareTo(ITestContainer other) =>
            other is null ? 1 : string.Compare(Source, other.Source, StringComparison.OrdinalIgnoreCase);

        public ITestContainer Snapshot() => new KubunoTestContainer(Discoverer, Source);
    }
}
