using System;
using Kubuno.Rust.Cargo.Processes;
using Kubuno.Rust.Launch;

namespace Kubuno.Rust.TestAdapter.Execution
{
    /// <summary>
    /// Builds the <see cref="LaunchDescription"/> for "debug this test", reusing Kubuno.Rust.Launch
    /// exactly as phase 1c does for a bin/example target (see Kubuno.Rust.Launch's own
    /// `LaunchDescriptionBuilder`/`TestLaunchArgs`/`RustDebugEnvironment`/`RustToolchain`) rather
    /// than duplicating any of that logic here.
    /// </summary>
    public static class DebugTestLauncher
    {
        /// <param name="processRunner">Used to run `rustc --print sysroot`/`rustc -vV` (via <see cref="CargoBackedSyncProcessRunner"/> - see its doc comment).</param>
        /// <param name="executablePath">The already-built test binary (from discovery/<see cref="Discovery.CargoTestBinary"/> - authoritative, so <see cref="ExecutableResolver"/>'s path-convention fallback is never exercised here).</param>
        /// <param name="workingDirectory">The debuggee's working directory (the owning package's root).</param>
        /// <param name="targetDirectory">The resolved Cargo target directory (<see cref="Discovery.CargoTestBinary.TargetDirectory"/>), needed for the dylib-search PATH additions.</param>
        /// <param name="libtestName">The exact test to run under the debugger (`--exact`, single test - see the remarks on <see cref="Execution.KubunoTestExecutor"/> for why debugging never batches).</param>
        /// <param name="existingPath">The current process's own PATH, appended after the computed dylib directories (see <see cref="RustDebugEnvironment.Build"/>).</param>
        public static LaunchDescription Build(
            Kubuno.Rust.Cargo.Processes.IProcessRunner processRunner,
            string executablePath,
            string workingDirectory,
            string targetDirectory,
            string libtestName,
            string? existingPath)
        {
            if (processRunner is null)
            {
                throw new ArgumentNullException(nameof(processRunner));
            }

            var syncRunner = new CargoBackedSyncProcessRunner(processRunner);

            SysrootResult sysroot = RustToolchain.GetSysroot(syncRunner, workingDirectory);
            if (!sysroot.Succeeded)
            {
                throw new InvalidOperationException($"Kubuno: could not resolve the Rust toolchain sysroot for debugging: {sysroot.Error}");
            }

            HostTripleResult hostTriple = RustToolchain.GetHostTriple(syncRunner, workingDirectory);
            if (!hostTriple.Succeeded)
            {
                throw new InvalidOperationException($"Kubuno: could not resolve the Rust host triple for debugging: {hostTriple.Error}");
            }

            // "test": the profile `cargo test` itself builds under (see Kubuno.Rust.Launch's
            // `CargoLayout.ResolveProfileDirectoryName`, which maps it to the "debug" output
            // directory) - not "dev"/"debug", which would be correct for a `cargo build`
            // artifact but is not the profile name Cargo actually used here.
            var target = new LaunchTarget(
                Package: string.Empty,
                Name: libtestName,
                Kind: LaunchTargetKind.Test,
                Profile: "test",
                TargetDir: targetDirectory,
                WorkspaceRoot: workingDirectory,
                PackageRoot: workingDirectory,
                TestBinaryPath: executablePath);

            return LaunchDescriptionBuilder.Build(
                target,
                sysroot.Sysroot!,
                hostTriple.HostTriple!,
                testArgs: TestLaunchArgs.Build(libtestName),
                existingPath: existingPath);
        }
    }
}
