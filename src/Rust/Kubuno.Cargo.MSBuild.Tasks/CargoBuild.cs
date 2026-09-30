using System;
using System.IO;
using Kubuno.Rust.Cargo.Commands;
using Kubuno.Rust.Cargo.Diagnostics;
using Kubuno.Rust.Cargo.Metadata;
using Kubuno.Rust.Launch;
using Microsoft.Build.Framework;

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

        private string? _artifactExecutablePath;

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

            // Several bins can be produced by one `cargo build --workspace`; only remember the
            // one this task was actually asked to build (or the only one, when Bin is unset).
            if (string.IsNullOrEmpty(Bin) || string.Equals(artifact.Target.Name, Bin, StringComparison.Ordinal))
            {
                _artifactExecutablePath = artifact.Executable;
            }
        }

        protected override void OnCompleted(Kubuno.Rust.Cargo.Processes.ProcessRunResult result)
        {
            ExecutablePath = _artifactExecutablePath ?? ResolveConventionalExecutablePath();
        }

        private string ResolveConventionalExecutablePath()
        {
            string manifestDirectory = Path.GetDirectoryName(Path.GetFullPath(ManifestPath)) ?? ManifestPath;
            string targetDir = CargoLayout.ResolveTargetDir(manifestDirectory, TargetDir);
            string binName = !string.IsNullOrEmpty(Bin)
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
