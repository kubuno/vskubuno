using System.Collections.Generic;
using System.Linq;
using Kubuno.Cargo.DesignSurface;
using Kubuno.Cargo.Diagnostics;
using Kubuno.Cargo.Metadata;

namespace Kubuno.Cargo.Tests.DesignSurface
{
    /// <summary>docs/EVENTS.md EVT-7b: the project crate the design surface links (its controls render for real).</summary>
    public class DesignProjectCrateTests
    {
        private const string Profile = @"C:\t\rsproj\app\debug";
        private const string Deps = Profile + @"\deps";
        private const string Manifest = @"C:\p\app\Cargo.toml";
        private const string AppId = "path+file:///C:/p/app#app@0.1.0";

        private static CargoArtifact Artifact(string id, string name, string kind, params string[] files) => new CargoArtifact
        {
            PackageId = id,
            Target = new CargoTarget { Name = name, Kind = new[] { kind }, SrcPath = name == "app" ? @"C:\p\app\src\main.rs" : @"Z:\x\src\lib.rs", Edition = "2021" },
            Filenames = files,
        };

        private static CargoArtifact[] Artifacts() => new[]
        {
            Artifact("ui", "kubuno_ui", "dylib", Profile + @"\kubuno_ui.dll", Profile + @"\kubuno_ui.dll.lib"),
            Artifact("views", "kubuno_views", "lib", Deps + @"\libkubuno_views-8c.rlib", Deps + @"\libkubuno_views-8c.rmeta"),
            Artifact("controls", "kubuno_controls", "lib", Deps + @"\libkubuno_controls-6d.rlib"),
            Artifact("tracing", "tracing", "lib", Deps + @"\libtracing-a3.rlib"),
            Artifact("gauges", "gauges", "lib", Deps + @"\libgauges-11.rlib"),
            Artifact("derive", "my_derive", "proc-macro", Deps + @"\my_derive-22.dll"),
            new CargoArtifact
            {
                PackageId = AppId,
                Target = new CargoTarget { Name = "app", Kind = new[] { "bin" }, SrcPath = @"C:\p\app\src\main.rs", Edition = "2021" },
                Filenames = new[] { Profile + @"\app.exe", Profile + @"\app.pdb" },
                Executable = Profile + @"\app.exe",
            },
        };

        private static CargoMetadata Metadata() => new CargoMetadata
        {
            Packages = new[] { new CargoPackage { Id = AppId, Name = "app", Version = "0.2.1-beta.1", ManifestPath = Manifest, Authors = new[] { "Kubuno" } } },
            Resolve = new CargoResolve
            {
                Nodes = new[]
                {
                    new CargoResolveNode
                    {
                        Id = AppId,
                        Features = new[] { "fancy" },
                        Deps = new[]
                        {
                            Dep("kubuno_ui", "ui"),
                            Dep("kubuno_views", "views"),
                            Dep("kubuno_controls", "controls"),
                            Dep("tracing", "tracing"),
                            Dep("gauges", "gauges"),
                            Dep("my_derive", "derive"),
                            new CargoResolveDep { Name = "tempfile", Pkg = "tempfile", DepKinds = new[] { new CargoDepKindInfo { Kind = "dev" } } },
                        },
                    },
                },
            },
        };

        private static CargoResolveDep Dep(string name, string pkg) => new CargoResolveDep { Name = name, Pkg = pkg, DepKinds = new[] { new CargoDepKindInfo { Kind = null } } };

        [Fact]
        public void The_crate_root_is_compiled_as_an_rlib_against_the_build_s_own_libraries()
        {
            var crate = DesignProjectCrate.From(Metadata(), Artifacts(), Manifest, null, out var reason);

            Assert.NotNull(crate);
            Assert.Null(reason);
            Assert.Equal("app", crate!.CrateName);
            Assert.Equal(@"C:\p\app\src\main.rs", crate.SourcePath);
            Assert.Equal(Profile + @"\app.exe", crate.StampFile);
            Assert.Equal(new[] { "kubuno_ui", "kubuno_views", "kubuno_controls", "tracing", "gauges", "my_derive" }, crate.Externs.Select(e => e.Key));
            Assert.Equal(Deps + @"\kubuno_ui.dll", crate.Externs[0].Value);

            var inputs = DesignSurfaceInputs.From(Artifacts(), _ => true, out _) ?? throw new System.InvalidOperationException("inputs");
            var args = crate.RustcArguments(inputs, @"C:\d\libapp.rlib", "debug");
            Assert.Equal(new[] { "--edition", "2021", "--crate-name", "app", "--crate-type", "rlib", @"C:\p\app\src\main.rs" }, args.Take(7));
            Assert.Contains("feature=\"fancy\"", args);
            Assert.Contains("gauges=" + Deps + @"\libgauges-11.rlib", args);
            Assert.Contains("dependency=" + Deps, args);
            Assert.Equal(@"C:\d\libapp.rlib", args.Last());

            var env = crate.Environment();
            Assert.Equal(@"C:\p\app", env["CARGO_MANIFEST_DIR"]);
            Assert.Equal("app", env["CARGO_CRATE_NAME"]);
            Assert.Equal(("0", "2", "1", "beta.1"), (env["CARGO_PKG_VERSION_MAJOR"], env["CARGO_PKG_VERSION_MINOR"], env["CARGO_PKG_VERSION_PATCH"], env["CARGO_PKG_VERSION_PRE"]));
        }

        [Fact]
        public void The_surface_links_the_crate_and_its_other_libraries()
        {
            var crate = DesignProjectCrate.From(Metadata(), Artifacts(), Manifest, "app", out _)!;
            Assert.Equal(new[] { "tracing", "gauges" }, crate.LinkedDependencies);
            var include = crate.SurfaceIncludeSource();
            Assert.Contains("extern crate app as _;", include);
            Assert.Contains("extern crate gauges as _;", include);
            Assert.DoesNotContain("my_derive", include);

            var inputs = DesignSurfaceInputs.From(Artifacts(), _ => true, out _)!;
            var args = DesignSurfaceBuilder.RustcArgumentsFor(inputs, @"C:\d\surface.exe", "debug", crate, @"C:\d\libapp.rlib");
            Assert.Contains("kubuno_design_project", args);
            Assert.Contains(@"app=C:\d\libapp.rlib", args);
            Assert.Contains("gauges=" + Deps + @"\libgauges-11.rlib", args);
            Assert.Equal(new[] { "-o", @"C:\d\surface.exe" }, args.Skip(args.Count - 2));
            Assert.Equal(DesignSurfaceBuilder.RustcArgumentsFor(inputs, "x.exe", "debug"), DesignSurfaceBuilder.RustcArgumentsFor(inputs, "x.exe", "debug", null, null));
        }

        [Fact]
        public void Without_its_program_or_a_library_there_is_no_crate_to_link()
        {
            var noBin = Artifacts().Where(a => a.PackageId != AppId).ToArray();
            Assert.Null(DesignProjectCrate.From(Metadata(), noBin, Manifest, null, out var reason));
            Assert.Contains("not built", reason);

            var noLib = Artifacts().Where(a => a.PackageId != "gauges").ToArray();
            Assert.Null(DesignProjectCrate.From(Metadata(), noLib, Manifest, null, out reason));
            Assert.Contains("gauges", reason);

            Assert.Null(DesignProjectCrate.From(Metadata(), Artifacts(), @"C:\other\Cargo.toml", null, out reason));
            Assert.Contains("metadata", reason);
        }
    }
}
