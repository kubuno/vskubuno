using System;
using System.Collections.Generic;
using System.IO;
using Kubuno.Rust.Cargo.Commands;
using Kubuno.Rust.Cargo.Diagnostics;
using Kubuno.Rust.Cargo.Metadata;
using Kubuno.Rust.Launch;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Kubuno.Cargo.MSBuild.Tasks
{
    /// <summary>
    /// <c>cargo build</c>, driving <see cref="CoreCompile"/> in <c>Kubuno.Rust.Sdk</c>'s
    /// <c>Sdk.targets</c> (docs/RSPROJ.md §2).
    /// </summary>
    public sealed class CargoBuild : CargoBuildTaskBase
    {
        /// <summary>Restricts the build to one <c>[[bin]]</c> target, emitted as <c>--bin &lt;value&gt;</c>.</summary>
        public string? Bin { get; set; }

        /// <summary>Emits <c>--workspace</c> when true.</summary>
        public bool Workspace { get; set; }

        /// <summary>
        /// The <c>[[bin]]</c> whose artifact <see cref="ExecutablePath"/> reports, without restricting what cargo builds
        /// (unlike <see cref="Bin"/>): for a <c>--workspace</c> build, which produces every member's executables at once.
        /// Defaults to <see cref="Bin"/>.
        /// </summary>
        public string? ExecutableBin { get; set; }

        /// <summary>
        /// Linker arguments for the executable only (the SDK's generated Win32 <c>.res</c> file). When set, the build
        /// runs <c>cargo rustc --bin &lt;Bin&gt; ... -- -C link-arg=&lt;arg&gt;</c> instead of <c>cargo build</c>: <c>cargo rustc</c>
        /// passes extra rustc arguments to the final crate only, so dependencies keep their cache. Requires <see cref="Bin"/>.
        /// </summary>
        public string[] LinkArgs { get; set; } = Array.Empty<string>();

        /// <summary>
        /// The built executable's absolute path once <see cref="CargoBuildTaskBase.Execute"/>
        /// returns — the authoritative <c>compiler-artifact</c> path when cargo actually rebuilt
        /// the target, otherwise a path-convention fallback (<see cref="ExecutableResolver"/>) for
        /// an incremental, already-fresh build. This is what <c>Sdk.targets</c> assigns to
        /// <c>$(TargetPath)</c>.
        /// </summary>
        [Output]
        public string? ExecutablePath { get; set; }

        /// <summary>
        /// Every executable of a <c>[[bin]]</c> target this build produced or found fresh (cargo reports both), with the
        /// target's name as <c>BinName</c> metadata: what a <c>--workspace</c> build hands back to each project of the
        /// workspace so that it can pick its own executable.
        /// </summary>
        [Output]
        public ITaskItem[] Executables { get; set; } = Array.Empty<ITaskItem>();

        /// <summary>
        /// Hands the errors and warnings back as <see cref="Diagnostics"/> instead of logging them, and never fails the
        /// task because of them (<see cref="Succeeded"/> tells how cargo ended). A workspace-wide build runs in a project
        /// of its own that is not part of the solution, whose errors Visual Studio would show in the Output window only:
        /// each project of the workspace logs the diagnostics of its own files instead (<see cref="KubunoWorkspaceDiagnostics"/>).
        /// The rendered text of every diagnostic is still logged, as a message.
        /// </summary>
        public bool DiagnosticsAsItems { get; set; }

        /// <summary>
        /// With <see cref="DiagnosticsAsItems"/>: one item per error or warning (metadata <c>Severity</c> = <c>Error</c> or
        /// <c>Warning</c>, <c>Code</c>, <c>File</c>, <c>Line</c>, <c>Column</c>, <c>Message</c>), plus, when cargo failed without
        /// any error diagnostic, one <c>Error</c> item without a file per line cargo wrote to its error output.
        /// </summary>
        [Output]
        public ITaskItem[] Diagnostics { get; set; } = Array.Empty<ITaskItem>();

        /// <summary>Whether cargo exited successfully.</summary>
        [Output]
        public bool Succeeded { get; set; }

        private string? _artifactExecutablePath;
        private readonly List<ITaskItem> _executables = new();
        private readonly List<ITaskItem> _diagnostics = new();
        private readonly HashSet<string> _diagnosticKeys = new(StringComparer.Ordinal);

        protected override void LogBuildEvent(CargoBuildEvent buildEvent)
        {
            if (!DiagnosticsAsItems || buildEvent is not CargoDiagnosticEvent diagnosticEvent)
            {
                base.LogBuildEvent(buildEvent);
                return;
            }

            CargoDiagnostic diagnostic = diagnosticEvent.Diagnostic;
            string? severity = diagnostic.Severity switch
            {
                CargoDiagnosticSeverity.Error or CargoDiagnosticSeverity.InternalCompilerError => "Error",
                CargoDiagnosticSeverity.Warning => "Warning",
                _ => null,
            };
            if (severity is null)
            {
                base.LogBuildEvent(buildEvent);
                return;
            }

            // The same diagnostic can be reported for several units of one crate (a lib and its bin, say): once is enough.
            string key = $"{severity}|{diagnostic.Code}|{diagnostic.FilePath}|{diagnostic.Line}|{diagnostic.Column}|{diagnostic.Message}";
            if (_diagnosticKeys.Add(key))
            {
                _diagnostics.Add(DiagnosticItem(severity, diagnostic.Code, diagnostic.FilePath, diagnostic.Line ?? 0, diagnostic.Column ?? 0, diagnostic.Message));
            }

            if (!string.IsNullOrEmpty(diagnostic.RenderedText))
            {
                Log.LogMessage(MessageImportance.Normal, diagnostic.RenderedText);
            }
        }

        protected override void OnFailedWithoutErrors(Kubuno.Rust.Cargo.Processes.ProcessRunResult result)
        {
            if (!DiagnosticsAsItems)
            {
                base.OnFailedWithoutErrors(result);
                return;
            }

            if (_diagnostics.Exists(item => item.GetMetadata("Severity") == "Error"))
            {
                return;
            }

            foreach (string errorLine in result.StandardErrorLines)
            {
                if (!string.IsNullOrWhiteSpace(errorLine))
                {
                    _diagnostics.Add(DiagnosticItem("Error", null, null, 0, 0, errorLine));
                }
            }

            if (!_diagnostics.Exists(item => item.GetMetadata("Severity") == "Error"))
            {
                _diagnostics.Add(DiagnosticItem("Error", null, null, 0, 0, $"cargo exited with code {result.ExitCode}."));
            }
        }

        private ITaskItem DiagnosticItem(string severity, string? code, string? file, int line, int column, string message)
        {
            var item = new TaskItem("diagnostic" + _diagnostics.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            item.SetMetadata("Severity", severity);
            item.SetMetadata("Code", code ?? string.Empty);
            item.SetMetadata("File", file ?? string.Empty);
            item.SetMetadata("Line", line.ToString(System.Globalization.CultureInfo.InvariantCulture));
            item.SetMetadata("Column", column.ToString(System.Globalization.CultureInfo.InvariantCulture));
            item.SetMetadata("Message", message);
            return item;
        }

        protected override CargoCommand CreateCommand()
        {
            bool linkArgs = LinkArgs.Length > 0 && !string.IsNullOrEmpty(Bin);
            CargoCommand command = linkArgs ? CargoCommand.Rustc() : CargoCommand.Build();
            if (Workspace)
            {
                command.WithWorkspace();
            }
            if (!string.IsNullOrEmpty(Bin))
            {
                command.WithTarget(CargoTargetSelector.Bin(Bin!));
            }
            if (linkArgs)
            {
                foreach (string arg in LinkArgs)
                {
                    command.WithRustcArgs("-C", "link-arg=" + arg);
                }
            }
            return command;
        }

        protected override void OnBuildEvent(CargoBuildEvent buildEvent)
        {
            if (buildEvent is not CargoArtifactEvent artifactEvent)
            {
                return;
            }

            CargoArtifact artifact = artifactEvent.Artifact;
            if (artifact.Executable is null || !artifact.Target.IsKind(CargoTargetKind.Bin))
            {
                return;
            }

            var item = new TaskItem(artifact.Executable);
            item.SetMetadata("BinName", artifact.Target.Name);
            _executables.Add(item);

            // Several bins can be produced by one `cargo build --workspace`; only remember the
            // one this task was asked for (or the only one, when no bin is named).
            string? wanted = string.IsNullOrEmpty(ExecutableBin) ? Bin : ExecutableBin;
            if (string.IsNullOrEmpty(wanted) || string.Equals(artifact.Target.Name, wanted, StringComparison.Ordinal))
            {
                _artifactExecutablePath = artifact.Executable;
            }
        }

        protected override void OnCompleted(Kubuno.Rust.Cargo.Processes.ProcessRunResult result)
        {
            Succeeded = result.Succeeded;
            if (DiagnosticsAsItems && !result.Succeeded)
            {
                OnFailedWithoutErrors(result);
            }

            Diagnostics = _diagnostics.ToArray();
            Executables = _executables.ToArray();
            ExecutablePath = _artifactExecutablePath ?? ResolveConventionalExecutablePath();
        }

        private string ResolveConventionalExecutablePath()
        {
            string manifestDirectory = Path.GetDirectoryName(Path.GetFullPath(ManifestPath)) ?? ManifestPath;
            // A workspace member's default target directory is the workspace root's, not its own.
            string targetDir = CargoLayout.ResolveTargetDir(string.IsNullOrEmpty(WorkspaceRoot) ? manifestDirectory : WorkspaceRoot!, TargetDir);
            string binName = !string.IsNullOrEmpty(ExecutableBin)
                ? ExecutableBin!
                : !string.IsNullOrEmpty(Bin)
                ? Bin!
                : !string.IsNullOrEmpty(Package)
                    ? Package!
                    : new DirectoryInfo(manifestDirectory).Name;

            var target = new LaunchTarget(
                Package: Package ?? binName,
                Name: binName,
                Kind: LaunchTargetKind.Bin,
                Profile: string.IsNullOrEmpty(Profile) ? "debug" : Profile!,
                TargetDir: targetDir,
                WorkspaceRoot: manifestDirectory,
                TargetTriple: string.IsNullOrEmpty(TargetTriple) ? null : TargetTriple);

            return ExecutableResolver.Resolve(target);
        }
    }
}
