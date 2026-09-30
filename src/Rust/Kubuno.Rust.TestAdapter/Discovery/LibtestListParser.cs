using System;
using System.Collections.Generic;

namespace Kubuno.TestAdapter.Discovery
{
    /// <summary>One entry from `&lt;test-exe&gt; --list --format terse`.</summary>
    public sealed class LibtestListEntry
    {
        public LibtestListEntry(string name, bool isBenchmark)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            IsBenchmark = isBenchmark;
        }

        /// <summary>The exact libtest name, e.g. "tests::greet_includes_the_name" - the value `--exact` matches against at run time.</summary>
        public string Name { get; }

        public bool IsBenchmark { get; }
    }

    /// <summary>
    /// Parses `&lt;test-exe&gt; --list --format terse` output: one `&lt;name&gt;: test` or
    /// `&lt;name&gt;: benchmark` line per test/benchmark, nothing else (verified against real
    /// output - unlike the default `--list` format, `--format terse` emits no trailing
    /// "N tests, M benchmarks" summary line, so every non-empty line is a real entry).
    /// </summary>
    public static class LibtestListParser
    {
        private const string Separator = ": ";

        public static IReadOnlyList<LibtestListEntry> Parse(IEnumerable<string> terseListOutputLines)
        {
            if (terseListOutputLines is null)
            {
                throw new ArgumentNullException(nameof(terseListOutputLines));
            }

            var entries = new List<LibtestListEntry>();
            foreach (string rawLine in terseListOutputLines)
            {
                string line = rawLine.TrimEnd('\r');
                if (line.Length == 0)
                {
                    continue;
                }

                int separatorIndex = line.LastIndexOf(Separator, StringComparison.Ordinal);
                if (separatorIndex < 0)
                {
                    // Defensive only: every terse line has "name: test"/"name: benchmark" shape.
                    // Tolerating (and skipping) anything else keeps a future libtest output
                    // change from crashing discovery outright.
                    continue;
                }

                string name = line.Substring(0, separatorIndex);
                string kind = line.Substring(separatorIndex + Separator.Length);

                bool isBenchmark = string.Equals(kind, "benchmark", StringComparison.Ordinal);
                if (!isBenchmark && !string.Equals(kind, "test", StringComparison.Ordinal))
                {
                    continue;
                }

                entries.Add(new LibtestListEntry(name, isBenchmark));
            }

            return entries;
        }
    }
}
