using System;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Rust.Cargo.Processes;

namespace Kubuno.Rust.Cargo.Tests.Fakes
{
    /// <summary>
    /// A mock <see cref="IProcessRunner"/> that never spawns anything: it hands back a
    /// canned <see cref="ProcessRunResult"/> and records the request it was given, so tests
    /// can assert on the exact command line / working directory / environment a caller built
    /// without needing "cargo" (or anything else) to actually be on PATH.
    /// </summary>
    internal sealed class FakeProcessRunner : IProcessRunner
    {
        private readonly Func<ProcessRunRequest, ProcessRunResult> _handler;

        public FakeProcessRunner(Func<ProcessRunRequest, ProcessRunResult> handler)
        {
            _handler = handler;
        }

        public FakeProcessRunner(ProcessRunResult result)
            : this(_ => result)
        {
        }

        /// <summary>The request passed to the most recent <see cref="RunAsync"/> call.</summary>
        public ProcessRunRequest? LastRequest { get; private set; }

        public Task<ProcessRunResult> RunAsync(
            ProcessRunRequest request,
            IProgress<ProcessOutputLine>? onOutput,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            ProcessRunResult result = _handler(request);

            if (onOutput is not null)
            {
                foreach (string line in result.StandardOutputLines)
                {
                    onOutput.Report(new ProcessOutputLine(line, isError: false));
                }
                foreach (string line in result.StandardErrorLines)
                {
                    onOutput.Report(new ProcessOutputLine(line, isError: true));
                }
            }

            return Task.FromResult(result);
        }
    }
}
