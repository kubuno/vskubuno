using System.Collections.Generic;
using System.Linq;
using Kubuno.Desktop.Logic.DesignSurface;
using Kubuno.Rust.Cargo.Diagnostics;
using Kubuno.Rust.Cargo.Metadata;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Desktop.Tests.DesignSurface
{
    /// <summary>docs/EVENTS.md EVT-7b: the project crate the design surface links (its controls render for real).</summary>
    [TestClass]
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
            Artifact("ui", "kubuno_desktop_ui", "lib", Deps + @"\libkubuno_ui-1b.rlib", Deps + @"\libkubuno_ui-1b.rmeta"),
            Artifact("views", "kubuno_desktop_views", "lib", Deps + @"\libkubuno_views-8c.rlib", Deps + @"\libkubuno_views-8c.rmeta"),
            Artifact("controls", "kubuno_desktop_controls", "lib", Deps + @"\libkubuno_controls-6d.rlib"),
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
                            Dep("kubuno_desktop_ui", "ui"),
                            Dep("kubuno_desktop_views", "views"),
                            Dep("kubuno_desktop_controls", "controls"),
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

        [TestMethod]
        public void The_crate_root_is_compiled_as_an_rlib_against_the_build_s_own_libraries()
        {
            var crate = DesignProjectCrate.From(Metadata(), Artifacts(), Manifest, null, out var reason);

            Assert.IsNotNull(crate);
            Assert.IsNull(reason);
            Assert.AreEqual("app", crate!.CrateName);
            Assert.AreEqual(@"C:\p\app\src\main.rs", crate.SourcePath);
            Assert.AreEqual(Profile + @"\app.exe", crate.StampFile);
            CollectionAssert.AreEqual(new[] { "kubuno_desktop_ui", "kubuno_desktop_views", "kubuno_desktop_controls", "tracing", "gauges", "my_derive" }, crate.Externs.Select(e => e.Key).ToArray());
            Assert.AreEqual(Deps + @"\libkubuno_ui-1b.rlib", crate.Externs[0].Value);

            var inputs = DesignSurfaceInputs.From(Artifacts(), _ => true, out _) ?? throw new System.InvalidOperationException("inputs");
            var args = crate.RustcArguments(inputs, @"C:\d\libapp.rlib", "debug");
            CollectionAssert.AreEqual(new[] { "--edition", "2021", "--crate-name", "app", "--crate-type", "rlib", @"C:\p\app\src\main.rs" }, args.Take(7).ToArray());
            CollectionAssert.Contains(args.ToList(), "feature=\"fancy\"");
            CollectionAssert.Contains(args.ToList(), "gauges=" + Deps + @"\libgauges-11.rlib");
            CollectionAssert.Contains(args.ToList(), "dependency=" + Deps);
            Assert.AreEqual(@"C:\d\libapp.rlib", args.Last());

            var env = crate.Environment();
            Assert.AreEqual(@"C:\p\app", env["CARGO_MANIFEST_DIR"]);
            Assert.AreEqual("app", env["CARGO_CRATE_NAME"]);
            Assert.AreEqual(("0", "2", "1", "beta.1"), (env["CARGO_PKG_VERSION_MAJOR"], env["CARGO_PKG_VERSION_MINOR"], env["CARGO_PKG_VERSION_PATCH"], env["CARGO_PKG_VERSION_PRE"]));
        }

        [TestMethod]
        public void The_surface_links_the_crate_and_its_other_libraries()
        {
            var crate = DesignProjectCrate.From(Metadata(), Artifacts(), Manifest, "app", out _)!;
            CollectionAssert.AreEqual(new[] { "tracing", "gauges" }, crate.LinkedDependencies.ToArray());
            var include = crate.SurfaceIncludeSource();
            StringAssert.Contains(include, "extern crate app as _;");
            StringAssert.Contains(include, "extern crate gauges as _;");
            Assert.IsFalse(include.Contains("my_derive"));

            var inputs = DesignSurfaceInputs.From(Artifacts(), _ => true, out _)!;
            var args = DesignSurfaceBuilder.RustcArgumentsFor(inputs, @"C:\d\surface.exe", "debug", crate, @"C:\d\libapp.rlib");
            CollectionAssert.Contains(args.ToList(), "kubuno_design_project");
            CollectionAssert.Contains(args.ToList(), @"app=C:\d\libapp.rlib");
            CollectionAssert.Contains(args.ToList(), "gauges=" + Deps + @"\libgauges-11.rlib");
            CollectionAssert.AreEqual(new[] { "-o", @"C:\d\surface.exe" }, args.Skip(args.Count - 2).ToArray());
            CollectionAssert.AreEqual(DesignSurfaceBuilder.RustcArgumentsFor(inputs, "x.exe", "debug").ToArray(), DesignSurfaceBuilder.RustcArgumentsFor(inputs, "x.exe", "debug", null, null).ToArray());
        }

        [TestMethod]
        public void The_build_scripts_library_paths_reach_the_surface_link()
        {
            var crate = DesignProjectCrate.From(Metadata(), Artifacts(), Manifest, "app", out _)!;
            crate.NativeLinkPaths = new[] { @"native=C:\t\debug\build\windows_x86_64_msvc-1\out\lib" };
            var inputs = DesignSurfaceInputs.From(Artifacts(), _ => true, out _)!;
            var args = DesignSurfaceBuilder.RustcArgumentsFor(inputs, @"C:\d\surface.exe", "debug", crate, @"C:\d\libapp.rlib").ToList();
            var at = args.IndexOf(@"native=C:\t\debug\build\windows_x86_64_msvc-1\out\lib");
            Assert.IsTrue(at > 0 && args[at - 1] == "-L");
            CollectionAssert.AreEqual(new[] { "-o", @"C:\d\surface.exe" }, args.Skip(args.Count - 2).ToArray());
        }

        [TestMethod]
        public void A_package_with_a_library_target_links_the_library()
        {
            var lib = new CargoArtifact
            {
                PackageId = AppId,
                Target = new CargoTarget { Name = "app_core", Kind = new[] { "lib" }, SrcPath = @"C:\p\app\src\lib.rs", Edition = "2021" },
                Filenames = new[] { Deps + @"\libapp_core-5f.rlib", Deps + @"\libapp_core-5f.rmeta" },
            };
            var crate = DesignProjectCrate.From(Metadata(), Artifacts().Concat(new[] { lib }).ToArray(), Manifest, "app", out var reason);

            Assert.IsNotNull(crate, reason);
            Assert.AreEqual("app_core", crate!.CrateName);
            Assert.AreEqual(@"C:\p\app\src\lib.rs", crate.SourcePath);
            Assert.AreEqual(Deps + @"\libapp_core-5f.rlib", crate.StampFile);
            StringAssert.Contains(crate.SurfaceIncludeSource(), "extern crate app_core as _;");
            var inputs = DesignSurfaceInputs.From(Artifacts(), _ => true, out _)!;
            CollectionAssert.AreEqual(new[] { "--edition", "2021", "--crate-name", "app_core", "--crate-type", "rlib", @"C:\p\app\src\lib.rs" }, crate.RustcArguments(inputs, @"C:\d\libapp_core.rlib", "debug").Take(7).ToArray());
        }

        [TestMethod]
        public void Without_its_program_or_a_library_there_is_no_crate_to_link()
        {
            var noBin = Artifacts().Where(a => a.PackageId != AppId).ToArray();
            Assert.IsNull(DesignProjectCrate.From(Metadata(), noBin, Manifest, null, out var reason));
            StringAssert.Contains(reason, "not built");

            var noLib = Artifacts().Where(a => a.PackageId != "gauges").ToArray();
            Assert.IsNull(DesignProjectCrate.From(Metadata(), noLib, Manifest, null, out reason));
            StringAssert.Contains(reason, "gauges");

            Assert.IsNull(DesignProjectCrate.From(Metadata(), Artifacts(), @"C:\other\Cargo.toml", null, out reason));
            StringAssert.Contains(reason, "metadata");
        }
    }
}
