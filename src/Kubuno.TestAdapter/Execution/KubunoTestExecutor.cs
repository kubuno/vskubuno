using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Cargo.Processes;
using Kubuno.TestAdapter.Discovery;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;

namespace Kubuno.TestAdapter.Execution
{
    /// <summary>
    /// VSTest test executor: groups the requested <see cref="TestCase"/>s by the executable that
    /// contains them (<see cref="KubunoTestProperties.ExecutablePath"/>) and runs each group as
    /// one batched libtest invocation (see <see cref="CargoTestRunner"/>) - except while
    /// debugging, where each test is launched individually (see <see cref="RunGroupAsync"/>).
    /// </summary>
    [ExtensionUri(KubunoTestAdapterConstants.ExecutorUriString)]
    public sealed class KubunoTestExecutor : ITestExecutor
    {
        public static readonly Uri ExecutorUri = new Uri(KubunoTestAdapterConstants.ExecutorUriString);

        private readonly IProcessRunner _processRunner;
        private volatile CancellationTokenSource? _cancellationTokenSource;

        static KubunoTestExecutor() => AssemblyResolution.EnsureInstalled();

        public KubunoTestExecutor()
            : this(new ProcessRunner())
        {
        }

        public KubunoTestExecutor(IProcessRunner processRunner)
        {
            _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        }

        public void RunTests(IEnumerable<TestCase>? tests, IRunContext? runContext, IFrameworkHandle? frameworkHandle)
        {
            if (tests is null || frameworkHandle is null)
            {
                return;
            }

            var cts = new CancellationTokenSource();
            _cancellationTokenSource = cts;
            try
            {
                var groups = tests
                    .GroupBy(testCase => testCase.GetPropertyValue(KubunoTestProperties.ExecutablePath, string.Empty))
                    .Where(group => group.Key.Length > 0);

                foreach (var group in groups)
                {
                    if (cts.IsCancellationRequested)
                    {
                        break;
                    }
                    RunGroupAsync(group.Key, group.ToList(), runContext, frameworkHandle, cts.Token)
                        .GetAwaiter()
                        .GetResult();
                }
            }
            finally
            {
                _cancellationTokenSource = null;
            }
        }

        public void RunTests(IEnumerable<string>? sources, IRunContext? runContext, IFrameworkHandle? frameworkHandle)
        {
            // A source-only run (VSTest gives us Cargo.toml paths, not TestCases) happens when
            // the caller didn't already discover tests first - e.g. `vstest.console.exe
            // <Cargo.toml>` from the command line. Test Explorer itself always already knows the
            // TestCases (populated via the container discoverer - see Containers/) and calls the
            // TestCase overload above instead, but supporting this overload too costs little:
            // re-run discovery synchronously, then delegate.
            if (sources is null || frameworkHandle is null)
            {
                return;
            }

            var sink = new BufferingTestCaseSink();
            var discoverer = new KubunoTestDiscoverer(_processRunner);
            discoverer.DiscoverTests(sources, discoveryContext: null!, logger: frameworkHandle, sink);
            RunTests(sink.TestCases, runContext, frameworkHandle);
        }

        public void Cancel() => _cancellationTokenSource?.Cancel();

        private async Task RunGroupAsync(
            string executablePath,
            List<TestCase> group,
            IRunContext? runContext,
            IFrameworkHandle frameworkHandle,
            CancellationToken cancellationToken)
        {
            string workingDirectory = group[0].GetPropertyValue(KubunoTestProperties.WorkingDirectory, string.Empty);

            if (runContext?.IsBeingDebugged == true)
            {
                // Debugging never batches: a debugger session tells one story (breakpoints hit,
                // one call stack at a time), so each selected test gets its own
                // LaunchProcessWithDebuggerAttached call, run one after another. Unlike the
                // normal (non-debug) path below, no output is captured here - VS itself owns the
                // debuggee's console once attached - so only Duration and a Passed/Failed
                // exit-code verdict are reported; see DebugTestLauncher's remarks.
                foreach (TestCase testCase in group)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                    await RunUnderDebuggerAsync(executablePath, workingDirectory, testCase, frameworkHandle, cancellationToken).ConfigureAwait(false);
                }
                return;
            }

            foreach (TestCase testCase in group)
            {
                frameworkHandle.RecordStart(testCase);
            }

            var byLibtestName = group.ToDictionary(
                testCase => testCase.GetPropertyValue(KubunoTestProperties.LibtestName, testCase.FullyQualifiedName));
            var libtestNames = byLibtestName.Keys.ToList();

            CargoTestRunResult runResult;
            try
            {
                runResult = await CargoTestRunner
                    .RunAsync(_processRunner, executablePath, workingDirectory, libtestNames, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                foreach (TestCase testCase in group)
                {
                    var canceledResult = new TestResult(testCase) { Outcome = TestOutcome.None };
                    frameworkHandle.RecordEnd(testCase, TestOutcome.None);
                    frameworkHandle.RecordResult(canceledResult);
                }
                return;
            }
            catch (Exception ex)
            {
                foreach (TestCase testCase in group)
                {
                    var errorResult = new TestResult(testCase)
                    {
                        Outcome = TestOutcome.Failed,
                        ErrorMessage = $"Kubuno: could not run '{executablePath}': {ex.Message}",
                    };
                    frameworkHandle.RecordResult(errorResult);
                    frameworkHandle.RecordEnd(testCase, TestOutcome.Failed);
                }
                return;
            }

            // libtest's text output reports one total duration for the whole run, never a
            // per-test figure (verified against every captured fixture - see
            // Fixtures/Execution/*.txt): splitting the batch's wall-clock time evenly across its
            // tests is an approximation, documented on CargoTestRunResult.Duration, not a real
            // per-test measurement.
            TimeSpan perTestDuration = libtestNames.Count > 0
                ? TimeSpan.FromTicks(runResult.Duration.Ticks / libtestNames.Count)
                : TimeSpan.Zero;

            foreach (TestCase testCase in group)
            {
                string libtestName = testCase.GetPropertyValue(KubunoTestProperties.LibtestName, testCase.FullyQualifiedName);
                TestResult result = BuildResult(testCase, libtestName, runResult, perTestDuration);
                frameworkHandle.RecordResult(result);
                frameworkHandle.RecordEnd(testCase, result.Outcome);
            }
        }

        private static TestResult BuildResult(TestCase testCase, string libtestName, CargoTestRunResult runResult, TimeSpan duration)
        {
            var result = new TestResult(testCase) { Duration = duration };

            if (runResult.Outcomes.TryGetValue(libtestName, out LibtestTestOutcome? outcome))
            {
                result.Outcome = outcome.Verdict switch
                {
                    LibtestVerdict.Passed => TestOutcome.Passed,
                    LibtestVerdict.Ignored => TestOutcome.Skipped,
                    LibtestVerdict.Failed => TestOutcome.Failed,
                    _ => TestOutcome.None,
                };

                if (outcome.Verdict == LibtestVerdict.Failed)
                {
                    result.ErrorMessage = outcome.ErrorMessage ?? "Test failed (see output).";
                    result.ErrorStackTrace = outcome.ErrorLocation is null ? null : $"   at {libtestName} in {outcome.ErrorLocation}";
                }
            }
            else
            {
                // Requested but absent from the parsed outcomes - should not happen (every
                // requested name is always assigned Passed/Failed/Ignored by
                // LibtestOutputParser), kept only as defense in depth.
                result.Outcome = TestOutcome.None;
                result.ErrorMessage = "Kubuno: no result reported for this test - see the raw output message attached to this result.";
            }

            result.Messages.Add(new TestResultMessage(TestResultMessage.StandardOutCategory, runResult.CombinedOutput));
            return result;
        }

        private async Task RunUnderDebuggerAsync(
            string executablePath,
            string workingDirectory,
            TestCase testCase,
            IFrameworkHandle frameworkHandle,
            CancellationToken cancellationToken)
        {
            frameworkHandle.RecordStart(testCase);

            string libtestName = testCase.GetPropertyValue(KubunoTestProperties.LibtestName, testCase.FullyQualifiedName);
            string targetDirectory = testCase.GetPropertyValue(KubunoTestProperties.TargetDirectory, string.Empty);

            TestResult result;
            try
            {
                Kubuno.Launch.LaunchDescription description = await Task.Run(
                    () => DebugTestLauncher.Build(
                        _processRunner,
                        executablePath,
                        workingDirectory,
                        targetDirectory,
                        libtestName,
                        existingPath: Environment.GetEnvironmentVariable("PATH")),
                    cancellationToken).ConfigureAwait(false);

                var commandLine = new Kubuno.Cargo.Commands.CargoCommandLine(description.ExecutablePath, description.Arguments);
                IDictionary<string, string?> environmentVariables =
                    description.EnvironmentVariables.ToDictionary(kvp => kvp.Key, kvp => (string?)kvp.Value);
                int processId = frameworkHandle.LaunchProcessWithDebuggerAttached(
                    description.ExecutablePath,
                    description.WorkingDirectory,
                    commandLine.Arguments,
                    environmentVariables);

                using var process = System.Diagnostics.Process.GetProcessById(processId);
                await Task.Run(() => process.WaitForExit(), cancellationToken).ConfigureAwait(false);

                // Unlike the non-debug path, the debuggee's stdout is never redirected here (VS
                // owns its console once a debugger is attached to it), so the only signal
                // available afterward is the process exit code - libtest's own convention (0 =
                // every requested test passed/was ignored, 101 = at least one failed).
                result = new TestResult(testCase)
                {
                    Outcome = process.ExitCode == 0 ? TestOutcome.Passed : TestOutcome.Failed,
                };
                result.Messages.Add(new TestResultMessage(
                    TestResultMessage.StandardOutCategory,
                    "Kubuno: this test ran under the debugger; its output was not captured by the test adapter (see the Debug/attached console instead)."));
            }
            catch (OperationCanceledException)
            {
                result = new TestResult(testCase) { Outcome = TestOutcome.None };
            }
            catch (Exception ex)
            {
                result = new TestResult(testCase)
                {
                    Outcome = TestOutcome.Failed,
                    ErrorMessage = $"Kubuno: could not debug-launch '{executablePath}': {ex.Message}",
                };
            }

            frameworkHandle.RecordResult(result);
            frameworkHandle.RecordEnd(testCase, result.Outcome);
        }
    }
}
