using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Rust.Cargo.Metadata;
using Kubuno.Rust.Cargo.Processes;
using Kubuno.Rust.Launch;
using Kubuno.Rust.Logic;
using Kubuno.Shared.Logging;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Workspace;

namespace Kubuno.Rust.Debugging
{
    /// <summary>
    /// Generates <c>.vs\launch.vs.json</c> (phase 1c) from `cargo metadata`'s bin and example
    /// targets, using <c>Kubuno.Rust.Launch</c> end to end (<see cref="RustToolchain"/> for the
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
        /// <param name="workspace">
        /// The current Open Folder workspace, if known - used only for the best-effort
        /// <see cref="EnsureStartupItemSelectedAsync"/> step (real
        /// <c>IProjectConfigurationService</c> attempt); everything else this method does is
        /// workspace-independent. <see langword="null"/> is accepted (skips that step) so callers
        /// that do not have one handy do not need to special-case this method.
        /// </param>
        public static async Task GenerateAsync(string workingDirectory, string manifestPath, CancellationToken cancellationToken, IWorkspace? workspace = null)
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

                // Not a snapshot of this process's (devenv's) own PATH: launch.vs.json is read by
                // VS's own native debug engine, which supports `${env.VAR}` interpolation for a
                // `"type": "default"` configuration's `env` object - VAR is resolved against
                // whatever the debuggee would actually inherit at the moment it's launched, which
                // is more correct (and more robust to PATH changes between generating this file and
                // pressing F5) than baking in devenv's own PATH from generation time. Confirmed live
                // that embedding a literal snapshot here still left the launched exe unable to find
                // `kubuno_ui.dll`'s own `std-*.dll` dependency (STATUS_DLL_NOT_FOUND) - see
                // LaunchVsJsonWriter's own remarks for the other half of that bug (the `env` shape
                // itself, fixed independently of this).
                const string existingPath = "${env.PATH}";

                // Standard-library natvis is embedded in every PDB rustc links and Kubuno.natvis is a
                // VSIX asset; Just My Code and the step filters are per-user files (see RustDebuggerFiles).
                Kubuno.Rust.ProjectSystem.RustDebuggerSettings.EnsureDebuggerFilesInstalled(KubunoLog.WriteLine);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                Kubuno.Rust.ProjectSystem.RustDebuggerSettings.EnsurePanicExceptionSetting(KubunoLog.WriteLine);
                await TaskScheduler.Default;

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

                var vsDirectory = Path.Combine(metadata.WorkspaceRoot, Constants.VsHiddenFolderName);
                Directory.CreateDirectory(vsDirectory);
                var launchVsJsonPath = Path.Combine(vsDirectory, Constants.LaunchVsJsonFileName);

                if (entries.Count == 0)
                {
                    KubunoLog.WriteLine("Kubuno: no bin/example Cargo targets found; not writing launch.vs.json.");
                    return;
                }

                File.WriteAllText(launchVsJsonPath, LaunchVsJsonWriter.WriteFile(entries));
                KubunoLog.WriteLine($"Kubuno: wrote {launchVsJsonPath} with {entries.Count} target(s): {string.Join(", ", entries.Select(e => e.Description.Name))}.");

                // The package actually built/opened (not every package in a multi-package
                // workspace) is the only one whose default-run/bin-name fallback chain applies -
                // see StartupItemSelector's own remarks for why.
                var primaryPackage = metadata.Packages.FirstOrDefault(package =>
                    string.Equals(package.ManifestPath, manifestPath, StringComparison.OrdinalIgnoreCase));
                if (primaryPackage is not null)
                {
                    var defaultBinTarget = StartupItemSelector.SelectDefaultBinTarget(primaryPackage);
                    if (defaultBinTarget is not null)
                    {
                        await EnsureStartupItemSelectedAsync(workspace, vsDirectory, manifestPath, defaultBinTarget, cancellationToken).ConfigureAwait(false);
                    }
                }
                else
                {
                    // manifestPath is a virtual `[workspace]`-only manifest (opening the workspace
                    // root itself, e.g. this repo's own Z:\...\windows\Cargo.toml) - it never
                    // appears in `packages` above (it is not a package), so there is no "primary
                    // package" to ask SelectDefaultBinTarget about. Without this, F5 was never
                    // pre-selected at all for a workspace root - see
                    // StartupItemSelector.SelectDefaultBinTargetForWorkspace's own remarks for the
                    // fallback chain (workspace default-members, then the shell-directory
                    // convention, then "the first bin").
                    var workspaceDefault = StartupItemSelector.SelectDefaultBinTargetForWorkspace(metadata);
                    if (workspaceDefault is not null)
                    {
                        await EnsureStartupItemSelectedAsync(
                            workspace, vsDirectory, workspaceDefault.Value.Package.ManifestPath, workspaceDefault.Value.BinTarget, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception exception)
            {
                // Best-effort: a failure here must never fail the build/workspace-open path that
                // triggered it (see call sites).
                KubunoLog.WriteException("Kubuno: failed to generate launch.vs.json", exception);
            }
        }

        /// <summary>
        /// Pre-selects <paramref name="binTargetName"/> as Open Folder's "Select Startup Item" -
        /// so F5 works immediately after opening the folder, the same way CMake Tools/Makefile
        /// Open Folder support pre-select a target - but only when nothing has been selected yet
        /// (never overrides a choice the developer already made, including one made in a previous
        /// session and persisted).
        ///
        /// Two mechanisms, both best-effort and independent of each other:
        /// 1. <c>Microsoft.VisualStudio.Workspace.Debug.IProjectConfigurationService.SetCurrentProject</c>
        ///    (via <c>WorkspaceServiceHelper.GetProjectConfigurationServiceAsync</c>) - the
        ///    documented, supported API for this (the same "current project/target" surface
        ///    CMake Tools-style Open Folder providers use), found by reflecting over the real
        ///    <c>Microsoft.VisualStudio.Workspace.dll</c> assembly (no public sample of it exists
        ///    for a plain `"type": "default"` launch.vs.json-only setup - <c>ProjectTargetFileContext</c>'s
        ///    exact expected <c>FilePath</c> semantics for that case are the one thing this
        ///    couldn't be fully confirmed against a live instance in the time available, hence
        ///    mechanism 2 below as a verified fallback, not a replacement).
        /// 2. Writing <c>CurrentProjectSetting</c> into <c>.vs\ProjectSettings.json</c> directly -
        ///    the on-disk state behind the toolbar dropdown for this scenario (a Cargo folder with
        ///    no project system). Read-modify-write (not blind overwrite) so any other key VS
        ///    itself might add to this file is preserved.
        ///
        /// Known gap (see README's "Known limitations"): on a workspace-root open (this method's
        /// own caller, the `else` branch for a virtual `[workspace]` manifest), neither mechanism
        /// reliably makes the toolbar/`Debug.Start` reflect the write live - confirmed by hand that
        /// even editing `ProjectSettings.json` directly while the folder was already open, or
        /// changing window focus afterward, left `Debug.Start.IsAvailable` false and the toolbar
        /// still reading its own "Aucune configuration" placeholder until the developer opened the
        /// dropdown and picked the item themselves at least once. The file this method writes is
        /// correct (and is what the dropdown eventually shows once nudged), so F5 works after that
        /// one manual pick - it's only the very first, automatic pre-selection that doesn't take
        /// live effect for this scenario.
        /// </summary>
        private static async Task EnsureStartupItemSelectedAsync(
            IWorkspace? workspace, string vsDirectory, string manifestPath, string binTargetName, CancellationToken cancellationToken)
        {
            var projectSettingsPath = Path.Combine(vsDirectory, "ProjectSettings.json");

            JsonObject settings;
            try
            {
                settings = File.Exists(projectSettingsPath)
                    ? (JsonNode.Parse(File.ReadAllText(projectSettingsPath)) as JsonObject) ?? new JsonObject()
                    : new JsonObject();
            }
            catch (Exception)
            {
                // A corrupt/unexpected ProjectSettings.json is VS's own state, not this
                // extension's - start from empty rather than fail the whole launch-target
                // regeneration over it.
                settings = new JsonObject();
            }

            if (settings.TryGetPropertyValue("CurrentProjectSetting", out var existing) &&
                existing is JsonValue existingValue &&
                existingValue.TryGetValue(out string? existingName) &&
                !string.IsNullOrEmpty(existingName))
            {
                // Already chosen (by the developer, or by a previous run of this same method) -
                // never override it.
                return;
            }

            if (workspace is not null)
            {
                try
                {
                    var configurationService = await WorkspaceServiceHelper.GetProjectConfigurationServiceAsync(workspace).ConfigureAwait(false);
                    if (configurationService is not null)
                    {
                        await configurationService.SetCurrentProject(new ProjectTargetFileContext(manifestPath, binTargetName), binTargetName).ConfigureAwait(false);
                    }
                }
                catch (Exception exception)
                {
                    // Best-effort: mechanism 2 below is the one actually verified to move the
                    // toolbar's selection for this scenario.
                    KubunoLog.WriteException("Kubuno: IProjectConfigurationService.SetCurrentProject failed (falling back to ProjectSettings.json)", exception);
                }
            }

            try
            {
                settings["CurrentProjectSetting"] = binTargetName;
                File.WriteAllText(projectSettingsPath, settings.ToJsonString());
                KubunoLog.WriteLine($"Kubuno: pre-selected '{binTargetName}' as the Open Folder startup item ({projectSettingsPath}).");
            }
            catch (Exception exception)
            {
                KubunoLog.WriteException("Kubuno: failed to write ProjectSettings.json for startup item pre-selection", exception);
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
