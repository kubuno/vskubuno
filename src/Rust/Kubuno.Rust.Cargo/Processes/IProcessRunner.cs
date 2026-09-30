using System;
using System.Threading;
using System.Threading.Tasks;

namespace Kubuno.Rust.Cargo.Processes
{
    /// <summary>
    /// Abstraction over launching a process and streaming its output line by line. Kept
    /// separate from Cargo-specific code so the VSIX can inject its own implementation (e.g.
    /// one that writes to a VS output pane) and so tests can mock it without spawning a
    /// real "cargo" process.
    /// </summary>
    public interface IProcessRunner
    {
        /// <summary>
        /// Runs the process described by <paramref name="request"/> to completion.
        /// </summary>
        /// <param name="request">File name, argument string, working directory and environment.</param>
        /// <param name="onOutput">
        /// Optional sink for each stdout/stderr line as it is produced, in addition to the
        /// buffered lines returned in the final <see cref="ProcessRunResult"/>.
        /// </param>
        /// <param name="cancellationToken">
        /// When canceled, the process is killed and the task ends with
        /// <see cref="OperationCanceledException"/>.
        /// </param>
        Task<ProcessRunResult> RunAsync(
            ProcessRunRequest request,
            IProgress<ProcessOutputLine>? onOutput,
            CancellationToken cancellationToken);
    }
}
