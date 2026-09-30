using System.Linq;
using Kubuno.Rust.TestAdapter.Discovery;

namespace Kubuno.Rust.TestAdapter.Tests.Discovery
{
    /// <summary>
    /// Parses real `&lt;test-exe&gt; --list --format terse` output, captured from the
    /// samples/hello-rust fixture crate (a lib unit test, a bin target with zero tests, an
    /// integration test) and from a throwaway fixture crate with three tests (see
    /// Fixtures/Discovery/*.terse.txt).
    /// </summary>
    public class LibtestListParserTests
    {
        [Fact]
        public void Parses_a_single_lib_unit_test()
        {
            var entries = LibtestListParser.Parse(TestFixtures.ReadAllLines("Discovery", "hello-rust-list-lib.terse.txt"));

            var entry = Assert.Single(entries);
            Assert.Equal("tests::greet_includes_the_name", entry.Name);
            Assert.False(entry.IsBenchmark);
        }

        [Fact]
        public void Parses_an_integration_test()
        {
            var entries = LibtestListParser.Parse(TestFixtures.ReadAllLines("Discovery", "hello-rust-list-integration.terse.txt"));

            var entry = Assert.Single(entries);
            Assert.Equal("greet_includes_the_name", entry.Name);
        }

        [Fact]
        public void A_binary_with_no_tests_yields_no_entries()
        {
            var entries = LibtestListParser.Parse(TestFixtures.ReadAllLines("Discovery", "hello-rust-list-bin.terse.txt"));

            Assert.Empty(entries);
        }

        [Fact]
        public void Parses_every_test_in_a_multi_test_binary_in_order()
        {
            var entries = LibtestListParser.Parse(TestFixtures.ReadAllLines("Discovery", "fixture-crate-list.terse.txt"));

            Assert.Equal(
                new[] { "tests::it_fails", "tests::it_is_ignored", "tests::it_passes" },
                entries.Select(e => e.Name).ToArray());
            Assert.All(entries, e => Assert.False(e.IsBenchmark));
        }

        [Fact]
        public void Ignores_blank_lines_and_a_trailing_non_terse_summary_line()
        {
            var entries = LibtestListParser.Parse(new[]
            {
                "tests::a: test",
                "",
                "tests::b: benchmark",
                "2 tests, 1 benchmark",
            });

            Assert.Equal(2, entries.Count);
            Assert.Equal("tests::a", entries[0].Name);
            Assert.False(entries[0].IsBenchmark);
            Assert.Equal("tests::b", entries[1].Name);
            Assert.True(entries[1].IsBenchmark);
        }
    }
}
