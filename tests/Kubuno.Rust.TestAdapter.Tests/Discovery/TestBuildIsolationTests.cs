using System.Collections.Generic;
using System.Linq;
using Kubuno.Rust.Cargo.Metadata;
using Kubuno.Rust.TestAdapter.Discovery;

namespace Kubuno.Rust.TestAdapter.Tests.Discovery
{
    /// <summary>
    /// The Kubuno desktop workspace's former shape (before kubuno-ui became an rlib on 2026-10-03): kubuno-views-macros (a proc-macro) has kubuno-views as a dev-dependency, which uses
    /// the kubuno-ui dylib; `cargo test --workspace` then builds kubuno_ui.dll twice into the same file.
    /// </summary>
    public class TestBuildIsolationTests
    {
        private static CargoPackage Package(string name, string kind, params (string Name, string? Kind)[] dependencies) => new()
        {
            Id = "path+file:///ws/" + name + "#0.1.0",
            Name = name,
            ManifestPath = @"C:\ws\" + name + @"\Cargo.toml",
            Targets = new[] { new CargoTarget { Name = name.Replace('-', '_'), Kind = new[] { kind }, CrateTypes = new[] { kind == "proc-macro" ? "proc-macro" : kind } } },
            Dependencies = dependencies.Select(d => new CargoDependency { Name = d.Name, Kind = d.Kind, Path = @"C:\ws\" + d.Name }).ToList(),
        };

        private static CargoMetadata Metadata(params CargoPackage[] packages) => new()
        {
            Packages = packages,
            WorkspaceMembers = packages.Select(p => p.Id).ToList(),
            TargetDirectory = @"C:\target",
        };

        [Fact]
        public void A_proc_macro_whose_tests_reach_the_dylib_is_isolated()
        {
            var metadata = Metadata(
                Package("kubuno-ui", "dylib"),
                Package("kubuno-views", "lib", ("kubuno-ui", null), ("kubuno-views-macros", null)),
                Package("kubuno-views-macros", "proc-macro", ("kubuno-views-meta", null), ("kubuno-views", "dev")),
                Package("kubuno-views-meta", "lib"),
                Package("kubuno-data-macros", "proc-macro", ("kubuno-data-model", null)),
                Package("kubuno-data-model", "lib"),
                Package("app", "bin", ("kubuno-views", null)));

            Assert.Equal(new[] { "kubuno-views-macros" }, TestBuildIsolation.PackagesToIsolate(metadata));
        }

        [Fact]
        public void Nothing_is_isolated_without_a_dylib()
        {
            var metadata = Metadata(
                Package("core", "lib"),
                Package("macros", "proc-macro", ("core", "dev")));

            Assert.Empty(TestBuildIsolation.PackagesToIsolate(metadata));
        }

        [Fact]
        public void A_registry_dependency_named_like_a_member_is_not_followed()
        {
            var ui = Package("ui", "dylib");
            var macros = Package("macros", "proc-macro");
            macros.Dependencies = new List<CargoDependency> { new() { Name = "ui", Kind = "dev", Path = null } };

            Assert.Empty(TestBuildIsolation.PackagesToIsolate(Metadata(ui, macros)));
        }

        [Fact]
        public void Each_isolated_package_gets_its_own_target_directory()
        {
            var plan = new TestBuildIsolation.Plan(new[] { "a" }, @"C:\target");

            Assert.Equal(@"C:\target\kubuno-isolated-tests\a", plan.TargetDirectoryFor("a"));
            Assert.Equal(@"C:\target\kubuno-tests", plan.WorkspaceTestTargetDirectory);
        }

        [Fact]
        public void Only_a_workspace_with_a_dylib_member_needs_a_plan()
        {
            Assert.True(TestBuildIsolation.HasDylibMember(Metadata(Package("ui", "dylib"), Package("app", "bin", ("ui", null)))));
            Assert.False(TestBuildIsolation.HasDylibMember(Metadata(Package("core", "lib"), Package("app", "bin", ("core", null)))));
        }
    }
}
