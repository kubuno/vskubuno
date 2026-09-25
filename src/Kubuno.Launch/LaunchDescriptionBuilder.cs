using System;
using System.Collections.Generic;

namespace Kubuno.Launch
{
    /// <summary>
    /// Composes <see cref="ExecutableResolver"/>, <see cref="RustDebugEnvironment"/> and
    /// <see cref="RustToolchain"/> into one <see cref="LaunchDescription"/> for a
    /// <see cref="LaunchTarget"/>. This is the single entry point a caller (the VSIX, or a
    /// test) needs after it already has a resolved sysroot and host triple in hand.
    /// </summary>
    public static class LaunchDescriptionBuilder
    {
        /// <param name="target">The target to launch.</param>
        /// <param name="sysroot">The toolchain sysroot (see <see cref="RustToolchain.GetSysroot"/>).</param>
        /// <param name="hostTriple">The host triple whose std lib directory to add to PATH.</param>
        /// <param name="testArgs">
        /// For a <see cref="LaunchTargetKind.Test"/> target, the arguments to pass (build
        /// with <see cref="TestLaunchArgs.Build"/>); ignored for bin/example targets, which
        /// always launch with no arguments (the caller can still override via
        /// <paramref name="argumentsOverride"/>).
        /// </param>
        /// <param name="argumentsOverride">When set, replaces the arguments entirely (for bin/example targets that need CLI args).</param>
        /// <param name="existingPath">The inherited PATH to append after the computed dylib search directories, or null to omit it.</param>
        /// <param name="rustBacktrace">The RUST_BACKTRACE value to set, or null to omit the variable.</param>
        /// <param name="environmentOverrides">Extra/overriding environment variables, applied last.</param>
        /// <param name="workingDirectoryOverride">Overrides the default working directory (the target's package root).</param>
        public static LaunchDescription Build(
            LaunchTarget target,
            string sysroot,
            string hostTriple,
            IReadOnlyList<string>? testArgs = null,
            IReadOnlyList<string>? argumentsOverride = null,
            string? existingPath = null,
            string? rustBacktrace = "1",
            IReadOnlyDictionary<string, string>? environmentOverrides = null,
            string? workingDirectoryOverride = null)
        {
            if (target is null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            var executablePath = ExecutableResolver.Resolve(target);

            var environment = RustDebugEnvironment.Build(
                target.TargetDir,
                target.TargetTriple,
                target.Profile,
                sysroot,
                hostTriple,
                existingPath,
                rustBacktrace,
                environmentOverrides);

            var natvisFiles = RustToolchain.FindNatvisFiles(sysroot);

            var arguments = argumentsOverride
                ?? (target.Kind == LaunchTargetKind.Test ? testArgs : null)
                ?? Array.Empty<string>();

            return new LaunchDescription
            {
                Name = target.Name,
                ExecutablePath = executablePath,
                Arguments = arguments,
                WorkingDirectory = workingDirectoryOverride ?? target.EffectivePackageRoot,
                EnvironmentVariables = environment,
                NatvisFiles = natvisFiles,
                Debugger = "native",
            };
        }
    }
}
