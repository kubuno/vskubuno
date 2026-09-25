using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Cargo.Metadata;
using Kubuno.Cargo.Processes;
using Kubuno.Launch;
using Kubuno.VisualStudio.Logging;

namespace Kubuno.VisualStudio.Debugging
{
    /// <summary>
    /// Generates <c>.vs\launch.vs.json</c> (phase 1c) from `cargo metadata`'s bin and example
    /// targets, using <c>Kubuno.Launch</c> end to end (<see cref="RustToolchain"/> for the
    /// sysroot/host triple, <see cref="LaunchDescriptionBuilder"/> for the PATH/environment a
    /// `-C prefer-dynamic` build needs, <see cref="LaunchVsJsonWriter"/> for the file itself).
    ///
    /// Design choice (see the phase 1c report for the full reasoning): a static
    /// <c>launch.vs.json</c> rather than a live <c>ILaunchDebugTargetProvider</c>/
    /// <c>IVsDebugLaunchTargetProvider</c> MEF component. Those interfaces exist (5 versions of
    /// the former, 3 of the latter, across `Microsoft.VisualStudio.Workspace.Debug` and
    /// `.Extensions.VS.Debug`) but are undocumented beyond their member names, version-fragmented,
    /// and layered on a `ProjectConfigurationService`/`DebugTargetsManager` machinery with no
    /// public walkthrough - implementing against them blind, with no way to compile-check
    /// intermediate assumptions the way `IFileContextProvider`/`IBuildMessageService` could be
    /// (see `Workspace/CargoBuildFileContext.cs`), was judged too high-risk for the time budget.
    /// `launch.vs.json` is fully documented, and its <c>"type": "default"</c> configurations are
    /// handled entirely by Visual Studio's own native debug engine once generated - populating
    /// the "Select Startup Item" dropdown and making F5/Ctrl+F5 work with zero further launch
    /// code from this extension. The one thing that route does not give: an automatic "build
    /// before F5" hook (no `preLaunchTask`-equivalent is documented for a `"type": "default"`
    /// configuration) - builds are expected to happen via the Build/Rebuild/Clean commands
    /// (`Workspace/CargoBuildFileContextAction.cs`), same as for VS's own CMake/Makefile Open
    /// Folder support.
    /// </summary>
    internal static class RustLaunchTargetsGenerator
    {
        /// <param name="workingDirectory">Directory to run `cargo metadata` from (the package directory is fine).</param>
        /// <param name="manifestPath">Path to the `Cargo.toml` that was built.</param>
        public static async Task GenerateAsync(string workingDirectory, string manifestPath, CancellationToken cancellationToken)
        {
            try
            {
                var metadataReader = new CargoMetadataReader(new ProcessRunner());
                var metadata = await metadataReader.ReadAsync(workingDirectory, manifestPath, cancellationToken: cancellationToken).ConfigureAwait(false);

                var launchProcessRunner = new SystemProcessRunner();
                var sysrootResult = RustToolchain.GetSysroot(launchProcessRunner, metadata.WorkspaceRoot);
                if (!sysrootResult.Succeeded)
                {
                    KubunoLog.WriteLine($"Kubuno: could not resolve the Rust sysroot ({sysrootResult.Error}); skipping launch.vs.json generation.");
                    return;
                }

                var hostTripleResult = RustToolchain.GetHostTriple(launchProcessRunner, metadata.WorkspaceRoot);
                if (!hostTripleResult.Succeeded)
                {
                    KubunoLog.WriteLine($"Kubuno: could not resolve the Rust host triple ({hostTripleResult.Error}); skipping launch.vs.json generation.");
                    return;
                }

                var sysroot = sysrootResult.Sysroot!;
                var hostTriple = hostTripleResult.HostTriple!;
                var existingPath = Environment.GetEnvironmentVariable("PATH");

                // See NatvisInstaller's remarks for why this - rather than PDB embedding or a
                // VSIX asset - is the mechanism that actually gets std types (String, Vec,
                // Option, ...) visualized under VS's native debugger.
                NatvisInstaller.EnsureInstalled(RustToolchain.FindNatvisFiles(sysroot));

                var entries = new List<(LaunchDescription Description, string ProjectPath)>();
                foreach (var package in metadata.Packages)
                {
                    var packageRoot = Path.GetDirectoryName(package.ManifestPath) ?? metadata.WorkspaceRoot;
                    foreach (var target in package.Targets)
                    {
                        LaunchTargetKind kind;
                        if (target.IsKind(CargoTargetKind.Bin))
                        {
                            kind = LaunchTargetKind.Bin;
                        }
                        else if (target.IsKind(CargoTargetKind.Example))
                        {
                            kind = LaunchTargetKind.Example;
                        }
                        else
                        {
                            continue;
                        }

                        var launchTarget = new LaunchTarget(
                            Package: package.Name,
                            Name: target.Name,
                            Kind: kind,
                            Profile: "dev",
                            TargetDir: metadata.TargetDirectory,
                            WorkspaceRoot: metadata.WorkspaceRoot,
                            PackageRoot: packageRoot);

                        var description = LaunchDescriptionBuilder.Build(launchTarget, sysroot, hostTriple, existingPath: existingPath);
                        var projectPath = MakeRelative(metadata.WorkspaceRoot, description.ExecutablePath);
                        entries.Add((description, projectPath));
                    }
                }

                var vsDirectory = Path.Combine(metadata.WorkspaceRoot, Kubuno.VisualStudio.Constants.VsHiddenFolderName);
                Directory.CreateDirectory(vsDirectory);
                var launchVsJsonPath = Path.Combine(vsDirectory, Kubuno.VisualStudio.Constants.LaunchVsJsonFileName);

                if (entries.Count == 0)
                {
                    KubunoLog.WriteLine("Kubuno: no bin/example Cargo targets found; not writing launch.vs.json.");
                    return;
                }

                File.WriteAllText(launchVsJsonPath, LaunchVsJsonWriter.WriteFile(entries));
                KubunoLog.WriteLine($"Kubuno: wrote {launchVsJsonPath} with {entries.Count} target(s): {string.Join(", ", entries.Select(e => e.Description.Name))}.");
            }
            catch (Exception exception)
            {
                // Best-effort: a failure here must never fail the build/workspace-open path that
                // triggered it (see call sites).
                KubunoLog.WriteException("Kubuno: failed to generate launch.vs.json", exception);
            }
        }

        private static string MakeRelative(string workspaceRoot, string path)
        {
            try
            {
                // CARGO_TARGET_DIR (and so the debuggee's executable) commonly lives on a
                // different drive than the workspace itself - e.g. the Kubuno desktop workspace
                // convention CLAUDE.md documents (CARGO_TARGET_DIR=C:\kubuno-build\desktop-target
                // regardless of which drive the source tree is opened from). Uri.MakeRelativeUri
                // between two "file" URIs on different drives does not error - it happily returns
                // a nonsensical "..\..\..\C:\..." path (both are the same *scheme*, just
                // different roots) - so the drive/root must be checked explicitly first.
                if (!string.Equals(Path.GetPathRoot(workspaceRoot), Path.GetPathRoot(path), StringComparison.OrdinalIgnoreCase))
                {
                    return path;
                }

                var workspaceUri = new Uri(workspaceRoot.TrimEnd('\\', '/') + Path.DirectorySeparatorChar);
                var pathUri = new Uri(path);

                var relative = Uri.UnescapeDataString(workspaceUri.MakeRelativeUri(pathUri).ToString());
                return relative.Replace('/', Path.DirectorySeparatorChar);
            }
            catch (Exception)
            {
                // Falling back to the absolute path is still a valid "project" value for
                // launch.vs.json (see LaunchVsJsonWriter's remarks) - just less tidy to read.
                return path;
            }
        }
    }
}
