using System;
using System.Threading;

namespace Kubuno.TestAdapter.Execution
{
    /// <summary>
    /// Bridges <see cref="Kubuno.Cargo.Processes.IProcessRunner"/> (async, line-buffered - what
    /// this project otherwise uses throughout, and what its tests fake) onto
    /// <see cref="Kubuno.Launch.IProcessRunner"/> (synchronous, whole-string output), the seam
    /// <see cref="Kubuno.Launch.RustToolchain"/> needs to run `rustc --print sysroot`/`rustc -vV`.
    /// The two libraries define their own, deliberately independent, process-runner
    /// abstractions (see Kubuno.Launch's own `IProcessRunner.cs` doc comment: "kept... so code
    /// needing to shell out... stays unit-testable... Kubuno.Launch is otherwise pure") - both
    /// are read-only references for this project, so this small adapter lives here rather than
    /// changing either.
    /// </summary>
    internal sealed class CargoBackedSyncProcessRunner : Kubuno.Launch.IProcessRunner
    {
        private readonly Kubuno.Cargo.Processes.IProcessRunner _inner;

        public CargoBackedSyncProcessRunner(Kubuno.Cargo.Processes.IProcessRunner inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public Kubuno.Launch.ProcessRunResult Run(string fileName, string arguments, string? workingDirectory = null)
        {
            var request = new Kubuno.Cargo.Processes.ProcessRunRequest(fileName, arguments)
            {
                WorkingDirectory = workingDirectory,
            };

            Kubuno.Cargo.Processes.ProcessRunResult result = _inner
                .RunAsync(request, onOutput: null, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            return new Kubuno.Launch.ProcessRunResult(
                result.ExitCode,
                string.Join(Environment.NewLine, result.StandardOutputLines),
                string.Join(Environment.NewLine, result.StandardErrorLines));
        }
    }
}
