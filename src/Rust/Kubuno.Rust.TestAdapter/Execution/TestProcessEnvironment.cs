using System;
using System.Collections.Generic;
using System.IO;

using Kubuno.Rust.Launch;

namespace Kubuno.Rust.TestAdapter.Execution
{
    /// <summary>
    /// The environment a test executable needs to start at all when its crate links a Rust dylib (the Kubuno desktop
    /// workspace's <c>kubuno_ui.dll</c>, built with <c>-C prefer-dynamic</c>): the dylib sits next to the test executable in
    /// <c>&lt;target&gt;\&lt;profile&gt;\deps</c>, and Rust's <c>std-*.dll</c> in the toolchain - neither on the PATH Visual Studio
    /// hands to the test host. Without them, <c>--list</c> and the run itself exit with <c>STATUS_DLL_NOT_FOUND</c> and Test
    /// Explorer shows no tests. The same directories F5 prepends (<see cref="RustDebugEnvironment.BuildPathAdditions"/>).
    /// </summary>
    public static class TestProcessEnvironment
    {
        private static readonly object ToolchainLock = new();
        private static string? _targetLibDir;

        /// <summary>
        /// <paramref name="environment"/> (may be null) plus a PATH that starts with the test executable's own folder, its profile
        /// folder and the toolchain's library folder. When the toolchain cannot be resolved, only the two build folders are added.
        /// </summary>
        public static IReadOnlyDictionary<string, string> For(
            string executablePath,
            string workingDirectory,
            IReadOnlyDictionary<string, string>? environment)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (environment is not null)
            {
                foreach (var pair in environment)
                {
                    result[pair.Key] = pair.Value;
                }
            }

            var existingPath = result.TryGetValue("PATH", out var explicitPath) ? explicitPath : Environment.GetEnvironmentVariable("PATH");
            result["PATH"] = BuildPath(executablePath, TargetLibDir(workingDirectory), existingPath);
            return result;
        }

        /// <summary>The PATH value: the executable's folder, its parent (the profile folder), the toolchain library folder, then <paramref name="existingPath"/>.</summary>
        public static string BuildPath(string executablePath, string? toolchainLibDir, string? existingPath)
        {
            var entries = new List<string>();
            var ownDirectory = Path.GetDirectoryName(executablePath);
            if (!string.IsNullOrEmpty(ownDirectory))
            {
                entries.Add(ownDirectory!);
                var parent = Path.GetDirectoryName(ownDirectory);
                if (!string.IsNullOrEmpty(parent))
                {
                    entries.Add(parent!);
                }
            }

            if (!string.IsNullOrEmpty(toolchainLibDir))
            {
                entries.Add(toolchainLibDir!);
            }

            if (!string.IsNullOrEmpty(existingPath))
            {
                entries.Add(existingPath!);
            }

            return string.Join(";", entries);
        }

        /// <summary><c>rustc --print sysroot</c> + <c>rustc -vV</c>, once per process; null when either fails.</summary>
        private static string? TargetLibDir(string workingDirectory)
        {
            lock (ToolchainLock)
            {
                if (_targetLibDir is not null)
                {
                    return _targetLibDir;
                }

                // A real process, not the caller's runner: this is a machine fact, the same for every test program.
                var runner = new CargoBackedSyncProcessRunner(new Kubuno.Rust.Cargo.Processes.ProcessRunner());
                SysrootResult sysroot = RustToolchain.GetSysroot(runner, workingDirectory);
                HostTripleResult hostTriple = RustToolchain.GetHostTriple(runner, workingDirectory);
                if (!sysroot.Succeeded || !hostTriple.Succeeded)
                {
                    return null;
                }

                _targetLibDir = RustToolchain.GetTargetLibDir(sysroot.Sysroot!, hostTriple.HostTriple!);
                return _targetLibDir;
            }
        }
    }
}
