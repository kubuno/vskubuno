using System.Linq;
using Kubuno.Cargo.Registry;

namespace Kubuno.Cargo.Tests.Registry
{
    /// <summary>Recorded crates.io responses (search API, sparse index, crate endpoint) - no network.</summary>
    public class CratesIoParserTests
    {
        [Fact]
        public void Search_results_carry_versions_downloads_and_the_total()
        {
            var page = CratesIoParser.ParseSearch(TestFixtures.ReadAllText("CratesIo", "search-serde.json"));

            Assert.Equal(3, page.Crates.Count);
            Assert.True(page.Total > 1000);
            var serde = page.Crates[0];
            Assert.Equal("serde", serde.Name);
            Assert.True(serde.ExactMatch);
            Assert.Equal("1.0.229", serde.MaxStableVersion);
            Assert.Equal("1.0.229", serde.PreferredVersion);
            Assert.True(serde.Downloads > 1_000_000_000);
            Assert.Equal("A generic serialization/deserialization framework", serde.Description);
            Assert.Equal("https://github.com/serde-rs/serde", serde.Repository);
        }

        [Fact]
        public void Index_lines_give_versions_yanked_flags_and_features()
        {
            var versions = CratesIoParser.ParseIndex(TestFixtures.ReadAllText("CratesIo", "index-anyhow.txt"));

            Assert.Equal(5, versions.Count);
            Assert.True(versions[0].Yanked);
            Assert.Equal("0.0.0", versions[0].Vers);

            var latest = versions.Single(v => v.Vers == "1.0.104");
            Assert.False(latest.Yanked);
            Assert.Equal("1.68", latest.RustVersion);
            Assert.Equal(new[] { "std" }, latest.Features["default"]);
            Assert.Equal(new[] { "backtrace", "std" }, latest.SelectableFeatures);
        }

        [Fact]
        public void Features2_are_merged_and_dep_colon_optional_dependencies_are_not_implicit_features()
        {
            var rc = CratesIoParser.ParseIndex(TestFixtures.ReadAllText("CratesIo", "index-anyhow.txt")).Single(v => v.Vers == "1.1.0-rc.1");

            Assert.Equal(new[] { "dep:serde" }, rc.Features["serde"]);
            Assert.Empty(rc.ImplicitFeatures);
            Assert.Equal(new[] { "serde", "std" }, rc.SelectableFeatures);
        }

        [Fact]
        public void An_optional_dependency_without_dep_colon_is_an_implicit_feature()
        {
            var line = "{\"name\":\"x\",\"vers\":\"1.0.0\",\"deps\":[{\"name\":\"log\",\"req\":\"^0.4\",\"optional\":true,\"kind\":\"normal\"},{\"name\":\"t\",\"req\":\"1\",\"optional\":true,\"kind\":\"dev\"}],\"features\":{},\"yanked\":false}";

            var version = CratesIoParser.ParseIndex(line + "\nnot json\n").Single();

            Assert.Equal(new[] { "log" }, version.ImplicitFeatures);
        }

        [Fact]
        public void Crate_details_give_the_description_links_and_per_version_licenses()
        {
            var details = CratesIoParser.ParseCrate(TestFixtures.ReadAllText("CratesIo", "crate-anyhow.json"));

            Assert.Equal("anyhow", details.Name);
            Assert.Equal("https://github.com/dtolnay/anyhow", details.Repository);
            Assert.Equal("1.0.104", details.MaxStableVersion);
            Assert.Equal("MIT OR Apache-2.0", details.Licenses["1.0.104"]);
            Assert.True(details.Downloads > 0);
        }

        [Theory]
        [InlineData("a", "1/a")]
        [InlineData("cc", "2/cc")]
        [InlineData("syn", "3/s/syn")]
        [InlineData("Serde", "se/rd/serde")]
        [InlineData("anyhow", "an/yh/anyhow")]
        public void Sparse_index_paths_follow_cargo_layout(string name, string expected)
        {
            Assert.Equal(expected, CratesIoParser.IndexPath(name));
        }

        [Fact]
        public void Version_status_reports_the_latest_stable_and_a_yanked_resolution()
        {
            var versions = CratesIoParser.ParseIndex(TestFixtures.ReadAllText("CratesIo", "index-anyhow.txt"));

            var current = CrateVersionStatus.Compute("1.0.102", versions);
            Assert.Equal("1.0.104", current.LatestStable);
            Assert.Equal("1.1.0-rc.1", current.Latest);
            Assert.False(current.IsYanked);

            Assert.True(CrateVersionStatus.Compute("0.0.0", versions).IsYanked);
        }
    }
}
