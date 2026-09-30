using System;
using System.Linq;
using Kubuno.Rust.Cargo.Processes;
using Kubuno.Rust.TestAdapter.Discovery;
using Kubuno.Rust.TestAdapter.Tests.Fakes;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;

namespace Kubuno.Rust.TestAdapter.Tests.Discovery
{
    /// <summary>
    /// End-to-end discovery over the real samples/hello-rust fixtures: builds (cargo test
    /// --no-run), lists each of the three resulting test binaries, and reports a TestCase per
    /// test - all through a <see cref="FakeProcessRunner"/> that replays the captured output
    /// (Fixtures/Discovery/hello-rust-*), never spawning a real "cargo" process.
    /// </summary>
    public class KubunoTestDiscovererTests
    {
        private const string ManifestPath = @"Z:\projects\kubuno\vskubuno\samples\hello-rust\Cargo.toml";

        private static FakeProcessRunner CreateHelloRustRunner()
        {
            var noRunLines = TestFixtures.ReadAllLines("Discovery", "hello-rust-no-run.jsonl");
            var libListLines = TestFixtures.ReadAllLines("Discovery", "hello-rust-list-lib.terse.txt");
            var binListLines = TestFixtures.ReadAllLines("Discovery", "hello-rust-list-bin.terse.txt");
            var integrationListLines = TestFixtures.ReadAllLines("Discovery", "hello-rust-list-integration.terse.txt");

            return new FakeProcessRunner(request =>
            {
                if (string.Equals(request.FileName, "cargo", StringComparison.Ordinal))
                {
                    return new ProcessRunResult(0, noRunLines, Array.Empty<string>());
                }
                if (request.FileName.Contains("hello_rust-73c3c3696dd9a88e"))
                {
                    return new ProcessRunResult(0, libListLines, Array.Empty<string>());
                }
                if (request.FileName.Contains("hello_rust-1c0c90adf61aa746"))
                {
                    return new ProcessRunResult(0, binListLines, Array.Empty<string>());
                }
                if (request.FileName.Contains("basic-5471b5231873c648"))
                {
                    return new ProcessRunResult(0, integrationListLines, Array.Empty<string>());
                }
                throw new InvalidOperationException($"Unexpected process request: {request.FileName} {request.Arguments}");
            });
        }

        [Fact]
        public void Discovers_the_two_unit_tests_and_the_one_integration_test_across_all_three_binaries()
        {
            var discoverer = new KubunoTestDiscoverer(CreateHelloRustRunner());
            var sink = new BufferingTestCaseSink();

            discoverer.DiscoverTests(new[] { ManifestPath }, discoveryContext: null!, logger: null!, sink);

            Assert.Equal(2, sink.TestCases.Count);
            Assert.Contains(sink.TestCases, t => t.FullyQualifiedName == "hello_rust::tests::greet_includes_the_name");
            Assert.Contains(sink.TestCases, t => t.FullyQualifiedName == "basic::greet_includes_the_name");
        }

        [Fact]
        public void Every_discovered_TestCase_uses_the_manifest_path_as_its_Source()
        {
            var discoverer = new KubunoTestDiscoverer(CreateHelloRustRunner());
            var sink = new BufferingTestCaseSink();

            discoverer.DiscoverTests(new[] { ManifestPath }, discoveryContext: null!, logger: null!, sink);

            Assert.All(sink.TestCases, t => Assert.Equal(ManifestPath, t.Source, StringComparer.OrdinalIgnoreCase));
        }

        [Fact]
        public void Every_discovered_TestCase_uses_this_adapters_executor_uri()
        {
            var discoverer = new KubunoTestDiscoverer(CreateHelloRustRunner());
            var sink = new BufferingTestCaseSink();

            discoverer.DiscoverTests(new[] { ManifestPath }, discoveryContext: null!, logger: null!, sink);

            Assert.All(sink.TestCases, t => Assert.Equal(KubunoTestAdapterConstants.ExecutorUriString, t.ExecutorUri.ToString()));
        }

        [Fact]
        public void The_bin_targets_test_harness_with_zero_tests_contributes_no_test_cases()
        {
            var discoverer = new KubunoTestDiscoverer(CreateHelloRustRunner());
            var sink = new BufferingTestCaseSink();

            discoverer.DiscoverTests(new[] { ManifestPath }, discoveryContext: null!, logger: null!, sink);

            Assert.DoesNotContain(sink.TestCases, t => t.FullyQualifiedName.StartsWith("hello-rust::", StringComparison.Ordinal));
        }

        [Fact]
        public void A_build_failure_reports_no_test_cases_and_does_not_throw()
        {
            var runner = new FakeProcessRunner(_ => new ProcessRunResult(
                101,
                new[] { """{"reason":"build-finished","success":false}""" },
                Array.Empty<string>()));
            var discoverer = new KubunoTestDiscoverer(runner);
            var sink = new BufferingTestCaseSink();

            discoverer.DiscoverTests(new[] { ManifestPath }, discoveryContext: null!, logger: null!, sink);

            Assert.Empty(sink.TestCases);
        }
    }
}
