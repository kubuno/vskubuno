using System;
using System.Collections.Generic;
using Kubuno.Rust.Launch;

namespace Kubuno.Rust.Launch.Tests
{
    /// <summary>
    /// A scripted <see cref="IProcessRunner"/> for unit tests: never spawns a real process.
    /// </summary>
    internal sealed class FakeProcessRunner : IProcessRunner
    {
        private readonly Queue<ProcessRunResult> _scriptedResults = new();
        public List<(string FileName, string Arguments, string? WorkingDirectory)> Calls { get; } = new();

        public FakeProcessRunner Enqueue(int exitCode, string standardOutput, string standardError = "")
        {
            _scriptedResults.Enqueue(new ProcessRunResult(exitCode, standardOutput, standardError));
            return this;
        }

        public ProcessRunResult Run(string fileName, string arguments, string? workingDirectory = null)
        {
            Calls.Add((fileName, arguments, workingDirectory));

            if (_scriptedResults.Count == 0)
            {
                throw new InvalidOperationException("FakeProcessRunner.Run called with no scripted result queued.");
            }

            return _scriptedResults.Dequeue();
        }
    }
}
