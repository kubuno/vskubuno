using System.Linq;
using Kubuno.Cargo.Tools;

namespace Kubuno.Cargo.Tests.Tools
{
    public class CargoMacheteOutputParserTests
    {
        [Fact]
        public void Parses_real_cargo_machete_report()
        {
            // cargo-machete 0.9.2, stdout and stderr merged, exit code 1.
            var lines = new[]
            {
                "Analyzing dependencies of crates in this directory...",
                "cargo-machete found the following unused dependencies in this directory:",
                @"app -- .\Cargo.toml:",
                "\tanyhow",
                "\tjson",
                "\tserde_derive",
                string.Empty,
                "If you believe cargo-machete has detected an unused dependency incorrectly,",
                "you can add the dependency to the list of dependencies to ignore in the",
                "`[package.metadata.cargo-machete]` section of the appropriate Cargo.toml.",
                "For example:",
                string.Empty,
                "[package.metadata.cargo-machete]",
                "ignored = [\"prost\"]",
                string.Empty,
                "Done!",
            };

            var result = CargoMacheteOutputParser.Parse(lines).Single();

            Assert.Equal("app", result.PackageName);
            Assert.Equal(@".\Cargo.toml", result.ManifestPath);
            Assert.Equal(new[] { "anyhow", "json", "serde_derive" }, result.Dependencies);
        }

        [Fact]
        public void Several_packages_and_a_clean_report()
        {
            var lines = new[]
            {
                "cargo-machete found the following unused dependencies in this directory:",
                "a -- C:/ws/a/Cargo.toml:",
                "\tx",
                "b -- C:/ws/b/Cargo.toml:",
                "\ty",
                "\tz",
            };

            var result = CargoMacheteOutputParser.Parse(lines);
            Assert.Equal(new[] { "a", "b" }, result.Select(r => r.PackageName));
            Assert.Equal(new[] { "y", "z" }, result[1].Dependencies);

            Assert.Empty(CargoMacheteOutputParser.Parse(new[] { "cargo-machete didn't find any unused dependencies in this directory. Good job!", "Done!" }));
        }

        [Fact]
        public void Parses_cargo_udeps_json_outcome()
        {
            // Shape of cargo-udeps' `Outcome` (--output json), preceded by a cargo progress line.
            var json = "    Checking app v0.1.0\n{\"success\":false,\"unused_deps\":{\"app 0.1.0 (path+file:///C:/ws/app)\":{\"manifest_path\":\"C:\\\\ws\\\\app\\\\Cargo.toml\",\"normal\":[\"anyhow\",\"json\"],\"development\":[\"static_assertions\"],\"build\":[]}},\"note\":\"Note: They might be false-positive.\"}";

            var result = CargoUdepsOutputParser.Parse(json).Single();

            Assert.Equal("app", result.PackageName);
            Assert.Equal(@"C:\ws\app\Cargo.toml", result.ManifestPath);
            Assert.Equal(new[] { "anyhow", "json", "static_assertions" }, result.Dependencies);
            Assert.Empty(CargoUdepsOutputParser.Parse("{\"success\":true,\"unused_deps\":{}}"));
        }
    }
}
