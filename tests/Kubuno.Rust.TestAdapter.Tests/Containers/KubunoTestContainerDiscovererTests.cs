using System.Collections.Generic;
using System.Linq;
using Kubuno.Rust.TestAdapter.Containers;
using Kubuno.Rust.TestAdapter.Execution;
using Microsoft.VisualStudio.TestWindow.Extensibility;

namespace Kubuno.Rust.TestAdapter.Tests.Containers
{
    public class KubunoTestContainerDiscovererTests
    {
        [Fact]
        public void Exposes_one_container_per_manifest_path_from_the_workspace_source()
        {
            var source = new FakeCargoWorkspaceSource(new[] { @"Z:\ws\a\Cargo.toml", @"Z:\ws\b\Cargo.toml" });
            var discoverer = new KubunoTestContainerDiscoverer(source);

            var containers = discoverer.TestContainers.ToList();

            Assert.Equal(2, containers.Count);
            Assert.Contains(containers, c => c.Source == @"Z:\ws\a\Cargo.toml");
            Assert.Contains(containers, c => c.Source == @"Z:\ws\b\Cargo.toml");
        }

        [Fact]
        public void Every_container_reports_this_adapters_executor_uri()
        {
            var source = new FakeCargoWorkspaceSource(new[] { @"Z:\ws\a\Cargo.toml" });
            var discoverer = new KubunoTestContainerDiscoverer(source);

            Assert.Equal(KubunoTestExecutor.ExecutorUri, discoverer.ExecutorUri);
            Assert.All(discoverer.TestContainers, c => Assert.Same(discoverer, c.Discoverer));
        }

        [Fact]
        public void Re_raises_the_workspace_sources_Changed_event_as_TestContainersUpdated()
        {
            var source = new FakeCargoWorkspaceSource(new[] { @"Z:\ws\a\Cargo.toml" });
            var discoverer = new KubunoTestContainerDiscoverer(source);
            int raisedCount = 0;
            discoverer.TestContainersUpdated += (_, _) => raisedCount++;

            source.SetManifestPaths(new[] { @"Z:\ws\a\Cargo.toml", @"Z:\ws\b\Cargo.toml" });

            Assert.Equal(1, raisedCount);
            Assert.Equal(2, discoverer.TestContainers.Count());
        }

        [Fact]
        public void A_container_is_never_an_app_container_and_reports_no_debug_engines()
        {
            var source = new FakeCargoWorkspaceSource(new[] { @"Z:\ws\a\Cargo.toml" });
            var discoverer = new KubunoTestContainerDiscoverer(source);

            ITestContainer container = discoverer.TestContainers.Single();

            Assert.False(container.IsAppContainerTestContainer);
            Assert.Empty(container.DebugEngines);
        }

        [Fact]
        public void CompareTo_orders_containers_by_source_path()
        {
            var source = new FakeCargoWorkspaceSource(new[] { @"Z:\ws\b\Cargo.toml", @"Z:\ws\a\Cargo.toml" });
            var discoverer = new KubunoTestContainerDiscoverer(source);

            var ordered = discoverer.TestContainers.OrderBy(c => c, Comparer<ITestContainer>.Create((a, b) => a.CompareTo(b))).ToList();

            Assert.Equal(@"Z:\ws\a\Cargo.toml", ordered[0].Source);
            Assert.Equal(@"Z:\ws\b\Cargo.toml", ordered[1].Source);
        }
    }
}
