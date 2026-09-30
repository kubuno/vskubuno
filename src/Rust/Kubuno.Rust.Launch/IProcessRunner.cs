namespace Kubuno.Rust.Launch
{
    /// <summary>
    /// The outcome of running an external process to completion.
    /// </summary>
    public readonly struct ProcessRunResult
    {
        public ProcessRunResult(int exitCode, string standardOutput, string standardError)
        {
            ExitCode = exitCode;
            StandardOutput = standardOutput;
            StandardError = standardError;
        }

        public int ExitCode { get; }

        public string StandardOutput { get; }

        public string StandardError { get; }

        public bool Succeeded => ExitCode == 0;
    }

    /// <summary>
    /// Abstraction over launching an external process and waiting for it to exit, so that
    /// code needing to shell out (e.g. `rustc --print sysroot`) stays unit-testable without
    /// actually spawning a process. Kubuno.Rust.Launch is otherwise pure; this is its one
    /// deliberate seam onto the outside world.
    /// </summary>
    public interface IProcessRunner
    {
        /// <param name="fileName">The executable to run (resolved against PATH by the implementation).</param>
        /// <param name="arguments">The raw, already-escaped argument string.</param>
        /// <param name="workingDirectory">The working directory, or null to inherit the caller's.</param>
        ProcessRunResult Run(string fileName, string arguments, string? workingDirectory = null);
    }
}
