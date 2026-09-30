using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using Kubuno.Rust.TestAdapter.Execution;
using Microsoft.VisualStudio.TestWindow.Extensibility;

namespace Kubuno.Rust.TestAdapter.Containers
{
    /// <summary>
    /// Tells Visual Studio's Test Explorer which Cargo.toml manifests exist (its "test
    /// containers") for an Open Folder Cargo workspace, where there is no project system to
    /// enumerate compiled test assemblies from - see INTEGRATION.md for why this legacy-named
    /// but still-shipping-in-VS-18 MEF contract (confirmed present in
    /// Microsoft.VisualStudio.TestWindow.Interfaces.dll and used by the real, currently-shipping
    /// "CMake Linux and Mac Test Adapter" for the same "no project system" scenario) is the
    /// right mechanism here.
    ///
    /// Exported via MEF (<c>[Export(typeof(ITestContainerDiscoverer))]</c>): Visual Studio's own
    /// composition container constructs this class and supplies whatever exports
    /// <see cref="ICargoWorkspaceSource"/> (see INTEGRATION.md for the orchestrator's export in
    /// Kubuno.VisualStudio) - this project never touches Visual Studio services directly, which
    /// is what keeps this class testable with a fake <see cref="ICargoWorkspaceSource"/> (see
    /// tests/Kubuno.Rust.TestAdapter.Tests/Containers).
    /// </summary>
    [Export(typeof(ITestContainerDiscoverer))]
    public sealed class KubunoTestContainerDiscoverer : ITestContainerDiscoverer
    {
        private readonly ICargoWorkspaceSource _workspaceSource;

        static KubunoTestContainerDiscoverer() => AssemblyResolution.EnsureInstalled();

        [ImportingConstructor]
        public KubunoTestContainerDiscoverer(ICargoWorkspaceSource workspaceSource)
        {
            _workspaceSource = workspaceSource ?? throw new ArgumentNullException(nameof(workspaceSource));
            _workspaceSource.Changed += OnWorkspaceSourceChanged;
        }

        public Uri ExecutorUri { get; } = KubunoTestExecutor.ExecutorUri;

        public IEnumerable<ITestContainer> TestContainers =>
            _workspaceSource.GetManifestPaths().Select(manifestPath => new KubunoTestContainer(this, manifestPath));

        public event EventHandler? TestContainersUpdated;

        private void OnWorkspaceSourceChanged(object sender, EventArgs e) =>
            TestContainersUpdated?.Invoke(this, EventArgs.Empty);
    }
}
