using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Cargo.Commands;
using Kubuno.Cargo.Diagnostics;
using Kubuno.Cargo.Metadata;
using Kubuno.Cargo.Processes;
using Xunit;

namespace Kubuno.Cargo.Tests.Integration
{
    /// <summary>
    /// End-to-end tests against a *real* "cargo" process (the fixture-based tests elsewhere in
    /// this project cover parsing logic against captured output without needing the Rust
    /// toolchain installed; these instead prove the whole pipeline — <see cref="CargoCommand"/>,
    /// <see cref="ProcessRunner"/>, <see cref="CargoMetadataReader"/> and
    /// <see cref="CargoMessageParser"/> together — against a live "cargo build". Skips itself
    /// (rather than failing) when "cargo" isn't on PATH, since this repo's tests otherwise don't
    /// require the Rust toolchain.
    /// </summary>
    public class CargoLiveIntegrationTests : IAsyncLifetime
    {
        private static readonly bool CargoIsAvailable = ProbeCargoAvailable();

        private string _projectDirectory = string.Empty;
        private string _targetDirectory = string.Empty;

        public async Task InitializeAsync()
        {
            if (!CargoIsAvailable)
            {
                return;
            }

            _projectDirectory = Path.Combine(Path.GetTempPath(), "kubuno-cargo-live-" + Guid.NewGuid().ToString("N"));
            _targetDirectory = _projectDirectory + "-target";
            Directory.CreateDirectory(_projectDirectory);

            // Isolate this test's build output from any other Cargo build (including another
            // agent's) that might be running against the ambient CARGO_TARGET_DIR at the same time.
            var runner = new ProcessRunner();
            var init = new ProcessRunRequest("cargo", "init --name kubuno_cargo_live_test --bin --vcs none")
            {
                WorkingDirectory = _projectDirectory,
                EnvironmentVariables = new Dictionary<string, string> { ["CARGO_TARGET_DIR"] = _targetDirectory },
            };
            ProcessRunResult initResult = await runner.RunAsync(init, onOutput: null, CancellationToken.None);
            Assert.True(initResult.Succeeded, $"cargo init failed: {string.Join(Environment.NewLine, initResult.StandardErrorLines)}");
        }

        public Task DisposeAsync()
        {
            try
            {
                if (Directory.Exists(_projectDirectory))
                {
                    Directory.Delete(_projectDirectory, recursive: true);
                }
                if (Directory.Exists(_targetDirectory))
                {
                    Directory.Delete(_targetDirectory, recursive: true);
                }
            }
            catch (IOException)
            {
                // Best-effort cleanup only.
            }
            return Task.CompletedTask;
        }

        private static bool ProbeCargoAvailable()
        {
            try
            {
                using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cargo",
                    Arguments = "--version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                });
                process?.WaitForExit(5000);
                return process is { ExitCode: 0 };
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return false;
            }
        }

        private Dictionary<string, string> TargetDirEnvironment() =>
            new Dictionary<string, string> { ["CARGO_TARGET_DIR"] = _targetDirectory };

        [Fact]
        public async Task Reads_real_metadata_for_a_freshly_generated_package()
        {
            if (!CargoIsAvailable)
            {
                return; // No Rust toolchain on this machine: nothing to verify here.
            }

            var reader = new CargoMetadataReader(new ProcessRunner());
            CargoMetadata metadata = await reader.ReadAsync(_projectDirectory, environmentVariables: TargetDirEnvironment());

            Assert.Equal(Path.GetFullPath(_projectDirectory), Path.GetFullPath(metadata.WorkspaceRoot));
            Assert.Equal(Path.GetFullPath(_targetDirectory), Path.GetFullPath(metadata.TargetDirectory));

            CargoPackage package = Assert.Single(metadata.Packages);
            Assert.Equal("kubuno_cargo_live_test", package.Name);
            CargoTarget bin = Assert.Single(package.Targets);
            Assert.Contains(CargoTargetKind.Bin, bin.Kind);
        }

        [Fact]
        public async Task Builds_the_real_package_and_parses_a_successful_build_finished_event()
        {
            if (!CargoIsAvailable)
            {
                return; // No Rust toolchain on this machine: nothing to verify here.
            }

            CargoCommandLine commandLine = CargoCommand.Build().WithMessageFormat("json-diagnostic-rendered-ansi").ToCommandLine();
            var request = new ProcessRunRequest(commandLine.FileName, commandLine.Arguments)
            {
                WorkingDirectory = _projectDirectory,
                EnvironmentVariables = TargetDirEnvironment(),
            };

            var runner = new ProcessRunner();
            ProcessRunResult result = await runner.RunAsync(request, onOutput: null, CancellationToken.None);
            Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.StandardErrorLines));

            CargoBuildEvent[] events = CargoMessageParser
                .ParseLines(result.StandardOutputLines, _projectDirectory)
                .ToArray();

            var finished = Assert.IsType<CargoBuildFinishedEvent>(events.Last());
            Assert.True(finished.Success);

            CargoArtifactEvent artifactEvent = Assert.IsType<CargoArtifactEvent>(
                events.OfType<CargoArtifactEvent>().Single(e => e.Artifact.Executable is not null));
            Assert.True(File.Exists(artifactEvent.Artifact.Executable));
        }
    }
}
