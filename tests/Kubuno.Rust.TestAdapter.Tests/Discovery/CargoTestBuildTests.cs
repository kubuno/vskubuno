using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Rust.Cargo.Diagnostics;
using Kubuno.Rust.Cargo.Processes;
using Kubuno.Rust.TestAdapter.Discovery;
using Kubuno.Rust.TestAdapter.Tests.Fakes;

namespace Kubuno.Rust.TestAdapter.Tests.Discovery
{
    /// <summary>
    /// Parses real `cargo test --no-run --message-format=json` output, captured from
    /// samples/hello-rust (Fixtures/Discovery/hello-rust-no-run.jsonl - a lib with one unit
    /// test, a `[[bin]]` with none, an example, and one integration test file) and from a
    /// throwaway single-lib fixture crate (fixture-crate-no-run.jsonl).
    ///
    /// The central thing under test: hello-rust-no-run.jsonl contains SIX "compiler-artifact"
    /// lines but only THREE are real test-harness binaries - the `[[bin]]` target additionally
    /// produces an ordinary (non-test) build artifact at `.../debug/hello-rust.exe`
    /// (`profile.test: false`), and the lib's own rlib/rmeta artifact has `executable: null`.
    /// <see cref="CargoTestBuild"/> must filter both of those out - see its own class remarks for
    /// why `profile.test` (not modeled by Kubuno.Rust.Cargo's public API) is what makes that possible.
    /// </summary>
    public class CargoTestBuildTests
    {
        private const string ManifestPath = @"Z:\projects\kubuno\vskubuno\samples\hello-rust\Cargo.toml";

        [Fact]
        public async Task Filters_to_exactly_the_three_test_harness_binaries()
        {
            var lines = TestFixtures.ReadAllLines("Discovery", "hello-rust-no-run.jsonl");
            var runner = new FakeProcessRunner(new ProcessRunResult(0, lines, Array.Empty<string>()));

            CargoTestBuildResult result = await CargoTestBuild.RunAsync(runner, ManifestPath, environmentVariables: null, CancellationToken.None);

            Assert.True(result.Success);
            Assert.Equal(3, result.Binaries.Count);

            var lib = Assert.Single(result.Binaries, b => b.Kind == CargoTestBinaryKind.Lib);
            Assert.Equal("hello_rust", lib.TargetName);
            Assert.EndsWith("hello_rust-73c3c3696dd9a88e.exe", lib.ExecutablePath, StringComparison.Ordinal);

            var bin = Assert.Single(result.Binaries, b => b.Kind == CargoTestBinaryKind.Bin);
            Assert.Equal("hello-rust", bin.TargetName);
            Assert.EndsWith("hello_rust-1c0c90adf61aa746.exe", bin.ExecutablePath, StringComparison.Ordinal);

            var integration = Assert.Single(result.Binaries, b => b.Kind == CargoTestBinaryKind.Integration);
            Assert.Equal("basic", integration.TargetName);
            Assert.EndsWith("basic-5471b5231873c648.exe", integration.ExecutablePath, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Excludes_the_ordinary_non_test_bin_artifact_and_the_lib_rlib()
        {
            var lines = TestFixtures.ReadAllLines("Discovery", "hello-rust-no-run.jsonl");
            var runner = new FakeProcessRunner(new ProcessRunResult(0, lines, Array.Empty<string>()));

            CargoTestBuildResult result = await CargoTestBuild.RunAsync(runner, ManifestPath, environmentVariables: null, CancellationToken.None);

            Assert.DoesNotContain(result.Binaries, b => b.ExecutablePath.EndsWith("hello-rust.exe", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(result.Binaries, b => b.ExecutablePath.EndsWith(".rlib", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task Resolves_the_target_directory_three_levels_above_the_executable()
        {
            var lines = TestFixtures.ReadAllLines("Discovery", "fixture-crate-no-run.jsonl");
            var runner = new FakeProcessRunner(new ProcessRunResult(0, lines, Array.Empty<string>()));

            CargoTestBuildResult result = await CargoTestBuild.RunAsync(
                runner, @"C:\Users\martinien\AppData\Local\Temp\kubuno-testadapter-fixture-crate\Cargo.toml", environmentVariables: null, CancellationToken.None);

            var binary = Assert.Single(result.Binaries);
            // executable: C:/kubuno-build/agent-testadapter-fixture\debug\deps\fixture_crate-....exe
            Assert.Equal(@"C:\kubuno-build\agent-testadapter-fixture\debug\deps", System.IO.Path.GetDirectoryName(binary.ExecutablePath)!.Replace("/", "\\"));
            Assert.Equal(@"C:\kubuno-build\agent-testadapter-fixture", binary.TargetDirectory.Replace("/", "\\"));
        }

        [Fact]
        public async Task Reports_diagnostics_and_failure_when_the_build_fails()
        {
            // Synthetic (not a captured real fixture, unlike the above): one compile error and a
            // failed build-finished, enough to exercise the diagnostics/success plumbing that
            // reuses CargoMessageParser (see CargoTestBuild's class remarks).
            string[] lines =
            {
                """{"reason":"compiler-message","package_id":"p#0.1.0","target":{"kind":["lib"],"crate_types":["lib"],"name":"broken","src_path":"C:\\broken\\src\\lib.rs","test":true},"message":{"$message_type":"diagnostic","message":"mismatched types","level":"error","spans":[],"children":[],"code":null}}""",
                """{"reason":"build-finished","success":false}""",
            };
            var runner = new FakeProcessRunner(new ProcessRunResult(101, lines, Array.Empty<string>()));

            CargoTestBuildResult result = await CargoTestBuild.RunAsync(runner, @"C:\broken\Cargo.toml", environmentVariables: null, CancellationToken.None);

            Assert.False(result.Success);
            Assert.Empty(result.Binaries);
            var diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal(CargoDiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Equal("mismatched types", diagnostic.Message);
        }

        [Fact]
        public async Task Passes_the_manifest_path_and_environment_variables_through_to_the_process_request()
        {
            var lines = TestFixtures.ReadAllLines("Discovery", "fixture-crate-no-run.jsonl");
            var runner = new FakeProcessRunner(new ProcessRunResult(0, lines, Array.Empty<string>()));
            var env = new System.Collections.Generic.Dictionary<string, string> { ["CARGO_TARGET_DIR"] = @"C:\kubuno-build\agent-testadapter-fixture" };

            await CargoTestBuild.RunAsync(runner, @"C:\Users\martinien\AppData\Local\Temp\kubuno-testadapter-fixture-crate\Cargo.toml", env, CancellationToken.None);

            var request = Assert.Single(runner.Requests);
            Assert.Equal("cargo", request.FileName);
            Assert.Contains("--no-run", request.Arguments);
            Assert.Contains("--message-format", request.Arguments);
            Assert.Same(env, request.EnvironmentVariables);
        }
    }
}
