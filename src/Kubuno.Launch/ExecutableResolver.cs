using System;
using System.IO;

namespace Kubuno.Launch
{
    /// <summary>
    /// Resolves the on-disk path of the executable to launch for a <see cref="LaunchTarget"/>.
    /// </summary>
    public static class ExecutableResolver
    {
        /// <summary>
        /// Resolves the executable path for the given target.
        ///
        /// Precedence (highest first):
        /// 1. <see cref="LaunchTarget.TestBinaryPath"/> for a <see cref="LaunchTargetKind.Test"/>
        ///    target, as reported by `cargo test --no-run --message-format=json` — the
        ///    authoritative source, since test binary names are mangled with a hash suffix
        ///    (`my_test-1a2b3c4d5e6f.exe`) that path convention alone can't reproduce.
        /// 2. <see cref="LaunchTarget.CargoExecutablePath"/> for a bin/example target, as
        ///    reported by cargo's `compiler-artifact` JSON message — authoritative because
        ///    it's what Cargo actually produced.
        /// 3. Path-convention fallback: `&lt;target-dir&gt;/[&lt;triple&gt;/]&lt;profile-dir&gt;/[examples/]&lt;name&gt;.exe`,
        ///    used when no `cargo … --message-format=json` output is available yet (e.g. a
        ///    target listed by `cargo metadata` that hasn't been built in this session).
        /// </summary>
        public static string Resolve(LaunchTarget target)
        {
            if (target is null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (target.Kind == LaunchTargetKind.Test && !string.IsNullOrEmpty(target.TestBinaryPath))
            {
                return target.TestBinaryPath!;
            }

            if (target.Kind != LaunchTargetKind.Test && !string.IsNullOrEmpty(target.CargoExecutablePath))
            {
                return target.CargoExecutablePath!;
            }

            if (target.Kind == LaunchTargetKind.Test)
            {
                // No `cargo test --no-run` artifact was supplied: the mangled test binary
                // name can't be derived from the target name alone.
                throw new InvalidOperationException(
                    $"Cannot resolve the executable for test target '{target.Name}' by path convention alone: " +
                    "test binaries are hash-suffixed by Cargo. Supply LaunchTarget.TestBinaryPath from " +
                    "`cargo test --no-run --message-format=json` instead.");
            }

            var profileDir = CargoLayout.ResolveProfileDir(target.TargetDir, target.TargetTriple, target.Profile);

            var directory = target.Kind == LaunchTargetKind.Example
                ? Path.Combine(profileDir, "examples")
                : profileDir;

            return Path.Combine(directory, target.Name + ".exe");
        }
    }
}
