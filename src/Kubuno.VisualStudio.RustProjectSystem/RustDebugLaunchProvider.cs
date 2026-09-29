using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Threading.Tasks;
using Kubuno.Cargo.Metadata;
using Kubuno.Cargo.Processes;
using Kubuno.Launch;
using Kubuno.VisualStudio.Core;
using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.Debug;
using Microsoft.VisualStudio.ProjectSystem.Properties;
using Microsoft.VisualStudio.ProjectSystem.VS.Debug;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;

namespace Kubuno.VisualStudio.RustProjectSystem
{
    /// <summary>
    /// F5/Ctrl+F5 for a <c>.rsproj</c> (docs/RSPROJ.md work package 4). CPS's own debug seam, the
    /// same one the JavaScript project system plugs its <c>ScriptDebugger</c> into: CPS picks the
    /// <see cref="IDebugLaunchProvider"/> whose <see cref="ExportDebuggerAttribute"/> name equals
    /// the project's <c>DebuggerFlavor</c> property (set to <see cref="DebuggerName"/> by
    /// Kubuno.Rust.Sdk's <c>Sdk.props</c>, read through its <c>debugger_general.xaml</c> rule), after
    /// the solution build manager has already built the project - so build-before-run comes from
    /// Visual Studio itself. Verified by reflection against the installed CPS assemblies
    /// (<c>Microsoft.VisualStudio.ProjectSystem[.VS].dll</c>, 17.0.0.0): the managed project
    /// system's <c>IDebugProfileLaunchTargetsProvider</c> does not exist in CPS proper.
    ///
    /// The launch itself is <see cref="LaunchDescriptionBuilder"/>'s logic without the JSON file
    /// Open Folder needs: native (MSVC/PDB) engine, <c>$(TargetPath)</c>, and PATH prepended with
    /// the profile directory, its <c>deps</c> and the toolchain's std lib directory
    /// (<see cref="RustDebugEnvironment"/>) - what a <c>-C prefer-dynamic</c> build such as the
    /// Kubuno desktop workspace (<c>kubuno_ui.dll</c> + <c>std-*.dll</c>) needs to start at all.
    /// </summary>
    [ExportDebugger(DebuggerName)]
    [AppliesTo(RustProjectCapabilities.RustProjectSystem)]
    internal sealed class RustDebugLaunchProvider : DebugLaunchProviderBase
    {
        /// <summary>The <c>DebuggerFlavor</c> value, and the name of the debugger rule (<c>rust_debugger.xaml</c>).</summary>
        public const string DebuggerName = "RustDebugger";

        private static readonly object ToolchainLock = new();
        private static (string Sysroot, string HostTriple)? _toolchain;

        [ImportingConstructor]
        public RustDebugLaunchProvider(ConfiguredProject configuredProject)
            : base(configuredProject)
        {
        }

        public override Task<bool> CanLaunchAsync(DebugLaunchOptions launchOptions) => Task.FromResult(true);

        public override async Task<IReadOnlyList<IDebugLaunchSettings>> QueryDebugTargetsAsync(DebugLaunchOptions launchOptions)
        {
            var properties = ConfiguredProject.Services.ProjectPropertiesProvider!.GetCommonProperties();

            var targetPath = await properties.GetEvaluatedPropertyValueAsync("TargetPath").ConfigureAwait(false);
            if (!string.IsNullOrEmpty(targetPath) && !File.Exists(targetPath))
            {
                targetPath = await ResolveFromCargoTargetDirectoryAsync(properties, targetPath).ConfigureAwait(false) ?? targetPath;
            }

            if (string.IsNullOrEmpty(targetPath) || !File.Exists(targetPath))
            {
                // Surfaced by Visual Studio as the F5 error message.
                throw new FileNotFoundException(
                    $"The Rust executable '{targetPath}' does not exist. Build the project first, or set CargoBin/CargoPackage to the [[bin]] target to run.",
                    targetPath);
            }

            var manifestPath = await properties.GetEvaluatedPropertyValueAsync("CargoManifestPath").ConfigureAwait(false);
            var manifestDirectory = Path.GetDirectoryName(manifestPath) ?? Path.GetDirectoryName(targetPath)!;
            var arguments = await properties.GetEvaluatedPropertyValueAsync("RustDebuggerCommandArguments").ConfigureAwait(false);
            var workingDirectory = await properties.GetEvaluatedPropertyValueAsync("RustDebuggerWorkingDirectory").ConfigureAwait(false);
            var environmentText = await properties.GetEvaluatedPropertyValueAsync("RustDebuggerEnvironment").ConfigureAwait(false);
            var backtrace = await properties.GetEvaluatedPropertyValueAsync("RustDebuggerBacktrace").ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(workingDirectory))
            {
                workingDirectory = manifestDirectory;
            }
            else if (!Path.IsPathRooted(workingDirectory))
            {
                workingDirectory = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ConfiguredProject.UnconfiguredProject.FullPath)!, workingDirectory));
            }

            var (sysroot, hostTriple) = ResolveToolchain(manifestDirectory);
            // Standard-library natvis is embedded in every PDB rustc links; Kubuno.natvis is a VSIX asset. What
            // remains per-user is Just My Code and the step filters (docs/DEBUGGING.md).
            RustDebuggerSettings.EnsureDebuggerFilesInstalled();
            // Exception Settings > C++ Exceptions > " ?? ::st_panic" (Rust panics) must reach the engine (see there).
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            RustDebuggerSettings.EnsurePanicExceptionSetting();
            await TaskScheduler.Default;

            // The profile directory is where cargo put $(TargetPath) itself, which already accounts
            // for CARGO_TARGET_DIR, the profile and any --target triple; RustDebugEnvironment only
            // needs "<target dir>" + "<profile>" to rebuild that same directory.
            var profileDirectory = Path.GetDirectoryName(targetPath)!;
            var targetDirectory = Path.GetDirectoryName(profileDirectory)!;

            var environment = RustDebugEnvironment.Build(
                targetDirectory,
                targetTriple: null,
                profile: Path.GetFileName(profileDirectory),
                sysroot,
                hostTriple,
                existingPath: Environment.GetEnvironmentVariable("PATH"),
                // Debug page, "Backtraces on panic" (on by default): off omits RUST_BACKTRACE entirely.
                rustBacktrace: string.Equals(backtrace, "false", StringComparison.OrdinalIgnoreCase) ? null : "1",
                overrides: DebugEnvironmentText.Parse(environmentText));

            var settings = new DebugLaunchSettings(launchOptions | DebugLaunchOptions.MergeEnvironment)
            {
                LaunchOperation = DebugLaunchOperation.CreateProcess,
                LaunchDebugEngineGuid = DebuggerEngines.NativeOnlyEngine,
                Executable = targetPath,
                Arguments = arguments ?? string.Empty,
                CurrentDirectory = workingDirectory,
            };

            foreach (var pair in environment)
            {
                settings.Environment[pair.Key] = pair.Value;
            }

            return new IDebugLaunchSettings[] { settings };
        }

        /// <summary>
        /// The evaluated <c>$(TargetPath)</c> assumes <c>&lt;manifest dir&gt;\target</c> when neither
        /// <c>&lt;CargoTargetDir&gt;</c> nor <c>CARGO_TARGET_DIR</c> is set, but cargo itself also honors
        /// <c>build.target-dir</c> from any <c>.cargo/config.toml</c> (e.g. a per-user one that keeps build
        /// outputs off a network share). Ask cargo where its target directory really is and rebuild
        /// "&lt;target dir&gt;\&lt;profile dir&gt;\&lt;exe&gt;" from it; null when that does not exist either.
        /// </summary>
        private static async Task<string?> ResolveFromCargoTargetDirectoryAsync(IProjectProperties properties, string targetPath)
        {
            var explicitTargetDir = await properties.GetEvaluatedPropertyValueAsync("CargoTargetDir").ConfigureAwait(false);
            var manifestPath = await properties.GetEvaluatedPropertyValueAsync("CargoManifestPath").ConfigureAwait(false);
            var manifestDirectory = Path.GetDirectoryName(manifestPath);
            if (!string.IsNullOrEmpty(explicitTargetDir) || string.IsNullOrEmpty(manifestDirectory))
            {
                return null;
            }

            try
            {
                var metadata = await new CargoMetadataReader(new ProcessRunner())
                    .ReadAsync(manifestDirectory!, manifestPath)
                    .ConfigureAwait(false);
                if (string.IsNullOrEmpty(metadata.TargetDirectory))
                {
                    return null;
                }

                var profileDirectoryName = Path.GetFileName(Path.GetDirectoryName(targetPath));
                var candidate = Path.Combine(metadata.TargetDirectory, profileDirectoryName, Path.GetFileName(targetPath));
                return File.Exists(candidate) ? candidate : null;
            }
            catch (CargoMetadataException)
            {
                return null;
            }
        }

        /// <summary>
        /// `rustc --print sysroot` / `rustc -vV`, once per Visual Studio session (run from the
        /// package directory so a <c>rust-toolchain.toml</c> there is honored the first time).
        /// </summary>
        private static (string Sysroot, string HostTriple) ResolveToolchain(string workingDirectory)
        {
            lock (ToolchainLock)
            {
                if (_toolchain is { } cached)
                {
                    return cached;
                }

                var runner = new SystemProcessRunner();
                var sysroot = RustToolchain.GetSysroot(runner, workingDirectory);
                if (!sysroot.Succeeded)
                {
                    throw new InvalidOperationException($"Could not resolve the Rust sysroot: {sysroot.Error}");
                }

                var hostTriple = RustToolchain.GetHostTriple(runner, workingDirectory);
                if (!hostTriple.Succeeded)
                {
                    throw new InvalidOperationException($"Could not resolve the Rust host triple: {hostTriple.Error}");
                }

                var resolved = (sysroot.Sysroot!, hostTriple.HostTriple!);
                _toolchain = resolved;
                return resolved;
            }
        }
    }
}
