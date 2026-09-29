using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Cargo.Commands;
using Kubuno.Cargo.Diagnostics;
using Kubuno.Cargo.Processes;

namespace Kubuno.Cargo.DesignSurface
{
    /// <summary>Outcome of <see cref="DesignSurfaceBuilder"/>.</summary>
    public enum DesignSurfaceBuildStatus
    {
        /// <summary>A surface linked against the project's own <c>kubuno_ui.dll</c> is ready.</summary>
        Ready,

        /// <summary>The project has not been built yet (no <c>kubuno_ui.dll</c> in its profile folder).</summary>
        NotBuilt,

        /// <summary>The project does not use <c>kubuno-views</c>, or its <c>kubuno-views</c> ships no surface.</summary>
        NotApplicable,

        Failed,

        Canceled,
    }

    /// <summary>A ready design surface: the exe and the folder holding it with its own copy of the project's <c>kubuno_ui.dll</c>.</summary>
    public sealed class DesignSurfaceBuild
    {
        public DesignSurfaceBuild(string exePath, string uiDllSha256, string uiDllSource)
        {
            ExePath = exePath;
            UiDllSha256 = uiDllSha256;
            UiDllSource = uiDllSource;
        }

        /// <summary>The project crate linked into the surface (EVT-7b: its controls render for real), null when it could not be.</summary>
        public string? ProjectCrate { get; set; }

        /// <summary>The registry the surface exported (<see cref="DesignSurfaceBuilder.RegistryFileName"/>), null when it did not.</summary>
        public string? RegistryPath => File.Exists(Path.Combine(Directory, DesignSurfaceBuilder.RegistryFileName)) ? Path.Combine(Directory, DesignSurfaceBuilder.RegistryFileName) : null;

        public string ExePath { get; }

        public string Directory => Path.GetDirectoryName(ExePath) ?? ExePath;

        /// <summary>SHA-256 of the <c>kubuno_ui.dll</c> the exe was linked against (upper-case hex).</summary>
        public string UiDllSha256 { get; }

        /// <summary>The project's <c>kubuno_ui.dll</c> that was copied next to the exe.</summary>
        public string UiDllSource { get; }
    }

    public sealed class DesignSurfaceBuildResult
    {
        public DesignSurfaceBuildResult(DesignSurfaceBuildStatus status, DesignSurfaceBuild? build, string message)
        {
            Status = status;
            Build = build;
            Message = message;
        }

        public DesignSurfaceBuildStatus Status { get; }

        public DesignSurfaceBuild? Build { get; }

        public string Message { get; }
    }

    /// <summary>
    /// The design-time build of the <c>.kbview</c> designer's surface (docs/DESIGNER.md section 15): the
    /// surface is compiled against the project's OWN dependency graph so that it links, and then loads,
    /// the very <c>kubuno_ui.dll</c> the project's application loads - a Rust dylib has no stable ABI.
    ///
    /// <para>Steps: (1) re-run the project's own cargo build (<see cref="CargoCommandFor"/>, identical to
    /// the SDK's, so it is a no-op right after a Visual Studio build and never rebuilds
    /// <c>kubuno_ui.dll</c> differently) and read its <c>compiler-artifact</c> messages; (2) compile
    /// <c>kubuno-views/examples/view_embed.rs</c> with <c>rustc</c> directly against those exact
    /// artifacts (<see cref="RustcArgumentsFor"/>: no cargo, so nothing in the project's target folder
    /// is rebuilt or overwritten - the surface needs no crate or <c>windows</c> feature the graph lacks);
    /// (3) put the exe, a byte-identical copy of the project's <c>kubuno_ui.dll</c> and the toolchain's
    /// <c>std-*.dll</c> in <c>&lt;target dir&gt;\kubuno-design\&lt;profile&gt;\&lt;key&gt;\</c>. A copy, the
    /// way the WinForms designer shadow-copies assemblies: a surface that loaded the project's own
    /// <c>deps\kubuno_ui.dll</c> would lock it, and the project's next build could not replace it.
    /// Outside the profile folder, so the SDK's <c>cargo clean --profile</c> never meets a file a running
    /// surface holds. The key hashes every input, so an unchanged project reuses the folder
    /// (<see cref="TryReuse"/>, no process at all).</para>
    /// </summary>
    public sealed class DesignSurfaceBuilder
    {
        public const string ExeName = "kubuno-design-surface.exe";
        public const string UiDllName = "kubuno_ui.dll";
        public const string DesignFolderName = "kubuno-design";
        public const string CurrentFileName = "current.json";
        /// <summary>The compile-time variable <c>view_embed.rs</c>'s <c>surfaceInfo</c> handshake embeds.</summary>
        public const string UiDllShaVariable = "KUBUNO_DESIGN_UI_DLL_SHA256";

        /// <summary>The compile-time variable naming the generated file that links the project crate (EVT-7b, <c>view_embed.rs</c>'s <c>mod project</c>).</summary>
        public const string ProjectIncludeVariable = "KUBUNO_DESIGN_PROJECT_RS";

        /// <summary>That generated file, in the design folder.</summary>
        public const string ProjectIncludeFileName = "project.rs";

        /// <summary>The registry the surface exports (<c>--export-registry</c>), saved next to it after a design build.</summary>
        public const string RegistryFileName = "registry.json";

        private readonly IProcessRunner _runner;

        public DesignSurfaceBuilder(IProcessRunner runner)
        {
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        }

        /// <summary><c>&lt;target dir&gt;\kubuno-design\&lt;profile&gt;</c>.</summary>
        public static string DesignDirectory(string targetDirectory, string profileDirectoryName) =>
            Path.Combine(targetDirectory, DesignFolderName, profileDirectoryName);

        /// <summary>Whether the project's profile folder holds a built <c>kubuno_ui.dll</c> (cheap, no process).</summary>
        public static bool IsProjectBuilt(DesignSurfaceProject project)
        {
            var profile = Path.Combine(project.EffectiveExpectedTargetDirectory, project.EffectiveProfileDirectoryName);
            return File.Exists(Path.Combine(profile, "deps", UiDllName)) || File.Exists(Path.Combine(profile, UiDllName));
        }

        /// <summary>
        /// The last design build of this project when none of its inputs changed since, without running
        /// anything; <see langword="null"/> otherwise (then <see cref="BuildAsync"/>).
        /// </summary>
        public static DesignSurfaceBuild? TryReuse(DesignSurfaceProject project)
        {
            var designDirectory = DesignDirectory(project.EffectiveExpectedTargetDirectory, project.EffectiveProfileDirectoryName);
            var stamp = ReadStamp(Path.Combine(designDirectory, CurrentFileName));
            if (stamp is null)
            {
                return null;
            }

            var folder = Path.Combine(designDirectory, stamp.Key);
            var exe = Path.Combine(folder, ExeName);
            return File.Exists(exe) && File.Exists(Path.Combine(folder, UiDllName)) && File.Exists(Path.Combine(folder, DesignSurfaceStamp.FileName))
                && stamp.InputsUnchanged(DesignSurfaceStamp.DescribeFile)
                ? new DesignSurfaceBuild(exe, stamp.UiDllSha256, stamp.UiDllSource) { ProjectCrate = stamp.ProjectCrate }
                : null;
        }

        /// <summary>
        /// Drops the last design build of <paramref name="project"/> (its <c>current.json</c> and folder, best
        /// effort) - after a surface failed the ABI handshake, so the next design build compiles afresh.
        /// </summary>
        public static void Forget(DesignSurfaceProject project)
        {
            var designDirectory = DesignDirectory(project.EffectiveExpectedTargetDirectory, project.EffectiveProfileDirectoryName);
            var current = Path.Combine(designDirectory, CurrentFileName);
            var stamp = ReadStamp(current);
            try
            {
                if (File.Exists(current))
                {
                    File.Delete(current);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A later design build overwrites it anyway.
            }

            if (stamp is not null)
            {
                TryDeleteDirectory(Path.Combine(designDirectory, stamp.Key));
            }
        }

        /// <summary>The project build's cargo command, argument for argument what <c>Kubuno.Rust.Sdk</c>'s <c>CargoBuild</c> task runs (same message format too: it reaches rustc's own arguments).</summary>
        public static CargoCommandLine CargoCommandFor(DesignSurfaceProject project)
        {
            var command = CargoCommand.Build();
            if (!string.IsNullOrEmpty(project.Bin))
            {
                command.WithTarget(CargoTargetSelector.Bin(project.Bin!));
            }

            command.WithManifestPath(project.ManifestPath);
            command.WithMessageFormat("json-diagnostic-rendered-ansi");
            if (!string.IsNullOrEmpty(project.Package))
            {
                command.WithPackage(project.Package!);
            }

            command.WithProfile(project.Profile);
            if (project.ExtraArgs.Count > 0)
            {
                command.WithExtraArgs(project.ExtraArgs.ToArray());
            }

            return command.ToCommandLine();
        }

        /// <summary>
        /// <c>rustc</c> arguments compiling the surface against <paramref name="inputs"/>: the three
        /// <c>kubuno_*</c> crates by exact path, everything else resolved in the project's <c>deps</c>
        /// folder by the crate hashes recorded in them; <c>-C prefer-dynamic</c> like every Kubuno app
        /// (one <c>std</c> shared with <c>kubuno_ui.dll</c>). Optimized for a release profile only.
        /// </summary>
        public static IReadOnlyList<string> RustcArgumentsFor(DesignSurfaceInputs inputs, string outputExe, string profile) =>
            RustcArgumentsFor(inputs, outputExe, profile, null, null);

        /// <summary>
        /// <see cref="RustcArgumentsFor(DesignSurfaceInputs, string, string)"/>, linking the project crate compiled
        /// at <paramref name="projectRlib"/> too (EVT-7b): <c>--cfg kubuno_design_project</c> turns on the surface's
        /// <c>mod project</c>, whose generated file (<see cref="ProjectIncludeVariable"/>) names the crate and its
        /// libraries.
        /// </summary>
        public static IReadOnlyList<string> RustcArgumentsFor(DesignSurfaceInputs inputs, string outputExe, string profile, DesignProjectCrate? project, string? projectRlib)
        {
            var args = BaseRustcArguments(inputs, outputExe, profile).ToList();
            if (project is null || projectRlib is null)
            {
                return args;
            }

            var output = args.Count - 2;
            var extra = new List<string> { "--cfg", "kubuno_design_project", "--extern", project.CrateName + "=" + projectRlib };
            foreach (var dependency in project.LinkedDependencies)
            {
                var path = project.Externs.First(e => e.Key == dependency).Value;
                extra.Add("--extern");
                extra.Add(dependency + "=" + path);
            }

            args.InsertRange(output, extra);
            return args;
        }

        private static IReadOnlyList<string> BaseRustcArguments(DesignSurfaceInputs inputs, string outputExe, string profile)
        {
            var args = new List<string>
            {
                "--edition", "2021",
                "--crate-name", "kubuno_design_surface",
                "--crate-type", "bin",
                inputs.SurfaceSource,
                "-C", "prefer-dynamic",
                "-C", string.Equals(profile, "release", StringComparison.Ordinal) ? "opt-level=3" : "opt-level=0",
                // Full debug info outside release (docs/DEBUGGING.md, "Debugging the design surface"): a custom control's
                // code runs in the surface, and a developer attaching the debugger to kubuno-design-surface.exe gets a PDB.
                "-C", string.Equals(profile, "release", StringComparison.Ordinal) ? "debuginfo=0" : "debuginfo=2",
                "--cap-lints", "allow",
                "-L", "dependency=" + inputs.DepsDirectory,
            };
            foreach (var external in inputs.Externs)
            {
                args.Add("--extern");
                args.Add(external.Key + "=" + external.Value);
            }

            args.Add("-o");
            args.Add(outputExe);
            return args;
        }

        /// <summary>The design folder's name: a hash of every input, the toolchain and the compile options.</summary>
        public static string ComputeKey(string uiDllSha256, IEnumerable<DesignSurfaceStampInput> inputs, string rustc, string profile)
        {
            var text = new StringBuilder()
                .Append(DesignSurfaceStamp.CurrentVersion).Append('\n')
                .Append(uiDllSha256).Append('\n')
                .Append(rustc).Append('\n')
                .Append(profile).Append('\n');
            foreach (var input in inputs)
            {
                text.Append(input.Path.ToUpperInvariant()).Append('|').Append(input.Length).Append('|').Append(input.LastWriteUtcTicks).Append('\n');
            }

            using var sha = SHA256.Create();
            return ToHex(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Substring(0, 16).ToLowerInvariant();
        }

        public static string Sha256OfFile(string path)
        {
            using var sha = SHA256.Create();
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return ToHex(sha.ComputeHash(stream));
        }

        /// <summary>
        /// Runs the design build (cargo, then rustc when the inputs changed). Cancellable: the running
        /// process is killed. <paramref name="log"/> receives every line cargo/rustc print (the Kubuno
        /// output pane). Never throws except <see cref="OperationCanceledException"/>, which is mapped to
        /// <see cref="DesignSurfaceBuildStatus.Canceled"/> too.
        /// </summary>
        public async Task<DesignSurfaceBuildResult> BuildAsync(DesignSurfaceProject project, IProgress<string>? log, CancellationToken cancellationToken)
        {
            try
            {
                return await BuildCoreAsync(project, log, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return new DesignSurfaceBuildResult(DesignSurfaceBuildStatus.Canceled, null, "design build canceled");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                log?.Report("[design build] " + ex.Message);
                return new DesignSurfaceBuildResult(DesignSurfaceBuildStatus.Failed, null, ex.Message);
            }
        }

        private async Task<DesignSurfaceBuildResult> BuildCoreAsync(DesignSurfaceProject project, IProgress<string>? log, CancellationToken cancellationToken)
        {
            // 1. The project's own cargo build: its artifacts ARE the graph the surface must link.
            var cargo = CargoCommandFor(project);
            log?.Report("[design build] " + cargo);
            var artifacts = new List<CargoArtifact>();
            var cargoRequest = new ProcessRunRequest(cargo.FileName, cargo.Arguments)
            {
                WorkingDirectory = project.ManifestDirectory,
                EnvironmentVariables = string.IsNullOrEmpty(project.TargetDirectory)
                    ? null
                    : new Dictionary<string, string> { ["CARGO_TARGET_DIR"] = project.TargetDirectory! },
            };
            var cargoProgress = new SynchronousProgress<ProcessOutputLine>(line =>
            {
                if (line.IsError)
                {
                    log?.Report(line.Text);
                    return;
                }

                switch (CargoMessageParser.Parse(line.Text, project.ManifestDirectory))
                {
                    case CargoArtifactEvent artifact:
                        lock (artifacts)
                        {
                            artifacts.Add(artifact.Artifact);
                        }

                        break;
                    case CargoDiagnosticEvent diagnostic when diagnostic.Diagnostic.Severity == CargoDiagnosticSeverity.Error:
                        log?.Report(AnsiText.Strip(diagnostic.Diagnostic.RenderedText ?? diagnostic.Diagnostic.Message).TrimEnd());
                        break;
                }
            });
            var cargoResult = await _runner.RunAsync(cargoRequest, cargoProgress, cancellationToken).ConfigureAwait(false);

            List<CargoArtifact> snapshot;
            lock (artifacts)
            {
                snapshot = artifacts.ToList();
            }

            var inputs = DesignSurfaceInputs.From(snapshot, File.Exists, out var reason);
            if (inputs is null)
            {
                // A failed build may still have produced every input (e.g. the application's own crate
                // failed, or its exe is locked by a running instance); only a missing input is fatal.
                if (!cargoResult.Succeeded)
                {
                    return new DesignSurfaceBuildResult(DesignSurfaceBuildStatus.Failed, null, $"the project's cargo build failed (exit code {cargoResult.ExitCode})");
                }

                return new DesignSurfaceBuildResult(DesignSurfaceBuildStatus.NotApplicable, null, reason ?? "no design surface for this project");
            }

            if (!cargoResult.Succeeded)
            {
                log?.Report("[design build] the project's cargo build failed, but kubuno-ui/kubuno-views were built: using them.");
            }

            cancellationToken.ThrowIfCancellationRequested();

            // 2. Toolchain: rustc as cargo runs it for this folder (rustup honours rust-toolchain.toml).
            var rustc = Environment.GetEnvironmentVariable("RUSTC");
            rustc = string.IsNullOrWhiteSpace(rustc) ? "rustc" : rustc!;
            var version = await RunSimpleAsync(rustc, "-vV", project.ManifestDirectory, cancellationToken).ConfigureAwait(false);
            var sysroot = (await RunSimpleAsync(rustc, "--print sysroot", project.ManifestDirectory, cancellationToken).ConfigureAwait(false)).Trim();
            if (version.Length == 0 || sysroot.Length == 0)
            {
                return new DesignSurfaceBuildResult(DesignSurfaceBuildStatus.Failed, null, "rustc could not be run");
            }

            // EVT-7b: the project's own crate (its controls), linked into the surface when it was built.
            var projectCrate = await ReadProjectCrateAsync(project, snapshot, log, cancellationToken).ConfigureAwait(false);

            var uiSha = Sha256OfFile(inputs.UiDll);
            var stampInputs = new[] { inputs.UiDll, inputs.ViewsRlib, inputs.ControlsRlib, inputs.SurfaceSource }
                .Concat(projectCrate is null ? Array.Empty<string>() : new[] { projectCrate.StampFile })
                .Select(path => DesignSurfaceStamp.DescribeFile(path) is { } d
                    ? new DesignSurfaceStampInput { Path = path, Length = d.Length, LastWriteUtcTicks = d.LastWriteUtcTicks }
                    : throw new IOException($"'{path}' disappeared during the design build"))
                .ToList();
            var versionLine = version.Split('\n')[0].Trim();
            var key = ComputeKey(uiSha, stampInputs, version, project.Profile);
            var designDirectory = DesignDirectory(inputs.TargetDirectory, Path.GetFileName(inputs.ProfileDirectory));
            var folder = Path.Combine(designDirectory, key);
            var stamp = new DesignSurfaceStamp { Key = key, UiDllSha256 = uiSha, UiDllSource = inputs.UiDll, Rustc = versionLine, Inputs = stampInputs };
            var exe = Path.Combine(folder, ExeName);

            if (File.Exists(Path.Combine(folder, DesignSurfaceStamp.FileName)) && File.Exists(exe)
                && File.Exists(Path.Combine(folder, UiDllName))
                && string.Equals(Sha256OfFile(Path.Combine(folder, UiDllName)), uiSha, StringComparison.Ordinal))
            {
                log?.Report($"[design build] up to date: {folder}");
                stamp.ProjectCrate = ReadStamp(Path.Combine(folder, DesignSurfaceStamp.FileName))?.ProjectCrate;
            }
            else
            {
                // 3. Compile into a scratch folder, then publish it in one move.
                Directory.CreateDirectory(designDirectory);
                var scratch = folder + ".tmp-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                Directory.CreateDirectory(scratch);
                try
                {
                    var scratchExe = Path.Combine(scratch, ExeName);
                    var surfaceEnvironment = new Dictionary<string, string> { [UiDllShaVariable] = uiSha };
                    string? projectRlib = null;
                    if (projectCrate is not null)
                    {
                        projectRlib = await CompileProjectCrateAsync(rustc, projectCrate, inputs, scratch, project.Profile, log, cancellationToken).ConfigureAwait(false);
                        if (projectRlib is not null)
                        {
                            var include = Path.Combine(scratch, ProjectIncludeFileName);
                            File.WriteAllText(include, projectCrate.SurfaceIncludeSource());
                            surfaceEnvironment[ProjectIncludeVariable] = include;
                            stamp.ProjectCrate = projectCrate.CrateName;
                        }
                    }

                    var rustcLine = new CargoCommandLine(rustc, RustcArgumentsFor(inputs, scratchExe, project.Profile, projectRlib is null ? null : projectCrate, projectRlib));
                    log?.Report("[design build] " + rustcLine);
                    var rustcRequest = new ProcessRunRequest(rustcLine.FileName, rustcLine.Arguments)
                    {
                        WorkingDirectory = project.ManifestDirectory,
                        EnvironmentVariables = surfaceEnvironment,
                    };
                    var rustcResult = await _runner.RunAsync(rustcRequest, new SynchronousProgress<ProcessOutputLine>(line => log?.Report(line.Text)), cancellationToken).ConfigureAwait(false);
                    if (!rustcResult.Succeeded || !File.Exists(scratchExe))
                    {
                        return new DesignSurfaceBuildResult(DesignSurfaceBuildStatus.Failed, null, $"rustc failed to compile the design surface (exit code {rustcResult.ExitCode})");
                    }

                    File.Copy(inputs.UiDll, Path.Combine(scratch, UiDllName), overwrite: true);
                    if (!string.Equals(Sha256OfFile(Path.Combine(scratch, UiDllName)), uiSha, StringComparison.Ordinal))
                    {
                        return new DesignSurfaceBuildResult(DesignSurfaceBuildStatus.Failed, null, "kubuno_ui.dll changed while the design surface was being built; build again");
                    }

                    var sysrootBin = Path.Combine(sysroot, "bin");
                    foreach (var std in Directory.Exists(sysrootBin) ? Directory.GetFiles(sysrootBin, "std-*.dll") : Array.Empty<string>())
                    {
                        File.Copy(std, Path.Combine(scratch, Path.GetFileName(std)), overwrite: true);
                    }

                    // The registry the surface knows (its linked project controls included): the Toolbox's project tab.
                    await ExportRegistryAsync(scratchExe, scratch, log, cancellationToken).ConfigureAwait(false);

                    File.WriteAllText(Path.Combine(scratch, DesignSurfaceStamp.FileName), stamp.ToJson());
                    if (Directory.Exists(folder))
                    {
                        Directory.Delete(folder, recursive: true);
                    }

                    Directory.Move(scratch, folder);
                }
                finally
                {
                    TryDeleteDirectory(scratch);
                }
            }

            File.WriteAllText(Path.Combine(designDirectory, CurrentFileName), stamp.ToJson());
            RemoveStaleFolders(designDirectory, key, log);
            log?.Report($"[design build] design surface ready: {exe} (kubuno_ui.dll {uiSha.Substring(0, 12)}...)");
            return new DesignSurfaceBuildResult(DesignSurfaceBuildStatus.Ready, new DesignSurfaceBuild(exe, uiSha, inputs.UiDll) { ProjectCrate = stamp.ProjectCrate }, "ready");
        }

        /// <summary>
        /// The project crate to link into the surface (EVT-7b), from <c>cargo metadata</c> (offline: the build just
        /// resolved everything) and this build's artifacts; null when it cannot be (logged) - the surface then
        /// renders the project's controls as placeholders.
        /// </summary>
        private async Task<DesignProjectCrate?> ReadProjectCrateAsync(DesignSurfaceProject project, IReadOnlyList<CargoArtifact> artifacts, IProgress<string>? log, CancellationToken cancellationToken)
        {
            try
            {
                var environment = string.IsNullOrEmpty(project.TargetDirectory) ? null : new Dictionary<string, string> { ["CARGO_TARGET_DIR"] = project.TargetDirectory! };
                var metadata = await new Metadata.CargoMetadataReader(_runner).ReadAsync(
                    project.ManifestDirectory,
                    project.ManifestPath,
                    Metadata.CargoMetadataReadOptions.IncludeDependencies | Metadata.CargoMetadataReadOptions.Offline,
                    environment,
                    cancellationToken).ConfigureAwait(false);
                var crate = DesignProjectCrate.From(metadata, artifacts, project.ManifestPath, project.Bin, out var reason);
                if (crate is null)
                {
                    log?.Report("[design build] the project's controls are not linked into the preview: " + reason);
                }

                return crate;
            }
            catch (Exception ex) when (ex is Metadata.CargoMetadataException or IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                log?.Report("[design build] the project's controls are not linked into the preview: cargo metadata failed (" + ex.Message + ")");
                return null;
            }
        }

        /// <summary>
        /// Compiles the project crate as an rlib into <paramref name="folder"/>; its path, or null when rustc failed
        /// (the project's code does not compile yet: logged, the surface is built without it).
        /// </summary>
        private async Task<string?> CompileProjectCrateAsync(string rustc, DesignProjectCrate crate, DesignSurfaceInputs inputs, string folder, string profile, IProgress<string>? log, CancellationToken cancellationToken)
        {
            var rlib = Path.Combine(folder, crate.RlibFileName);
            var line = new CargoCommandLine(rustc, crate.RustcArguments(inputs, rlib, profile));
            log?.Report("[design build] " + line);
            var request = new ProcessRunRequest(line.FileName, line.Arguments)
            {
                WorkingDirectory = crate.ManifestDirectory,
                EnvironmentVariables = new Dictionary<string, string>(crate.Environment().ToDictionary(kv => kv.Key, kv => kv.Value)),
            };
            var result = await _runner.RunAsync(request, new SynchronousProgress<ProcessOutputLine>(l => log?.Report(l.Text)), cancellationToken).ConfigureAwait(false);
            if (result.Succeeded && File.Exists(rlib))
            {
                return rlib;
            }

            log?.Report($"[design build] the project crate did not compile for the preview (exit code {result.ExitCode}): its controls show as placeholders.");
            return null;
        }

        /// <summary>Runs the fresh surface with <c>--export-registry</c> and saves what it prints (<see cref="RegistryFileName"/>); best-effort.</summary>
        private async Task ExportRegistryAsync(string exe, string folder, IProgress<string>? log, CancellationToken cancellationToken)
        {
            var result = await _runner.RunAsync(new ProcessRunRequest(exe, "--export-registry") { WorkingDirectory = folder }, null, cancellationToken).ConfigureAwait(false);
            var json = string.Join("\n", result.StandardOutputLines).Trim();
            if (result.Succeeded && json.StartsWith("{", StringComparison.Ordinal))
            {
                File.WriteAllText(Path.Combine(folder, RegistryFileName), json);
            }
            else
            {
                log?.Report($"[design build] the preview did not export its registry (exit code {result.ExitCode}).");
            }
        }

        private async Task<string> RunSimpleAsync(string fileName, string arguments, string workingDirectory, CancellationToken cancellationToken)
        {
            var result = await _runner.RunAsync(new ProcessRunRequest(fileName, arguments) { WorkingDirectory = workingDirectory }, null, cancellationToken).ConfigureAwait(false);
            return result.Succeeded ? string.Join("\n", result.StandardOutputLines) : string.Empty;
        }

        private static DesignSurfaceStamp? ReadStamp(string path)
        {
            try
            {
                return File.Exists(path) ? DesignSurfaceStamp.TryParse(File.ReadAllText(path)) : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>Older design folders (a running surface of another window may still hold one: skipped, removed next time).</summary>
        private static void RemoveStaleFolders(string designDirectory, string keep, IProgress<string>? log)
        {
            foreach (var directory in Directory.GetDirectories(designDirectory))
            {
                if (string.Equals(Path.GetFileName(directory), keep, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!TryDeleteDirectory(directory))
                {
                    log?.Report($"[design build] kept {directory} (in use)");
                }
            }
        }

        private static bool TryDeleteDirectory(string directory)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }

                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static string ToHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes)
            {
                builder.Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }

        /// <summary>An <see cref="IProgress{T}"/> that runs its callback on the reporting thread (no SynchronizationContext hop).</summary>
        private sealed class SynchronousProgress<T> : IProgress<T>
        {
            private readonly Action<T> _action;

            public SynchronousProgress(Action<T> action) => _action = action;

            public void Report(T value) => _action(value);
        }
    }
}
