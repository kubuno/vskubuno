using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Kubuno.Launch
{
    /// <summary>
    /// Computes the environment a Rust debuggee needs to start correctly under the native
    /// debugger, in particular the extra PATH entries a `-C prefer-dynamic` build needs to
    /// find its dylibs (the Kubuno desktop workspace links `kubuno_ui.dll` this way, and it
    /// pulls in `std-*.dll` — see `CLAUDE.md` and `docs/ARCHITECTURE.md`).
    /// </summary>
    public static class RustDebugEnvironment
    {
        /// <summary>
        /// The PATH entries to prepend, in the order the loader should search them:
        /// 1. the profile directory itself (`&lt;target-dir&gt;/[&lt;triple&gt;/]&lt;profile&gt;`) —
        ///    where the crate's own dylibs (e.g. `kubuno_ui.dll`) land;
        /// 2. its `deps` subdirectory — where dependency dylibs land;
        /// 3. the toolchain's host-triple std lib directory — where `std-*.dll` lives when
        ///    the build links it dynamically.
        /// </summary>
        public static IReadOnlyList<string> BuildPathAdditions(string targetDir, string? targetTriple, string profile, string sysroot, string hostTriple)
        {
            var profileDir = CargoLayout.ResolveProfileDir(targetDir, targetTriple, profile);

            return new[]
            {
                profileDir,
                Path.Combine(profileDir, "deps"),
                RustToolchain.GetTargetLibDir(sysroot, hostTriple),
            };
        }

        /// <summary>
        /// Builds the full environment variable map for the debuggee: PATH with the dylib
        /// search directories prepended (existing PATH preserved via
        /// <paramref name="existingPath"/>, so the debuggee can still find e.g. system
        /// DLLs), `RUST_BACKTRACE=1` by default, and any caller overrides applied last (so
        /// a caller can turn RUST_BACKTRACE off, or add its own variables).
        /// </summary>
        /// <param name="targetDir">The resolved Cargo target directory (see <see cref="CargoLayout.ResolveTargetDir"/>).</param>
        /// <param name="targetTriple">The `--target &lt;triple&gt;` used to build, or null for the host triple.</param>
        /// <param name="profile">The Cargo profile (e.g. "dev", "release", or a custom profile name).</param>
        /// <param name="sysroot">The toolchain sysroot (see <see cref="RustToolchain.GetSysroot"/>).</param>
        /// <param name="hostTriple">The host triple whose std lib directory to add (e.g. "x86_64-pc-windows-msvc").</param>
        /// <param name="existingPath">
        /// The PATH to append after the prepended entries, or null to omit the inherited
        /// PATH entirely (the caller — the VSIX — decides whether/how to read the current
        /// process's PATH; this library stays pure and doesn't read the environment itself).
        /// </param>
        /// <param name="rustBacktrace">The default RUST_BACKTRACE value; pass null to omit the variable entirely.</param>
        /// <param name="overrides">
        /// Applied last and win over every computed value above, including PATH and
        /// RUST_BACKTRACE — this is how a caller opts out of either.
        /// </param>
        public static IReadOnlyDictionary<string, string> Build(
            string targetDir,
            string? targetTriple,
            string profile,
            string sysroot,
            string hostTriple,
            string? existingPath = null,
            string? rustBacktrace = "1",
            IReadOnlyDictionary<string, string>? overrides = null)
        {
            var pathAdditions = BuildPathAdditions(targetDir, targetTriple, profile, sysroot, hostTriple);

            var path = string.IsNullOrEmpty(existingPath)
                ? string.Join(";", pathAdditions)
                : string.Join(";", pathAdditions.Concat(new[] { existingPath }));

            var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["PATH"] = path,
            };

            if (rustBacktrace is not null)
            {
                env["RUST_BACKTRACE"] = rustBacktrace;
            }

            if (overrides is not null)
            {
                foreach (var kvp in overrides)
                {
                    env[kvp.Key] = kvp.Value;
                }
            }

            return env;
        }
    }
}
