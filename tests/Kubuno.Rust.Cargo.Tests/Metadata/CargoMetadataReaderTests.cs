using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Kubuno.Rust.Cargo.Metadata;
using Kubuno.Rust.Cargo.Processes;
using Kubuno.Rust.Cargo.Tests.Fakes;
using Xunit;

namespace Kubuno.Rust.Cargo.Tests.Metadata
{
    /// <summary>
    /// Tests the request <see cref="CargoMetadataReader"/> builds (command line, working
    /// directory, environment) and its error handling, independently of real JSON content.
    /// </summary>
    public class CargoMetadataReaderTests
    {
        [Fact]
        public async Task Builds_the_expected_command_line_and_working_directory()
        {
            var runner = new FakeProcessRunner(new ProcessRunResult(0, new[] { "{}" }, Array.Empty<string>()));
            var reader = new CargoMetadataReader(runner);

            await reader.ReadAsync(workingDirectory: @"C:\repo\my-workspace");

            Assert.NotNull(runner.LastRequest);
            Assert.Equal("cargo", runner.LastRequest!.FileName);
            Assert.Equal("metadata --format-version 1 --no-deps", runner.LastRequest.Arguments);
            Assert.Equal(@"C:\repo\my-workspace", runner.LastRequest.WorkingDirectory);
        }

        [Fact]
        public async Task Passes_manifest_path_through()
        {
            var runner = new FakeProcessRunner(new ProcessRunResult(0, new[] { "{}" }, Array.Empty<string>()));
            var reader = new CargoMetadataReader(runner);

            await reader.ReadAsync(workingDirectory: @"C:\repo", manifestPath: @"C:\repo\sub\Cargo.toml");

            Assert.Equal(
                @"metadata --format-version 1 --no-deps --manifest-path C:\repo\sub\Cargo.toml",
                runner.LastRequest!.Arguments);
        }

        [Fact]
        public async Task Forwards_CARGO_TARGET_DIR_to_the_child_process_environment()
        {
            var runner = new FakeProcessRunner(new ProcessRunResult(0, new[] { "{}" }, Array.Empty<string>()));
            var reader = new CargoMetadataReader(runner);
            var env = new Dictionary<string, string> { ["CARGO_TARGET_DIR"] = @"C:\kubuno-build\target" };

            await reader.ReadAsync(workingDirectory: @"C:\repo", environmentVariables: env);

            Assert.NotNull(runner.LastRequest!.EnvironmentVariables);
            Assert.Equal(@"C:\kubuno-build\target", runner.LastRequest.EnvironmentVariables!["CARGO_TARGET_DIR"]);
        }

        [Fact]
        public async Task Throws_CargoMetadataException_on_non_zero_exit_code()
        {
            var runner = new FakeProcessRunner(
                new ProcessRunResult(101, Array.Empty<string>(), new[] { "error: could not find `Cargo.toml`" }));
            var reader = new CargoMetadataReader(runner);

            CargoMetadataException ex = await Assert.ThrowsAsync<CargoMetadataException>(
                () => reader.ReadAsync(workingDirectory: @"C:\repo"));

            Assert.Equal(101, ex.ExitCode);
            Assert.Contains("Cargo.toml", ex.StandardError);
        }

        [Fact]
        public async Task Throws_CargoMetadataException_on_malformed_JSON()
        {
            var runner = new FakeProcessRunner(new ProcessRunResult(0, new[] { "not json" }, Array.Empty<string>()));
            var reader = new CargoMetadataReader(runner);

            await Assert.ThrowsAsync<CargoMetadataException>(() => reader.ReadAsync(workingDirectory: @"C:\repo"));
        }
    }
}
