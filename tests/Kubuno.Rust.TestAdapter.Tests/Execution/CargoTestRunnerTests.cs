using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Rust.Cargo.Processes;
using Kubuno.Rust.TestAdapter.Execution;
using Kubuno.Rust.TestAdapter.Tests.Fakes;

namespace Kubuno.Rust.TestAdapter.Tests.Execution
{
    public class CargoTestRunnerTests
    {
        [Fact]
        public async Task Requests_every_name_in_one_process_invocation_with_exact_nocapture_and_a_single_thread()
        {
            string output = TestFixtures.ReadAllText("Execution", "fixture-crate-run-batch-pass-fail.txt");
            var runner = new FakeProcessRunner(new ProcessRunResult(101, output.Split('\n'), Array.Empty<string>()));

            await CargoTestRunner.RunAsync(
                runner,
                executablePath: @"C:\target\debug\deps\fixture_crate-abc.exe",
                workingDirectory: @"C:\fixture-crate",
                libtestNames: new[] { "tests::it_passes", "tests::it_fails" },
                CancellationToken.None);

            var request = Assert.Single(runner.Requests);
            Assert.Equal(@"C:\target\debug\deps\fixture_crate-abc.exe", request.FileName);
            Assert.Contains("tests::it_passes", request.Arguments);
            Assert.Contains("tests::it_fails", request.Arguments);
            Assert.Contains("--exact", request.Arguments);
            Assert.Contains("--nocapture", request.Arguments);
            Assert.Contains("--test-threads=1", request.Arguments);
            Assert.Equal(@"C:\fixture-crate", request.WorkingDirectory);
        }

        [Fact]
        public async Task Reports_the_exit_code_and_per_test_outcomes()
        {
            string output = TestFixtures.ReadAllText("Execution", "fixture-crate-run-batch-pass-fail.txt");
            var runner = new FakeProcessRunner(new ProcessRunResult(101, output.Split('\n'), Array.Empty<string>()));

            CargoTestRunResult result = await CargoTestRunner.RunAsync(
                runner, @"C:\exe.exe", @"C:\root", new[] { "tests::it_passes", "tests::it_fails" }, CancellationToken.None);

            Assert.Equal(101, result.ExitCode);
            Assert.Equal(LibtestVerdict.Passed, result.Outcomes["tests::it_passes"].Verdict);
            Assert.Equal(LibtestVerdict.Failed, result.Outcomes["tests::it_fails"].Verdict);
            Assert.Contains("math is broken", result.CombinedOutput);
        }

        [Fact]
        public async Task Throws_for_an_empty_test_name_list()
        {
            var runner = new FakeProcessRunner(new ProcessRunResult(0, Array.Empty<string>(), Array.Empty<string>()));

            await Assert.ThrowsAsync<ArgumentException>(() =>
                CargoTestRunner.RunAsync(runner, @"C:\exe.exe", @"C:\root", Array.Empty<string>(), CancellationToken.None));
        }

        [Fact]
        public void Names_are_split_into_batches_that_fit_a_command_line()
        {
            var names = new[] { "aaaa", "bbbb", "cccc", "a_very_long_name_on_its_own" };

            var batches = CargoTestRunner.SplitIntoBatches(names, maxLength: 15);

            Assert.Equal(new[] { new[] { "aaaa", "bbbb" }, new[] { "cccc" }, new[] { "a_very_long_name_on_its_own" } }, batches);
        }

        [Fact]
        public async Task Hundreds_of_tests_run_in_several_launches_and_every_outcome_is_kept()
        {
            // kubuno-controls has hundreds of tests: all their names in one launch exceed Windows' 32,767-character command line.
            var names = System.Linq.Enumerable.Range(0, 1500).Select(i => $"module::tests::a_reasonably_long_test_name_number_{i}").ToList();
            var runner = new FakeProcessRunner(request =>
            {
                var requested = request.Arguments.Split(' ').Select(a => a.Trim('"')).Where(a => a.StartsWith("module::", StringComparison.Ordinal)).ToList();
                var lines = requested.Select(n => $"test {n} ... ok").ToList();
                lines.Add($"test result: ok. {requested.Count} passed; 0 failed; 0 ignored; 0 measured; 0 filtered out; finished in 0.01s");
                return new ProcessRunResult(0, lines, Array.Empty<string>());
            });

            CargoTestRunResult result = await CargoTestRunner.RunAsync(runner, @"C:\target\debug\deps\kubuno_controls-1.exe", @"C:\root", names, CancellationToken.None);

            Assert.True(runner.Requests.Count > 1);
            Assert.All(runner.Requests, request => Assert.True(request.Arguments.Length < 32_000));
            Assert.Equal(1500, result.Outcomes.Count);
            Assert.All(result.Outcomes.Values, outcome => Assert.Equal(LibtestVerdict.Passed, outcome.Verdict));
        }
    }
}
