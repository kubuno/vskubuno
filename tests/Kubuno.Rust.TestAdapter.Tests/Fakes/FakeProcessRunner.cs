using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Cargo.Processes;

namespace Kubuno.TestAdapter.Tests.Fakes
{
    /// <summary>
    /// A mock <see cref="IProcessRunner"/> that never spawns anything: a handler decides the
    /// <see cref="ProcessRunResult"/> for each request (typically by inspecting
    /// <see cref="ProcessRunRequest.FileName"/>/<see cref="ProcessRunRequest.Arguments"/> to tell
    /// a "cargo test --no-run" call apart from a "&lt;exe&gt; --list" or a test-run call), and every
    /// request seen is recorded for assertions. Mirrors
    /// tests/Kubuno.Cargo.Tests/Fakes/FakeProcessRunner.cs (that one is a different project,
    /// owned by another agent, and not referenced here - a small mock like this one is cheap
    /// enough to keep local rather than share).
    /// </summary>
    internal sealed class FakeProcessRunner : IProcessRunner
    {
        private readonly Func<ProcessRunRequest, ProcessRunResult> _handler;
        private readonly List<ProcessRunRequest> _requests = new List<ProcessRunRequest>();

        public FakeProcessRunner(Func<ProcessRunRequest, ProcessRunResult> handler)
        {
            _handler = handler;
        }

        public FakeProcessRunner(ProcessRunResult result)
            : this(_ => result)
        {
        }

        public IReadOnlyList<ProcessRunRequest> Requests => _requests;

        public Task<ProcessRunResult> RunAsync(
            ProcessRunRequest request,
            IProgress<ProcessOutputLine>? onOutput,
            CancellationToken cancellationToken)
        {
            _requests.Add(request);
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
