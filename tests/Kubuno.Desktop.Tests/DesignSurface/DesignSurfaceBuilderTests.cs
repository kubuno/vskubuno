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
    /// <summary>docs/DESIGNER.md section 15: the design build of the .kbview designer's surface.</summary>
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

        // Shapes from a real `cargo build --message-format json` of a Kubuno Desktop Application (29/09/2026).
        private static CargoArtifact[] RealArtifacts() => new[]
        {
            Artifact("kubuno_controls", "lib", Deps + @"\libkubuno_controls-6de35b5c49a7da48.rlib", Deps + @"\libkubuno_controls-6de35b5c49a7da48.rmeta"),
            Artifact("kubuno_ui", "dylib", Profile + @"\kubuno_ui.dll", Profile + @"\kubuno_ui.dll.lib", Profile + @"\kubuno_ui.dll.exp", Profile + @"\kubuno_ui.pdb"),
            Artifact("kubuno_views", "lib", Deps + @"\libkubuno_views-8cdbad53981d1b62.rlib", Deps + @"\libkubuno_views-8cdbad53981d1b62.rmeta"),
            Artifact("app", "bin", Profile + @"\app.exe", Profile + @"\app.pdb"),
        };

        [TestMethod]
        public void Inputs_pick_the_exact_artifacts_and_the_deps_copy_of_the_dll()
        {
            var inputs = DesignSurfaceInputs.From(RealArtifacts(), _ => true, out var reason);

            Assert.IsNotNull(inputs);
            Assert.IsNull(reason);
            Assert.AreEqual(Deps + @"\kubuno_ui.dll", inputs!.UiDll);
            Assert.AreEqual(Deps + @"\libkubuno_views-8cdbad53981d1b62.rlib", inputs.ViewsRlib);
            Assert.AreEqual(Deps + @"\libkubuno_controls-6de35b5c49a7da48.rlib", inputs.ControlsRlib);
            Assert.AreEqual(Deps, inputs.DepsDirectory);
            Assert.AreEqual(Profile, inputs.ProfileDirectory);
            Assert.AreEqual(@"C:\t\rsproj\app", inputs.TargetDirectory);
            Assert.AreEqual(Views + @"\examples\view_embed.rs", inputs.SurfaceSource);
        }

        [TestMethod]
        public void Inputs_fall_back_to_the_uplifted_dll()
        {
            var inputs = DesignSurfaceInputs.From(RealArtifacts(), path => !path.EndsWith(@"deps\kubuno_ui.dll"), out _);
            Assert.AreEqual(Profile + @"\kubuno_ui.dll", inputs!.UiDll);
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
        public void Rustc_links_the_three_kubuno_crates_by_path_and_the_rest_through_deps()
        {
            var inputs = DesignSurfaceInputs.From(RealArtifacts(), _ => true, out _)!;

            var args = DesignSurfaceBuilder.RustcArgumentsFor(inputs, @"C:\out\kubuno-design-surface.exe", "debug");

            CollectionAssert.Contains(args.ToList(), "prefer-dynamic");
            CollectionAssert.Contains(args.ToList(), "opt-level=0");
            CollectionAssert.Contains(args.ToList(), "dependency=" + Deps);
            CollectionAssert.Contains(args.ToList(), "kubuno_ui=" + Deps + @"\kubuno_ui.dll");
            CollectionAssert.Contains(args.ToList(), "kubuno_views=" + Deps + @"\libkubuno_views-8cdbad53981d1b62.rlib");
            CollectionAssert.Contains(args.ToList(), "kubuno_controls=" + Deps + @"\libkubuno_controls-6de35b5c49a7da48.rlib");
            Assert.AreEqual(Views + @"\examples\view_embed.rs", args[6]);
            Assert.AreEqual(@"C:\out\kubuno-design-surface.exe", args[args.Count - 1]);
            CollectionAssert.Contains(DesignSurfaceBuilder.RustcArgumentsFor(inputs, "x.exe", "release").ToList(), "opt-level=3");
        }

        [TestMethod]
        public void Key_changes_with_every_input()
        {
            var inputs = new[] { new DesignSurfaceStampInput { Path = "a", Length = 1, LastWriteUtcTicks = 2 } };
            var key = DesignSurfaceBuilder.ComputeKey("AB", inputs, "rustc 1.98.1", "debug");

            Assert.AreEqual(16, key.Length);
            Assert.AreEqual(key, DesignSurfaceBuilder.ComputeKey("AB", inputs, "rustc 1.98.1", "debug"));
            Assert.AreNotEqual(key, DesignSurfaceBuilder.ComputeKey("AC", inputs, "rustc 1.98.1", "debug"));
            Assert.AreNotEqual(key, DesignSurfaceBuilder.ComputeKey("AB", new[] { new DesignSurfaceStampInput { Path = "a", Length = 1, LastWriteUtcTicks = 3 } }, "rustc 1.98.1", "debug"));
            Assert.AreNotEqual(key, DesignSurfaceBuilder.ComputeKey("AB", inputs, "rustc 1.99.0", "debug"));
            Assert.AreNotEqual(key, DesignSurfaceBuilder.ComputeKey("AB", inputs, "rustc 1.98.1", "release"));
        }

        [TestMethod]
        public void Stamp_round_trips_and_detects_a_changed_input()
        {
            var stamp = new DesignSurfaceStamp
            {
                Key = "0123456789abcdef",
                UiDllSha256 = "AB",
                UiDllSource = @"C:\d\kubuno_ui.dll",
                Inputs = { new DesignSurfaceStampInput { Path = @"C:\d\kubuno_ui.dll", Length = 10, LastWriteUtcTicks = 20 } },
            };

            var parsed = DesignSurfaceStamp.TryParse(stamp.ToJson());

            Assert.IsNotNull(parsed);
            Assert.AreEqual("0123456789abcdef", parsed!.Key);
            Assert.IsTrue(parsed.InputsUnchanged(_ => (10, 20)));
            Assert.IsFalse(parsed.InputsUnchanged(_ => (11, 20)));
            Assert.IsFalse(parsed.InputsUnchanged(_ => null));
            Assert.IsNull(DesignSurfaceStamp.TryParse("{}"));
            Assert.IsNull(DesignSurfaceStamp.TryParse("not json"));
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
