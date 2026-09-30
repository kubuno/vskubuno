using System;
using System.Threading;

namespace Kubuno.Rust.TestAdapter.Execution
{
    /// <summary>
    /// Bridges <see cref="Kubuno.Rust.Cargo.Processes.IProcessRunner"/> (async, line-buffered - what
    /// this project otherwise uses throughout, and what its tests fake) onto
    /// <see cref="Kubuno.Rust.Launch.IProcessRunner"/> (synchronous, whole-string output), the seam
    /// <see cref="Kubuno.Rust.Launch.RustToolchain"/> needs to run `rustc --print sysroot`/`rustc -vV`.
    /// The two libraries define their own, deliberately independent, process-runner
    /// abstractions (see Kubuno.Rust.Launch's own `IProcessRunner.cs` doc comment: "kept... so code
    /// needing to shell out... stays unit-testable... Kubuno.Rust.Launch is otherwise pure") - both
    /// are read-only references for this project, so this small adapter lives here rather than
    /// changing either.
    /// </summary>
    internal sealed class CargoBackedSyncProcessRunner : Kubuno.Rust.Launch.IProcessRunner
    {
        private readonly Kubuno.Rust.Cargo.Processes.IProcessRunner _inner;

        public CargoBackedSyncProcessRunner(Kubuno.Rust.Cargo.Processes.IProcessRunner inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public Kubuno.Rust.Launch.ProcessRunResult Run(string fileName, string arguments, string? workingDirectory = null)
        {
            var request = new Kubuno.Rust.Cargo.Processes.ProcessRunRequest(fileName, arguments)
            {
                WorkingDirectory = workingDirectory,
            };

            Kubuno.Rust.Cargo.Processes.ProcessRunResult result = _inner
                .RunAsync(request, onOutput: null, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            return new Kubuno.Rust.Launch.ProcessRunResult(
                result.ExitCode,
                string.Join(Environment.NewLine, result.StandardOutputLines),
                string.Join(Environment.NewLine, result.StandardErrorLines));
        }
    }
}
