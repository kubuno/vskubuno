using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Desktop.Logic.DesignSurface;
using Kubuno.Rust.Cargo.Diagnostics;
using Kubuno.Rust.Cargo.Metadata;
using Kubuno.Rust.Cargo.Processes;
using Kubuno.Rust.Cargo.Tests.Fakes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Desktop.Tests.DesignSurface
{
    /// <summary>docs/DESIGNER.md sections 15-16: the design build of the .kbview designer's surface, linked statically.</summary>
    [TestClass]
    public class DesignSurfaceBuilderTests
    {
        private const string Profile = @"C:\t\rsproj\app\debug";
        private const string Deps = Profile + @"\deps";
        private const string Views = @"Z:\desktop\src\crates\kubuno-views";

        private static CargoArtifact Artifact(string name, string kind, params string[] files) => new CargoArtifact
        {
            PackageId = "path+file:///x#" + name,
            Target = new CargoTarget { Name = name, Kind = new[] { kind }, SrcPath = name == "kubuno_views" ? Views + @"\src\lib.rs" : @"Z:\x\src\lib.rs" },
            Filenames = files,
        };

        // Shapes from a real `cargo build --message-format json` of a Kubuno Desktop Application (kubuno-ui an rlib, 03/10/2026).
        private static CargoArtifact[] RealArtifacts() => new[]
        {
            Artifact("kubuno_controls", "lib", Deps + @"\libkubuno_controls-6de35b5c49a7da48.rlib", Deps + @"\libkubuno_controls-6de35b5c49a7da48.rmeta"),
            Artifact("kubuno_ui", "lib", Deps + @"\libkubuno_ui-1b2c3d4e5f607182.rlib", Deps + @"\libkubuno_ui-1b2c3d4e5f607182.rmeta"),
            Artifact("kubuno_views", "lib", Deps + @"\libkubuno_views-8cdbad53981d1b62.rlib", Deps + @"\libkubuno_views-8cdbad53981d1b62.rmeta"),
            Artifact("app", "bin", Profile + @"\app.exe", Profile + @"\app.pdb"),
        };

        [TestMethod]
        public void Inputs_pick_the_exact_rlibs()
        {
            var inputs = DesignSurfaceInputs.From(RealArtifacts(), _ => true, out var reason);

            Assert.IsNotNull(inputs);
            Assert.IsNull(reason);
            Assert.AreEqual(Deps + @"\libkubuno_ui-1b2c3d4e5f607182.rlib", inputs!.UiRlib);
            Assert.AreEqual(Deps + @"\libkubuno_views-8cdbad53981d1b62.rlib", inputs.ViewsRlib);
            Assert.AreEqual(Deps + @"\libkubuno_controls-6de35b5c49a7da48.rlib", inputs.ControlsRlib);
            Assert.AreEqual(Deps, inputs.DepsDirectory);
            Assert.AreEqual(Profile, inputs.ProfileDirectory);
            Assert.AreEqual(@"C:\t\rsproj\app", inputs.TargetDirectory);
            Assert.AreEqual(Views + @"\examples\view_embed.rs", inputs.SurfaceSource);
        }

        [TestMethod]
        public void Inputs_refuse_a_kubuno_ui_still_built_as_a_dylib()
        {
            // A desktop checkout older than 2026-10-03: kubuno-ui was a Rust dylib (kubuno_ui.dll).
            var artifacts = RealArtifacts().Where(a => a.Target.Name != "kubuno_ui")
                .Append(Artifact("kubuno_ui", "dylib", Profile + @"\kubuno_ui.dll", Profile + @"\kubuno_ui.dll.lib", Profile + @"\kubuno_ui.pdb"));

            Assert.IsNull(DesignSurfaceInputs.From(artifacts, _ => true, out var reason));
            StringAssert.Contains(reason, "dylib");
            StringAssert.Contains(reason, "update the desktop checkout");
        }

        [TestMethod]
        public void Inputs_explain_a_project_without_kubuno_views_or_without_a_surface()
        {
            Assert.IsNull(DesignSurfaceInputs.From(new[] { Artifact("app", "bin", Profile + @"\app.exe") }, _ => true, out var reason));
            StringAssert.Contains(reason, "kubuno-views");

            Assert.IsNull(DesignSurfaceInputs.From(RealArtifacts(), path => !path.EndsWith("view_embed.rs"), out reason));
            StringAssert.Contains(reason, "no design surface");
        }

        [TestMethod]
        public void Cargo_command_is_the_sdk_build_command()
        {
            var project = new DesignSurfaceProject(@"C:\p\app\Cargo.toml", "release")
            {
                Package = "app",
                Bin = "app",
                ExtraArgs = DesignSurfaceProject.SplitExtraArgs("--locked; ;--offline"),
            };

            var line = DesignSurfaceBuilder.CargoCommandFor(project);

            Assert.AreEqual("cargo", line.FileName);
            CollectionAssert.AreEqual(
                new[] { "build", "--manifest-path", @"C:\p\app\Cargo.toml", "-p", "app", "--bin", "app", "--release", "--message-format", "json-diagnostic-rendered-ansi", "--locked", "--offline" },
                line.ArgumentList.ToArray());
        }

        [TestMethod]
        public void Rustc_links_the_three_kubuno_crates_by_path_statically_and_the_rest_through_deps()
        {
            var inputs = DesignSurfaceInputs.From(RealArtifacts(), _ => true, out _)!;

            var args = DesignSurfaceBuilder.RustcArgumentsFor(inputs, @"C:\out\kubuno-design-surface.exe", "debug");

            CollectionAssert.DoesNotContain(args.ToList(), "prefer-dynamic", "kubuno_ui and std are linked statically");
            CollectionAssert.Contains(args.ToList(), "opt-level=0");
            CollectionAssert.Contains(args.ToList(), "dependency=" + Deps);
            CollectionAssert.Contains(args.ToList(), "kubuno_ui=" + Deps + @"\libkubuno_ui-1b2c3d4e5f607182.rlib");
            CollectionAssert.Contains(args.ToList(), "kubuno_views=" + Deps + @"\libkubuno_views-8cdbad53981d1b62.rlib");
            CollectionAssert.Contains(args.ToList(), "kubuno_controls=" + Deps + @"\libkubuno_controls-6de35b5c49a7da48.rlib");
            Assert.IsFalse(args.Any(a => a.EndsWith(".dll", System.StringComparison.OrdinalIgnoreCase)), string.Join(" ", args));
            Assert.AreEqual(Views + @"\examples\view_embed.rs", args[6]);
            Assert.AreEqual(@"C:\out\kubuno-design-surface.exe", args[args.Count - 1]);
            CollectionAssert.Contains(DesignSurfaceBuilder.RustcArgumentsFor(inputs, "x.exe", "release").ToList(), "opt-level=3");
        }

        [TestMethod]
        public void Rustc_gets_the_native_search_paths_of_the_graph_once_each()
        {
            var inputs = DesignSurfaceInputs.From(RealArtifacts(), _ => true, out _)!;

            var args = DesignSurfaceBuilder.RustcArgumentsFor(inputs, @"C:\out\s.exe", "debug", null, null, new[] { @"C:\lib\a", @"C:\lib\b", @"c:\LIB\A" }).ToList();

            CollectionAssert.AreEqual(new[] { @"C:\lib\a", @"C:\lib\b" }, args.Select((a, i) => (a, i)).Where(x => x.a == "-L" && !args[x.i + 1].StartsWith("dependency=")).Select(x => args[x.i + 1]).ToArray());
            Assert.AreEqual(@"C:\out\s.exe", args[args.Count - 1]);
            Assert.AreEqual("-o", args[args.Count - 2]);
        }

        [TestMethod]
        public void Key_changes_with_every_input()
        {
            var inputs = new[] { new DesignSurfaceStampInput { Path = "a", Length = 1, LastWriteUtcTicks = 2 } };
            var key = DesignSurfaceBuilder.ComputeKey(inputs, "rustc 1.98.1", "debug");

            Assert.AreEqual(16, key.Length);
            Assert.AreEqual(key, DesignSurfaceBuilder.ComputeKey(inputs, "rustc 1.98.1", "debug"));
            Assert.AreNotEqual(key, DesignSurfaceBuilder.ComputeKey(new[] { new DesignSurfaceStampInput { Path = "a", Length = 1, LastWriteUtcTicks = 3 } }, "rustc 1.98.1", "debug"));
            Assert.AreNotEqual(key, DesignSurfaceBuilder.ComputeKey(new[] { new DesignSurfaceStampInput { Path = "b", Length = 1, LastWriteUtcTicks = 2 } }, "rustc 1.98.1", "debug"));
            Assert.AreNotEqual(key, DesignSurfaceBuilder.ComputeKey(inputs, "rustc 1.99.0", "debug"));
            Assert.AreNotEqual(key, DesignSurfaceBuilder.ComputeKey(inputs, "rustc 1.98.1", "release"));
        }

        [TestMethod]
        public void Stamp_round_trips_and_detects_a_changed_input()
        {
            var stamp = new DesignSurfaceStamp
            {
                Key = "0123456789abcdef",
                Inputs = { new DesignSurfaceStampInput { Path = @"C:\d\libkubuno_ui-1b2c3d4e5f607182.rlib", Length = 10, LastWriteUtcTicks = 20 } },
                ProjectCrate = "app",
            };

            var parsed = DesignSurfaceStamp.TryParse(stamp.ToJson());

            Assert.IsNotNull(parsed);
            Assert.AreEqual("0123456789abcdef", parsed!.Key);
            Assert.AreEqual("app", parsed.ProjectCrate);
            Assert.IsTrue(parsed.InputsUnchanged(_ => (10, 20)));
            Assert.IsFalse(parsed.InputsUnchanged(_ => (11, 20)));
            Assert.IsFalse(parsed.InputsUnchanged(_ => null));
            Assert.IsNull(DesignSurfaceStamp.TryParse("{}"));
            Assert.IsNull(DesignSurfaceStamp.TryParse("not json"));
        }

        [TestMethod]
        public void Stamps_of_dll_surfaces_are_not_reused()
        {
            var stamp = new DesignSurfaceStamp { Key = "0123456789abcdef", Version = 3 };

            Assert.IsNull(DesignSurfaceStamp.TryParse(stamp.ToJson()), "a version 3 surface loaded a kubuno_ui DLL: rebuilt statically");
            Assert.IsNull(DesignSurfaceStamp.TryParse(@"{""Version"":3,""Key"":""0123456789abcdef"",""UiDllSha256"":""AB"",""UiDllFileName"":""kubuno_ui-0123456789abcdef.dll""}"));
        }

        [TestMethod]
        public void A_project_is_built_once_its_deps_hold_a_kubuno_ui_rlib()
        {
            var target = Path.Combine(Path.GetTempPath(), "kubuno-dsb-" + System.Guid.NewGuid().ToString("N"));
            var deps = Path.Combine(target, "debug", "deps");
            Directory.CreateDirectory(deps);
            try
            {
                var project = new DesignSurfaceProject(@"C:\p\app\Cargo.toml", "debug") { TargetDirectory = target };
                Assert.IsFalse(DesignSurfaceBuilder.IsProjectBuilt(project));

                File.WriteAllText(Path.Combine(deps, "kubuno_ui.dll"), string.Empty);
                Assert.IsFalse(DesignSurfaceBuilder.IsProjectBuilt(project), "a dylib build is not a static kubuno_ui");

                File.WriteAllText(Path.Combine(deps, "libkubuno_ui-1b2c3d4e5f607182.rlib"), string.Empty);
                Assert.IsTrue(DesignSurfaceBuilder.IsProjectBuilt(project));
            }
            finally
            {
                Directory.Delete(target, recursive: true);
            }
        }

        [TestMethod]
        public async Task A_project_without_kubuno_views_is_not_applicable()
        {
            var runner = new FakeProcessRunner(new ProcessRunResult(0, new[] { @"{""reason"":""build-finished"",""success"":true}" }, new string[0]));
            var project = new DesignSurfaceProject(Path.Combine(Path.GetTempPath(), "none", "Cargo.toml"), "debug");

            var result = await new DesignSurfaceBuilder(runner).BuildAsync(project, null, CancellationToken.None);

            Assert.AreEqual(DesignSurfaceBuildStatus.NotApplicable, result.Status);
            StringAssert.StartsWith(runner.LastRequest!.Arguments, "build --manifest-path");
        }

        [TestMethod]
        public async Task A_failed_project_build_without_inputs_fails()
        {
            var runner = new FakeProcessRunner(new ProcessRunResult(101, new string[0], new[] { "error: could not compile `app`" }));
            var project = new DesignSurfaceProject(Path.Combine(Path.GetTempPath(), "none", "Cargo.toml"), "debug") { TargetDirectory = @"C:\t" };

            var result = await new DesignSurfaceBuilder(runner).BuildAsync(project, null, CancellationToken.None);

            Assert.AreEqual(DesignSurfaceBuildStatus.Failed, result.Status);
            Assert.AreEqual(@"C:\t", runner.LastRequest!.EnvironmentVariables!["CARGO_TARGET_DIR"]);
        }

        [TestMethod]
        public void Profile_folder_and_identity_follow_the_project_properties()
        {
            var project = new DesignSurfaceProject(@"C:\p\app\Cargo.toml", "release") { TargetDirectory = @"C:\t\rsproj\app" };

            Assert.AreEqual("release", project.EffectiveProfileDirectoryName);
            Assert.AreEqual(@"C:\t\rsproj\app", project.EffectiveExpectedTargetDirectory);
            Assert.AreEqual(@"C:\t\rsproj\app\kubuno-design\release", DesignSurfaceBuilder.DesignDirectory(project.EffectiveExpectedTargetDirectory, project.EffectiveProfileDirectoryName));
            Assert.AreNotEqual(project.Identity, new DesignSurfaceProject(@"C:\p\app\Cargo.toml", "debug").Identity);
            Assert.AreEqual(@"C:\p\app\target", new DesignSurfaceProject(@"C:\p\app\Cargo.toml", "debug").EffectiveExpectedTargetDirectory);
        }
    }
}
