using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Rust.Cargo.Commands;
using Kubuno.Rust.Cargo.Processes;

namespace Kubuno.Rust.TestAdapter.Execution
{
    /// <summary>Result of running a batch of tests from a single test executable.</summary>
    public sealed class CargoTestRunResult
    {
        public CargoTestRunResult(
            IReadOnlyDictionary<string, LibtestTestOutcome> outcomes,
            TimeSpan duration,
            string combinedOutput,
            int exitCode)
        {
            Outcomes = outcomes;
            Duration = duration;
            CombinedOutput = combinedOutput;
            ExitCode = exitCode;
        }

        public IReadOnlyDictionary<string, LibtestTestOutcome> Outcomes { get; }

        /// <summary>Total wall-clock time for the whole batch (libtest does not report per-test durations in text output - see <see cref="Execution.KubunoTestExecutor"/> for how this is distributed across the batch's TestResults).</summary>
        public TimeSpan Duration { get; }

        /// <summary>
        /// The process's stdout and stderr, concatenated: libtest's own "test &lt;name&gt; ..."
        /// lines and the "---- name stdout ----"/"failures:" summary go to stdout, but under
        /// `--nocapture` a panicking test's default panic hook writes straight to *stderr* (Rust's
        /// own default behavior - verified against real captures, see
        /// Fixtures/Execution/fixture-crate-run-fail.txt), so both streams are needed to recover
        /// the panic message. Always attached to every TestResult in the batch as a diagnostic
        /// message, regardless of parse success.
        /// </summary>
        public string CombinedOutput { get; }

        /// <summary>0 when every requested test passed or was ignored; 101 (libtest's own convention) when at least one failed.</summary>
        public int ExitCode { get; }
    }

    /// <summary>Runs a batch of tests from one test executable and parses the result.</summary>
    public static class CargoTestRunner
    {
        public static async Task<CargoTestRunResult> RunAsync(
            IProcessRunner processRunner,
            string executablePath,
            string workingDirectory,
            IReadOnlyList<string> libtestNames,
            CancellationToken cancellationToken)
        {
            if (processRunner is null)
            {
                throw new ArgumentNullException(nameof(processRunner));
            }
            if (libtestNames is null || libtestNames.Count == 0)
            {
                throw new ArgumentException("At least one test name must be requested.", nameof(libtestNames));
            }

            var args = new List<string>(libtestNames.Count + 3);
            args.AddRange(libtestNames);
            // libtest supports multiple positional filters (OR'd together) - confirmed against
            // `--help` and by running a batch of two exact names in one invocation - so every
            // test selected from this executable is requested in a single process launch.
            args.Add("--exact");
            args.Add("--nocapture");
            args.Add("--test-threads=1"); // deterministic, single-threaded: see TestLaunchArgs' own doc comment in Kubuno.Rust.Launch for the same rationale (debugging story), which applies equally to output attribution here.

            var commandLine = new CargoCommandLine(executablePath, args);
            var request = new ProcessRunRequest(commandLine.FileName, commandLine.Arguments)
            {
                WorkingDirectory = workingDirectory,
            };

            var stopwatch = Stopwatch.StartNew();
            ProcessRunResult processResult = await processRunner
                .RunAsync(request, onOutput: null, cancellationToken)
                .ConfigureAwait(false);
            stopwatch.Stop();

            string combinedOutput = string.Join(Environment.NewLine, processResult.StandardOutputLines);
            if (processResult.StandardErrorLines.Count > 0)
            {
                combinedOutput += Environment.NewLine + string.Join(Environment.NewLine, processResult.StandardErrorLines);
            }

            var outcomes = LibtestOutputParser.Parse(combinedOutput, libtestNames);
            return new CargoTestRunResult(outcomes, stopwatch.Elapsed, combinedOutput, processResult.ExitCode);
        }
    }
}
