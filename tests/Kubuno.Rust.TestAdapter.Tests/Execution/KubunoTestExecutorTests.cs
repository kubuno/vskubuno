using System;
using System.Linq;
using Kubuno.Cargo.Processes;
using Kubuno.TestAdapter;
using Kubuno.TestAdapter.Discovery;
using Kubuno.TestAdapter.Execution;
using Kubuno.TestAdapter.Tests.Fakes;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;

namespace Kubuno.TestAdapter.Tests.Execution
{
    /// <summary>Exercises the non-debug `RunTests(IEnumerable&lt;TestCase&gt;, ...)` path end-to-end, batching both of a fake executable's tests into one process invocation and replaying a captured libtest run (Fixtures/Execution/fixture-crate-run-batch-pass-fail.txt).</summary>
    public class KubunoTestExecutorTests
    {
        private const string ExecutablePath = @"C:\target\debug\deps\fixture_crate-abc.exe";
        private const string WorkingDirectory = @"C:\fixture-crate";

        private static TestCase MakeTestCase(string libtestName)
        {
            var binary = new CargoTestBinary(
                "fixture_crate",
                CargoTestBinaryKind.Lib,
                sourcePath: @"C:\does-not-exist.rs",
                executablePath: ExecutablePath,
                packageManifestPath: @"C:\fixture-crate\Cargo.toml",
                packageRoot: WorkingDirectory,
                targetDirectory: @"C:\target");
            return CargoTestCaseFactory.CreateTestCase(binary, libtestName);
        }

        [Fact]
        public void Runs_both_tests_from_the_same_executable_in_a_single_process_invocation()
        {
            string output = TestFixtures.ReadAllText("Execution", "fixture-crate-run-batch-pass-fail.txt");
            var processRunner = new FakeProcessRunner(new ProcessRunResult(101, output.Split('\n'), Array.Empty<string>()));
            var executor = new KubunoTestExecutor(processRunner);
            var frameworkHandle = new FakeFrameworkHandle();
            var runContext = new FakeRunContext { IsBeingDebugged = false };

            var passCase = MakeTestCase("tests::it_passes");
            var failCase = MakeTestCase("tests::it_fails");

            executor.RunTests(new[] { passCase, failCase }, runContext, frameworkHandle);

            Assert.Single(processRunner.Requests); // one batched invocation, not two
            Assert.Equal(2, frameworkHandle.Started.Count);
            Assert.Equal(2, frameworkHandle.Results.Count);
        }

        [Fact]
        public void Reports_Passed_and_Failed_outcomes_matching_the_libtest_run()
        {
            string output = TestFixtures.ReadAllText("Execution", "fixture-crate-run-batch-pass-fail.txt");
            var processRunner = new FakeProcessRunner(new ProcessRunResult(101, output.Split('\n'), Array.Empty<string>()));
            var executor = new KubunoTestExecutor(processRunner);
            var frameworkHandle = new FakeFrameworkHandle();

            var passCase = MakeTestCase("tests::it_passes");
            var failCase = MakeTestCase("tests::it_fails");

            executor.RunTests(new[] { passCase, failCase }, new FakeRunContext(), frameworkHandle);

            var passResult = frameworkHandle.Results.Single(r => r.TestCase.Id == passCase.Id);
            var failResult = frameworkHandle.Results.Single(r => r.TestCase.Id == failCase.Id);

            Assert.Equal(TestOutcome.Passed, passResult.Outcome);
            Assert.Equal(TestOutcome.Failed, failResult.Outcome);
            Assert.Contains("math is broken", failResult.ErrorMessage);
        }

        [Fact]
        public void Every_result_carries_the_raw_process_output_as_a_standard_output_message()
        {
            string output = TestFixtures.ReadAllText("Execution", "fixture-crate-run-batch-pass-fail.txt");
            var processRunner = new FakeProcessRunner(new ProcessRunResult(101, output.Split('\n'), Array.Empty<string>()));
            var executor = new KubunoTestExecutor(processRunner);
            var frameworkHandle = new FakeFrameworkHandle();

            executor.RunTests(new[] { MakeTestCase("tests::it_passes") }, new FakeRunContext(), frameworkHandle);

            var result = Assert.Single(frameworkHandle.Results);
            Assert.Contains(result.Messages, m => m.Category == TestResultMessage.StandardOutCategory);
        }

        [Fact]
        public void Different_executables_are_run_as_separate_process_invocations()
        {
            var processRunner = new FakeProcessRunner(request => new ProcessRunResult(
                0,
                new[] { "running 1 test", "test tests::it_passes ... ok", "", "test result: ok. 1 passed; 0 failed; 0 ignored; 0 measured; 0 filtered out; finished in 0.00s" },
                Array.Empty<string>()));
            var executor = new KubunoTestExecutor(processRunner);
            var frameworkHandle = new FakeFrameworkHandle();

            var binaryA = new CargoTestBinary("a", CargoTestBinaryKind.Lib, @"C:\a.rs", @"C:\a.exe", @"C:\a\Cargo.toml", @"C:\a", @"C:\target");
            var binaryB = new CargoTestBinary("b", CargoTestBinaryKind.Lib, @"C:\b.rs", @"C:\b.exe", @"C:\b\Cargo.toml", @"C:\b", @"C:\target");
            var caseA = CargoTestCaseFactory.CreateTestCase(binaryA, "tests::it_passes");
            var caseB = CargoTestCaseFactory.CreateTestCase(binaryB, "tests::it_passes");

            executor.RunTests(new[] { caseA, caseB }, new FakeRunContext(), frameworkHandle);

            Assert.Equal(2, processRunner.Requests.Count);
        }
    }
}
