using System.Linq;
using System.Threading.Tasks;
using Kubuno.Cargo.Metadata;
using Kubuno.Cargo.Processes;
using Kubuno.Cargo.Tests.Fakes;
using Xunit;

namespace Kubuno.Cargo.Tests.Metadata
{
    /// <summary>
    /// Exercises <see cref="CargoMetadataReader"/> against real `cargo metadata --format-version
    /// 1 --no-deps` output, captured from a two-package workspace
    /// (Fixtures/Metadata/workspace-metadata.json) built specifically to cover every target
    /// kind: a lib, two bins (one gated by `required-features`), an example, an integration
    /// test and a bench. The process itself is faked (<see cref="FakeProcessRunner"/>) so these
    /// tests are fast and don't need "cargo" on PATH; only the JSON parsing is real.
    /// </summary>
    public class CargoMetadataParsingTests
    {
        private static Task<CargoMetadata> ReadFixtureAsync(string fileName)
        {
            string[] lines = TestFixtures.ReadAllLines("Metadata", fileName);
            var runner = new FakeProcessRunner(new ProcessRunResult(0, lines, System.Array.Empty<string>()));
            var reader = new CargoMetadataReader(runner);
            return reader.ReadAsync(workingDirectory: @"C:\irrelevant-for-this-test");
        }

        [Fact]
        public async Task WorkspaceRoot_and_TargetDirectory_are_absolute_paths()
        {
            CargoMetadata metadata = await ReadFixtureAsync("workspace-metadata.json");

            Assert.True(System.IO.Path.IsPathRooted(metadata.WorkspaceRoot));
            Assert.EndsWith("fixtures-workspace", metadata.WorkspaceRoot);
            Assert.EndsWith("target", metadata.TargetDirectory);
        }

        [Fact]
        public async Task Both_workspace_packages_are_present()
        {
            CargoMetadata metadata = await ReadFixtureAsync("workspace-metadata.json");

            Assert.Equal(2, metadata.Packages.Count);
            Assert.Equal(2, metadata.WorkspaceMembers.Count);
            Assert.Contains(metadata.Packages, p => p.Name == "core-lib");
            Assert.Contains(metadata.Packages, p => p.Name == "app");
        }

        [Fact]
        public async Task Lib_target_is_reported_with_its_own_crate_name()
        {
            CargoMetadata metadata = await ReadFixtureAsync("workspace-metadata.json");
            CargoPackage coreLib = metadata.Packages.Single(p => p.Name == "core-lib");

            CargoTarget lib = Assert.Single(coreLib.Targets);
            Assert.Equal("core_lib", lib.Name);
            Assert.Equal(new[] { "lib" }, lib.Kind);
            Assert.EndsWith("core-lib\\src\\lib.rs", lib.SrcPath);
            Assert.Empty(lib.RequiredFeatures);
        }

        [Fact]
        public async Task App_package_lists_every_target_kind()
        {
            CargoMetadata metadata = await ReadFixtureAsync("workspace-metadata.json");
            CargoPackage app = metadata.Packages.Single(p => p.Name == "app");

            Assert.Equal(5, app.Targets.Count);

            CargoTarget mainBin = app.Targets.Single(t => t.Name == "app");
            Assert.Equal(new[] { "bin" }, mainBin.Kind);
            Assert.Empty(mainBin.RequiredFeatures);

            CargoTarget example = app.Targets.Single(t => t.Name == "demo");
            Assert.Equal(new[] { "example" }, example.Kind);

            CargoTarget test = app.Targets.Single(t => t.Name == "integration");
            Assert.Equal(new[] { "test" }, test.Kind);

            CargoTarget bench = app.Targets.Single(t => t.Name == "bench1");
            Assert.Equal(new[] { "bench" }, bench.Kind);
        }

        [Fact]
        public async Task RequiredFeatures_of_a_feature_gated_bin_are_parsed_despite_the_kebab_case_json_name()
        {
            CargoMetadata metadata = await ReadFixtureAsync("workspace-metadata.json");
            CargoPackage app = metadata.Packages.Single(p => p.Name == "app");

            CargoTarget extraBin = app.Targets.Single(t => t.Name == "extra_bin");
            Assert.Equal(new[] { "extra" }, extraBin.RequiredFeatures);
        }

        [Fact]
        public async Task Features_dictionary_is_parsed()
        {
            CargoMetadata metadata = await ReadFixtureAsync("workspace-metadata.json");
            CargoPackage app = metadata.Packages.Single(p => p.Name == "app");

            Assert.True(app.Features.ContainsKey("extra"));
            Assert.Empty(app.Features["extra"]);
        }

        [Fact]
        public async Task CARGO_TARGET_DIR_override_is_reflected_in_TargetDirectory()
        {
            CargoMetadata withDefault = await ReadFixtureAsync("workspace-metadata.json");
            CargoMetadata withCustomTargetDir = await ReadFixtureAsync("workspace-metadata-custom-target-dir.json");

            Assert.NotEqual(withDefault.TargetDirectory, withCustomTargetDir.TargetDirectory);
            Assert.Contains("custom-target-dir", withCustomTargetDir.TargetDirectory);
            // The workspace itself is unaffected by CARGO_TARGET_DIR.
            Assert.Equal(withDefault.WorkspaceRoot, withCustomTargetDir.WorkspaceRoot);
        }

        [Fact]
        public async Task Declared_dependencies_are_parsed_with_their_kind_and_path()
        {
            CargoMetadata metadata = await ReadFixtureAsync("workspace-metadata.json");

            CargoPackage app = metadata.Packages.Single(p => p.Name == "app");
            CargoDependency dependency = Assert.Single(app.Dependencies);
            Assert.Equal("core-lib", dependency.Name);
            Assert.Equal("*", dependency.Req);
            Assert.Null(dependency.Kind);
            Assert.False(dependency.Optional);
            Assert.NotNull(dependency.Path);
            Assert.EndsWith("core-lib", dependency.Path);

            Assert.Empty(metadata.Packages.Single(p => p.Name == "core-lib").Dependencies);
        }

        [Fact]
        public async Task Unknown_fields_in_the_JSON_do_not_break_parsing()
        {
            // `cargo metadata` output carries many fields this model doesn't map (license,
            // authors, "resolve", "metadata"...); parsing must silently ignore them.
            string[] lines = TestFixtures.ReadAllLines("Metadata", "workspace-metadata.json");
            Assert.Contains("\"authors\"", lines[0]);
            Assert.Contains("\"resolve\"", lines[0]);

            CargoMetadata metadata = await ReadFixtureAsync("workspace-metadata.json");
            Assert.NotEmpty(metadata.Packages);
        }
    }
}
