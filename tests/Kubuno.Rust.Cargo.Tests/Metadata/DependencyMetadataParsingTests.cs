using System.Linq;
using System.Threading.Tasks;
using Kubuno.Cargo.Metadata;
using Kubuno.Cargo.Processes;
using Kubuno.Cargo.Tests.Fakes;

namespace Kubuno.Cargo.Tests.Metadata
{
    /// <summary>
    /// Real `cargo metadata` output (cargo 1.98.1) of a package with every kind of dependency the
    /// Dependencies node shows: registry crates (one renamed, one optional, one target-specific), a
    /// direct proc-macro, a path dependency, a git dependency, a dev- and a build-dependency.
    /// </summary>
    public class DependencyMetadataParsingTests
    {
        private static CargoMetadata Full() => CargoMetadataReader.Parse(TestFixtures.ReadAllText("Metadata", "dependencies-full.json"));

        [Fact]
        public void Full_metadata_has_a_resolve_graph_rooted_at_the_package()
        {
            var metadata = Full();

            Assert.NotNull(metadata.Resolve);
            Assert.NotNull(metadata.Resolve!.Root);
            var root = metadata.Resolve.Find(metadata.Resolve.Root!);
            Assert.NotNull(root);
            Assert.Equal(9, root!.Deps.Count);
        }

        [Fact]
        public void Resolve_edges_carry_their_kinds_targets_and_extern_names()
        {
            var root = Full().Resolve!.Find(Full().Resolve!.Root!)!;

            var build = root.Deps.Single(d => d.Name == "autocfg");
            Assert.True(build.HasKind("build"));
            Assert.False(build.HasKind(null));

            var windowsOnly = root.Deps.Single(d => d.Name == "scopeguard");
            Assert.Equal("cfg(windows)", windowsOnly.DepKinds.Single().Target);

            // A renamed dependency's edge uses the rename.
            var renamed = root.Deps.Single(d => d.Pkg.Contains("#serde_json@"));
            Assert.Equal("json", renamed.Name);
        }

        [Fact]
        public void Packages_carry_source_license_description_and_proc_macro_kind()
        {
            var metadata = Full();

            var serde = metadata.Packages.Single(p => p.Name == "serde");
            Assert.StartsWith("registry+", serde.Source);
            Assert.Equal("MIT OR Apache-2.0", serde.License);
            Assert.False(string.IsNullOrEmpty(serde.Description));
            Assert.False(serde.IsProcMacro);

            Assert.True(metadata.Packages.Single(p => p.Name == "serde_derive").IsProcMacro);
            Assert.Null(metadata.Packages.Single(p => p.Name == "corelib").Source);
            Assert.StartsWith("git+https://github.com/dtolnay/ryu", metadata.Packages.Single(p => p.Name == "ryu").Source);
        }

        [Fact]
        public void Declared_dependencies_carry_features_default_features_and_renames()
        {
            var app = Full().Packages.Single(p => p.Name == "app");

            var serde = app.Dependencies.Single(d => d.Name == "serde");
            Assert.Equal(new[] { "derive" }, serde.Features);
            Assert.True(serde.UsesDefaultFeatures);

            var json = app.Dependencies.Single(d => d.Name == "serde_json");
            Assert.Equal("json", json.Rename);
            Assert.Equal("json", json.LocalName);

            Assert.True(app.Dependencies.Single(d => d.Name == "itoa").Optional);
            Assert.Equal("cfg(windows)", app.Dependencies.Single(d => d.Name == "scopeguard").Target);
        }

        [Fact]
        public void No_deps_output_has_no_resolve_graph()
        {
            var metadata = CargoMetadataReader.Parse(TestFixtures.ReadAllText("Metadata", "dependencies-no-deps.json"));

            Assert.Null(metadata.Resolve);
            Assert.Single(metadata.Packages);
            Assert.Equal(10, metadata.Packages[0].Dependencies.Count);
        }

        [Fact]
        public async Task Reading_with_dependencies_drops_no_deps_and_can_run_offline()
        {
            var runner = new FakeProcessRunner(new ProcessRunResult(0, new[] { TestFixtures.ReadAllText("Metadata", "dependencies-full.json") }, System.Array.Empty<string>()));
            var reader = new CargoMetadataReader(runner);

            var metadata = await reader.ReadAsync(@"C:\ws", @"C:\ws\Cargo.toml", CargoMetadataReadOptions.IncludeDependencies | CargoMetadataReadOptions.Offline);

            Assert.NotNull(metadata.Resolve);
            Assert.DoesNotContain("--no-deps", runner.LastRequest!.Arguments);
            Assert.Contains("--offline", runner.LastRequest.Arguments);

            await reader.ReadAsync(@"C:\ws", null, CargoMetadataReadOptions.IncludeDependencies, null, default, "x86_64-pc-windows-msvc");
            Assert.Contains("--filter-platform x86_64-pc-windows-msvc", runner.LastRequest!.Arguments);
        }
    }
}
