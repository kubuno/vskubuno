using Kubuno.Rust.TestAdapter.Execution;

namespace Kubuno.Rust.TestAdapter.Tests.Execution
{
    /// <summary>
    /// Parses real `--nocapture --test-threads=1` libtest runs, captured from a throwaway
    /// fixture crate with a passing test, a failing (panicking) test, and an `#[ignore]`d test
    /// (see Fixtures/Execution/*.txt - single-test and multi-test-in-one-process invocations,
    /// matching how <see cref="CargoTestRunner"/> batches by executable).
    ///
    /// The interesting case throughout: --nocapture makes the failing test's own `println!`
    /// land in the middle of libtest's "test &lt;name&gt; ... FAILED" line (see e.g.
    /// fixture-crate-run-fail.txt: "test tests::it_fails ... about to fail\nFAILED", not
    /// "test tests::it_fails ... FAILED" on one line) - every assertion here exists to prove the
    /// parser still gets the right verdict and message despite that, per the design explained on
    /// <see cref="LibtestOutputParser"/> itself.
    /// </summary>
    public class LibtestOutputParserTests
    {
        [Fact]
        public void A_single_passing_test_is_Passed_with_no_error_message()
        {
            string output = TestFixtures.ReadAllText("Execution", "fixture-crate-run-pass.txt");

            var outcomes = LibtestOutputParser.Parse(output, new[] { "tests::it_passes" });

            Assert.Equal(LibtestVerdict.Passed, outcomes["tests::it_passes"].Verdict);
            Assert.Null(outcomes["tests::it_passes"].ErrorMessage);
        }

        [Fact]
        public void A_single_failing_test_is_Failed_with_the_panic_message_and_location()
        {
            string output = TestFixtures.ReadAllText("Execution", "fixture-crate-run-fail.txt");

            var outcomes = LibtestOutputParser.Parse(output, new[] { "tests::it_fails" });

            LibtestTestOutcome outcome = outcomes["tests::it_fails"];
            Assert.Equal(LibtestVerdict.Failed, outcome.Verdict);
            Assert.Contains("math is broken", outcome.ErrorMessage);
            Assert.Contains("left: 4", outcome.ErrorMessage);
            Assert.Equal(@"src\lib.rs:17:9", outcome.ErrorLocation);
        }

        [Fact]
        public void An_ignored_test_forced_to_run_via_include_ignored_that_panics_is_Failed()
        {
            string output = TestFixtures.ReadAllText("Execution", "fixture-crate-run-ignored-included.txt");

            var outcomes = LibtestOutputParser.Parse(output, new[] { "tests::it_is_ignored" });

            LibtestTestOutcome outcome = outcomes["tests::it_is_ignored"];
            Assert.Equal(LibtestVerdict.Failed, outcome.Verdict);
            Assert.Contains("should never run", outcome.ErrorMessage);
        }

        [Fact]
        public void A_skipped_ignored_test_is_Ignored_not_Failed()
        {
            string output = TestFixtures.ReadAllText("Execution", "fixture-crate-run-ignored-skipped.txt");

            var outcomes = LibtestOutputParser.Parse(output, new[] { "tests::it_is_ignored" });

            Assert.Equal(LibtestVerdict.Ignored, outcomes["tests::it_is_ignored"].Verdict);
        }

        [Fact]
        public void A_mixed_run_of_all_three_tests_reports_each_verdict_correctly()
        {
            string output = TestFixtures.ReadAllText("Execution", "fixture-crate-run-all.txt");
            var requested = new[] { "tests::it_passes", "tests::it_fails", "tests::it_is_ignored" };

            var outcomes = LibtestOutputParser.Parse(output, requested);

            Assert.Equal(LibtestVerdict.Passed, outcomes["tests::it_passes"].Verdict);
            Assert.Equal(LibtestVerdict.Failed, outcomes["tests::it_fails"].Verdict);
            Assert.Equal(LibtestVerdict.Ignored, outcomes["tests::it_is_ignored"].Verdict);
        }

        [Fact]
        public void A_batched_two_test_invocation_attributes_the_panic_to_the_right_test_despite_nocapture_interleaving()
        {
            string output = TestFixtures.ReadAllText("Execution", "fixture-crate-run-batch-pass-fail.txt");
            var requested = new[] { "tests::it_passes", "tests::it_fails" };

            var outcomes = LibtestOutputParser.Parse(output, requested);

            Assert.Equal(LibtestVerdict.Passed, outcomes["tests::it_passes"].Verdict);
            Assert.Null(outcomes["tests::it_passes"].ErrorMessage);

            Assert.Equal(LibtestVerdict.Failed, outcomes["tests::it_fails"].Verdict);
            Assert.Contains("math is broken", outcomes["tests::it_fails"].ErrorMessage);
        }

        [Fact]
        public void A_batched_run_with_two_different_failures_attributes_each_panic_message_to_its_own_test()
        {
            string output = TestFixtures.ReadAllText("Execution", "fixture-crate-run-batch-all-included.txt");
            var requested = new[] { "tests::it_passes", "tests::it_fails", "tests::it_is_ignored" };

            var outcomes = LibtestOutputParser.Parse(output, requested);

            Assert.Equal(LibtestVerdict.Passed, outcomes["tests::it_passes"].Verdict);

            Assert.Equal(LibtestVerdict.Failed, outcomes["tests::it_fails"].Verdict);
            Assert.Contains("math is broken", outcomes["tests::it_fails"].ErrorMessage);

            // it_is_ignored was force-run here (--include-ignored) and panicked with a DIFFERENT
            // message than it_fails - this is the case that would break a naive "attribute the
            // one panic message we found to every failed test" shortcut.
            Assert.Equal(LibtestVerdict.Failed, outcomes["tests::it_is_ignored"].Verdict);
            Assert.Contains("should never run", outcomes["tests::it_is_ignored"].ErrorMessage);
            Assert.DoesNotContain("math is broken", outcomes["tests::it_is_ignored"].ErrorMessage);
        }

        [Fact]
        public void A_test_not_mentioned_anywhere_in_the_output_still_gets_a_default_Passed_outcome()
        {
            // Defensive: LibtestOutputParser always assigns every requested name an outcome
            // (Failed only when it appears in the trailing failures: list, Ignored only when its
            // own "... ignored" line is present) - never leaves one unaccounted for.
            var outcomes = LibtestOutputParser.Parse("running 0 tests\n\ntest result: ok. 0 passed; 0 failed; 0 ignored; 0 measured; 0 filtered out; finished in 0.00s\n", new[] { "tests::mystery" });

            Assert.Equal(LibtestVerdict.Passed, outcomes["tests::mystery"].Verdict);
        }
    }
}
