using System;
using System.Linq;
using Kubuno.Cargo.Processes;
using Kubuno.Launch;
using Kubuno.TestAdapter.Execution;
using Kubuno.TestAdapter.Tests.Fakes;

namespace Kubuno.TestAdapter.Tests.Execution
{
    /// <summary>Builds a debug <see cref="LaunchDescription"/> purely (no real process ever spawned - `rustc` calls go through a <see cref="FakeProcessRunner"/>, matching how RustToolchainTests likely fakes Kubuno.Launch's own IProcessRunner).</summary>
    public class DebugTestLauncherTests
    {
        private static FakeProcessRunner CreateRustcRunner() => new FakeProcessRunner(request =>
        {
            if (request.Arguments.Contains("--print sysroot"))
            {
                return new Kubuno.Cargo.Processes.ProcessRunResult(0, new[] { @"C:\Users\me\.rustup\toolchains\stable-x86_64-pc-windows-msvc" }, Array.Empty<string>());
            }
            if (request.Arguments.Contains("-vV"))
            {
                return new Kubuno.Cargo.Processes.ProcessRunResult(0, new[]
                {
                    "rustc 1.98.1 (797e8a9bc 2026-08-05)",
                    "host: x86_64-pc-windows-msvc",
                }, Array.Empty<string>());
            }
            throw new InvalidOperationException($"Unexpected rustc invocation: {request.Arguments}");
        });

        [Fact]
        public void Builds_a_test_kind_launch_description_using_the_authoritative_executable_path()
        {
            LaunchDescription description = DebugTestLauncher.Build(
                CreateRustcRunner(),
                executablePath: @"C:\target\debug\deps\fixture_crate-abc.exe",
                workingDirectory: @"C:\fixture-crate",
                targetDirectory: @"C:\target",
                libtestName: "tests::it_passes",
                existingPath: null);

            Assert.Equal(@"C:\target\debug\deps\fixture_crate-abc.exe", description.ExecutablePath);
            Assert.Equal(@"C:\fixture-crate", description.WorkingDirectory);
            Assert.Equal("tests::it_passes", description.Name);
            Assert.Equal("native", description.Debugger);
        }

        [Fact]
        public void Passes_exact_and_nocapture_test_arguments()
        {
            LaunchDescription description = DebugTestLauncher.Build(
                CreateRustcRunner(), @"C:\exe.exe", @"C:\root", @"C:\target", "tests::it_passes", existingPath: null);

            Assert.Contains("tests::it_passes", description.Arguments);
            Assert.Contains("--exact", description.Arguments);
            Assert.Contains("--nocapture", description.Arguments);
        }

        [Fact]
        public void The_dylib_search_path_includes_the_deps_directory_and_the_toolchains_std_lib_dir()
        {
            LaunchDescription description = DebugTestLauncher.Build(
                CreateRustcRunner(), @"C:\target\debug\deps\exe.exe", @"C:\root", @"C:\target", "tests::it_passes", existingPath: null);

            string path = description.EnvironmentVariables["PATH"];
            Assert.Contains(@"C:\target\debug\deps", path);
            Assert.Contains(@"rustlib\x86_64-pc-windows-msvc\lib", path);
        }

        [Fact]
        public void Throws_a_clear_error_when_the_toolchain_cannot_be_resolved()
        {
            var runner = new FakeProcessRunner(new Kubuno.Cargo.Processes.ProcessRunResult(1, Array.Empty<string>(), new[] { "rustc: command not found" }));

            var ex = Assert.Throws<InvalidOperationException>(() =>
                DebugTestLauncher.Build(runner, @"C:\exe.exe", @"C:\root", @"C:\target", "tests::it_passes", existingPath: null));

            Assert.Contains("toolchain", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }
}
