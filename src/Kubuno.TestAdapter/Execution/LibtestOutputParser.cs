using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Kubuno.TestAdapter.Execution
{
    public enum LibtestVerdict
    {
        Passed,
        Failed,
        Ignored,
    }

    /// <summary>One requested test's outcome, extracted from a libtest run's combined output.</summary>
    public sealed class LibtestTestOutcome
    {
        public LibtestTestOutcome(string name, LibtestVerdict verdict, string? errorMessage, string? errorLocation)
        {
            Name = name;
            Verdict = verdict;
            ErrorMessage = errorMessage;
            ErrorLocation = errorLocation;
        }

        public string Name { get; }

        public LibtestVerdict Verdict { get; }

        /// <summary>The panic message (or, failing that, the test's own captured "---- name stdout ----" block) for a failed test; null otherwise.</summary>
        public string? ErrorMessage { get; }

        /// <summary>"file:line:col" the panic occurred at, when found; null otherwise.</summary>
        public string? ErrorLocation { get; }
    }

    /// <summary>
    /// Parses libtest's own console output (`cargo test`'s harness binaries, run directly - see
    /// <see cref="CargoTestRunner"/>) for the outcome of a specific set of requested tests.
    ///
    /// Why this does NOT parse the "test &lt;name&gt; ... ok|FAILED|ignored" lines as the primary
    /// signal, even though that is libtest's headline per-test line: this adapter runs with
    /// `--nocapture` (per spec, so a test's own stdout/panic is visible live rather than only
    /// on failure), and libtest prints that line in two pieces - "test &lt;name&gt; ... " first,
    /// then (after the test body itself runs, printing whatever it prints) "ok"/"FAILED" appended
    /// - with no synchronization against the *test's own* stdout writes or its panic hook (which
    /// runs on a separate, per-test OS thread). Captured real output
    /// (Fixtures/Execution/fixture-crate-run-*.txt) shows this interleaving breaking a naive
    /// per-line regex: e.g. "test tests::it_fails ... about to fail\n\nthread '...' panicked at
    /// ...\nFAILED" - the test's own `println!` lands *between* "... " and "FAILED".
    ///
    /// Two things remain reliable regardless of that interleaving, because libtest emits them
    /// only after every test thread has already joined back with the main driver thread:
    /// 1. The trailing "failures:\n    &lt;name&gt;\n    &lt;name&gt;\n\ntest result: ..." block -
    ///    the definitive list of which requested tests failed (verified against every captured
    ///    fixture, batched and single, nocapture and not).
    /// 2. "test &lt;name&gt; ... ignored" lines - an ignored test's body never runs, so there is
    ///    nothing that could interleave with that one line.
    /// So: Failed = names in the failures block; Ignored = names on a "... ignored" line and not
    /// already Failed; Passed = every other requested name. The panic hook's own
    /// "thread '&lt;name&gt;' (&lt;pid&gt;) panicked at &lt;file&gt;:&lt;line&gt;:&lt;col&gt;:" header names the
    /// test by its thread name (== the libtest test name), so a failed test's message can still
    /// be attributed correctly even under interleaving - see <see cref="ParsePanics"/>.
    /// </summary>
    public static class LibtestOutputParser
    {
        private static readonly Regex IgnoredLine = new Regex(
            @"^test (?<name>\S.*?) \.\.\. ignored\s*$",
            RegexOptions.Multiline | RegexOptions.Compiled);

        private static readonly Regex FailuresBlock = new Regex(
            @"failures:\r?\n(?<names>(?:[ \t]+\S[^\r\n]*\r?\n)+)[\r\n]*test result:",
            RegexOptions.Compiled);

        private static readonly Regex Panic = new Regex(
            @"thread '(?<name>[^']+)'(?: \(\d+\))? panicked at (?<location>[^\r\n]+):\r?\n(?<message>.*?)(?=\r?\n(?:FAILED\b|thread '|failures:|test result:|test\s)|\z)",
            RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly Regex StdoutBlock = new Regex(
            @"---- (?<name>\S+) stdout ----\r?\n(?<content>.*?)(?=\r?\n(?:---- \S+ stdout ----|failures:)|\z)",
            RegexOptions.Singleline | RegexOptions.Compiled);

        public static IReadOnlyDictionary<string, LibtestTestOutcome> Parse(string combinedOutput, IReadOnlyCollection<string> requestedNames)
        {
            if (combinedOutput is null)
            {
                throw new ArgumentNullException(nameof(combinedOutput));
            }
            if (requestedNames is null)
            {
                throw new ArgumentNullException(nameof(requestedNames));
            }

            HashSet<string> failed = ParseFailedNames(combinedOutput);
            HashSet<string> ignored = ParseIgnoredNames(combinedOutput);
            Dictionary<string, (string Message, string Location)> panics = ParsePanics(combinedOutput);
            Dictionary<string, string> stdoutBlocks = ParseStdoutBlocks(combinedOutput);

            var outcomes = new Dictionary<string, LibtestTestOutcome>(StringComparer.Ordinal);
            foreach (string name in requestedNames)
            {
                if (failed.Contains(name))
                {
                    if (panics.TryGetValue(name, out var panic))
                    {
                        outcomes[name] = new LibtestTestOutcome(name, LibtestVerdict.Failed, panic.Message.TrimEnd(), panic.Location);
                    }
                    else if (stdoutBlocks.TryGetValue(name, out var block))
                    {
                        outcomes[name] = new LibtestTestOutcome(name, LibtestVerdict.Failed, block.Trim(), errorLocation: null);
                    }
                    else
                    {
                        outcomes[name] = new LibtestTestOutcome(name, LibtestVerdict.Failed, errorMessage: null, errorLocation: null);
                    }
                }
                else if (ignored.Contains(name))
                {
                    outcomes[name] = new LibtestTestOutcome(name, LibtestVerdict.Ignored, errorMessage: null, errorLocation: null);
                }
                else
                {
                    outcomes[name] = new LibtestTestOutcome(name, LibtestVerdict.Passed, errorMessage: null, errorLocation: null);
                }
            }
            return outcomes;
        }

        private static HashSet<string> ParseFailedNames(string output)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);

            // The true trailing summary is always the *last* "failures:" block that is
            // immediately (no blank line) followed by indented names and then "test result:" -
            // an earlier "failures:" header (introducing the per-test "---- name stdout ----"
            // dump) is followed by a blank line or a "----" line instead, so it never matches
            // this pattern. Still: take the last match, not just any, for extra safety.
            Match? lastMatch = null;
            foreach (Match match in FailuresBlock.Matches(output))
            {
                lastMatch = match;
            }
            if (lastMatch is null)
            {
                return names;
            }

            foreach (string rawLine in lastMatch.Groups["names"].Value.Split('\n'))
            {
                string name = rawLine.Trim();
                if (name.Length > 0)
                {
                    names.Add(name);
                }
            }
            return names;
        }

        private static HashSet<string> ParseIgnoredNames(string output)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match match in IgnoredLine.Matches(output))
            {
                names.Add(match.Groups["name"].Value);
            }
            return names;
        }

        private static Dictionary<string, (string Message, string Location)> ParsePanics(string output)
        {
            var panics = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
            foreach (Match match in Panic.Matches(output))
            {
                // The *first* panic message per name wins: a test can only panic once (the
                // thread unwinds/aborts right there), so a second match for the same name would
                // only happen if two different tests happened to share a name, which `--exact`
                // filtering never allows within one run.
                string name = match.Groups["name"].Value;
                if (!panics.ContainsKey(name))
                {
                    panics[name] = (match.Groups["message"].Value, match.Groups["location"].Value);
                }
            }
            return panics;
        }

        private static Dictionary<string, string> ParseStdoutBlocks(string output)
        {
            var blocks = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match match in StdoutBlock.Matches(output))
            {
                string name = match.Groups["name"].Value;
                if (!blocks.ContainsKey(name))
                {
                    blocks[name] = match.Groups["content"].Value;
                }
            }
            return blocks;
        }
    }
}
