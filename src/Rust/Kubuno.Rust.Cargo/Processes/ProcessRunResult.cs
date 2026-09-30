using System.Collections.Generic;

namespace Kubuno.Rust.Cargo.Processes
{
    /// <summary>Final outcome of a completed process run, with its buffered output.</summary>
    public sealed class ProcessRunResult
    {
        public ProcessRunResult(int exitCode, IReadOnlyList<string> standardOutputLines, IReadOnlyList<string> standardErrorLines)
        {
            ExitCode = exitCode;
            StandardOutputLines = standardOutputLines;
            StandardErrorLines = standardErrorLines;
        }

        public int ExitCode { get; }

        public bool Succeeded => ExitCode == 0;

        public IReadOnlyList<string> StandardOutputLines { get; }

        public IReadOnlyList<string> StandardErrorLines { get; }
    }
}
