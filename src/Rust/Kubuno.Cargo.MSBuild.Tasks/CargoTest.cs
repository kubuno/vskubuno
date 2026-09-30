using System;
using System.Collections.Generic;
using Kubuno.Rust.Cargo.Commands;
using Kubuno.Rust.Cargo.Diagnostics;
using Kubuno.Rust.Cargo.Metadata;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Kubuno.Cargo.MSBuild.Tasks
{
    /// <summary>
    /// <c>cargo test</c> (or, with <see cref="NoRun"/>, <c>cargo test --no-run</c>: build the test
    /// binaries without executing them, matching <c>cargo test --no-run --message-format=json</c>
    /// as already used to find a test binary's hash-suffixed path for the native debugger — see
    /// <c>Kubuno.Rust.Launch.ExecutableResolver</c>'s own remarks).
    /// </summary>
    public sealed class CargoTest : CargoBuildTaskBase
    {
        public bool NoRun { get; set; }

        public bool Workspace { get; set; }

        /// <summary>
        /// One item per <c>compiler-artifact</c> test binary produced, with the absolute
        /// executable path as the item spec and a <c>TargetName</c> metadata (the test target's
        /// own name, e.g. the integration test file's module name) — the authoritative source for
        /// a test binary's hash-suffixed on-disk name.
        /// </summary>
        [Output]
        public ITaskItem[] TestBinaries { get; set; } = Array.Empty<ITaskItem>();

        private readonly List<ITaskItem> _testBinaries = new List<ITaskItem>();

        protected override CargoCommand CreateCommand()
        {
            CargoCommand command = CargoCommand.Test();
            if (Workspace)
            {
                command.WithWorkspace();
            }
            if (NoRun)
            {
                command.WithExtraArgs("--no-run");
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
            if (artifact.Executable is null || !artifact.Target.IsKind(CargoTargetKind.Test))
            {
                return;
            }

            var item = new TaskItem(artifact.Executable);
            item.SetMetadata("TargetName", artifact.Target.Name);
            _testBinaries.Add(item);
        }

        protected override void OnCompleted(Kubuno.Rust.Cargo.Processes.ProcessRunResult result)
        {
            TestBinaries = _testBinaries.ToArray();
        }
    }
}
