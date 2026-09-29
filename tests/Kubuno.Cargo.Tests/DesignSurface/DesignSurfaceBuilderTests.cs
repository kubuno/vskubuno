using System.IO;
using Kubuno.Cargo.DesignSurface;
using Kubuno.Cargo.Diagnostics;
using Kubuno.Cargo.Metadata;
using Kubuno.Cargo.Processes;
using Kubuno.Cargo.Tests.Fakes;

namespace Kubuno.Cargo.Tests.DesignSurface
{
    /// <summary>docs/DESIGNER.md section 15: the design build of the .kbview designer's surface.</summary>
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

        [Fact]
        public void Inputs_pick_the_exact_artifacts_and_the_deps_copy_of_the_dll()
        {
            var inputs = DesignSurfaceInputs.From(RealArtifacts(), _ => true, out var reason);

            Assert.NotNull(inputs);
            Assert.Null(reason);
            Assert.Equal(Deps + @"\kubuno_ui.dll", inputs!.UiDll);
            Assert.Equal(Deps + @"\libkubuno_views-8cdbad53981d1b62.rlib", inputs.ViewsRlib);
            Assert.Equal(Deps + @"\libkubuno_controls-6de35b5c49a7da48.rlib", inputs.ControlsRlib);
            Assert.Equal(Deps, inputs.DepsDirectory);
            Assert.Equal(Profile, inputs.ProfileDirectory);
            Assert.Equal(@"C:\t\rsproj\app", inputs.TargetDirectory);
            Assert.Equal(Views + @"\examples\view_embed.rs", inputs.SurfaceSource);
        }

        [Fact]
        public void Inputs_fall_back_to_the_uplifted_dll()
        {
            var inputs = DesignSurfaceInputs.From(RealArtifacts(), path => !path.EndsWith(@"deps\kubuno_ui.dll"), out _);
            Assert.Equal(Profile + @"\kubuno_ui.dll", inputs!.UiDll);
        }

        [Fact]
        public void Inputs_explain_a_project_without_kubuno_views_or_without_a_surface()
        {
            Assert.Null(DesignSurfaceInputs.From(new[] { Artifact("app", "bin", Profile + @"\app.exe") }, _ => true, out var reason));
            Assert.Contains("kubuno-views", reason);

            Assert.Null(DesignSurfaceInputs.From(RealArtifacts(), path => !path.EndsWith("view_embed.rs"), out reason));
            Assert.Contains("no design surface", reason);
        }

        [Fact]
        public void Cargo_command_is_the_sdk_build_command()
        {
            var project = new DesignSurfaceProject(@"C:\p\app\Cargo.toml", "release")
            {
                Package = "app",
                Bin = "app",
                ExtraArgs = DesignSurfaceProject.SplitExtraArgs("--locked; ;--offline"),
            };

            var line = DesignSurfaceBuilder.CargoCommandFor(project);

            Assert.Equal("cargo", line.FileName);
            Assert.Equal(
                new[] { "build", "--manifest-path", @"C:\p\app\Cargo.toml", "-p", "app", "--bin", "app", "--release", "--message-format", "json-diagnostic-rendered-ansi", "--locked", "--offline" },
                line.ArgumentList);
        }

        [Fact]
        public void Rustc_links_the_three_kubuno_crates_by_path_and_the_rest_through_deps()
        {
            var inputs = DesignSurfaceInputs.From(RealArtifacts(), _ => true, out _)!;

            var args = DesignSurfaceBuilder.RustcArgumentsFor(inputs, @"C:\out\kubuno-design-surface.exe", "debug");

            Assert.Contains("prefer-dynamic", args);
            Assert.Contains("opt-level=0", args);
            Assert.Contains("dependency=" + Deps, args);
            Assert.Contains("kubuno_ui=" + Deps + @"\kubuno_ui.dll", args);
            Assert.Contains("kubuno_views=" + Deps + @"\libkubuno_views-8cdbad53981d1b62.rlib", args);
            Assert.Contains("kubuno_controls=" + Deps + @"\libkubuno_controls-6de35b5c49a7da48.rlib", args);
            Assert.Equal(Views + @"\examples\view_embed.rs", args[6]);
            Assert.Equal(@"C:\out\kubuno-design-surface.exe", args[args.Count - 1]);
            Assert.Contains("opt-level=3", DesignSurfaceBuilder.RustcArgumentsFor(inputs, "x.exe", "release"));
        }

        [Fact]
        public void Key_changes_with_every_input()
        {
            var inputs = new[] { new DesignSurfaceStampInput { Path = "a", Length = 1, LastWriteUtcTicks = 2 } };
            var key = DesignSurfaceBuilder.ComputeKey("AB", inputs, "rustc 1.98.1", "debug");

            Assert.Equal(16, key.Length);
            Assert.Equal(key, DesignSurfaceBuilder.ComputeKey("AB", inputs, "rustc 1.98.1", "debug"));
            Assert.NotEqual(key, DesignSurfaceBuilder.ComputeKey("AC", inputs, "rustc 1.98.1", "debug"));
            Assert.NotEqual(key, DesignSurfaceBuilder.ComputeKey("AB", new[] { new DesignSurfaceStampInput { Path = "a", Length = 1, LastWriteUtcTicks = 3 } }, "rustc 1.98.1", "debug"));
            Assert.NotEqual(key, DesignSurfaceBuilder.ComputeKey("AB", inputs, "rustc 1.99.0", "debug"));
            Assert.NotEqual(key, DesignSurfaceBuilder.ComputeKey("AB", inputs, "rustc 1.98.1", "release"));
        }

        [Fact]
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

            Assert.NotNull(parsed);
            Assert.Equal("0123456789abcdef", parsed!.Key);
            Assert.True(parsed.InputsUnchanged(_ => (10, 20)));
            Assert.False(parsed.InputsUnchanged(_ => (11, 20)));
            Assert.False(parsed.InputsUnchanged(_ => null));
            Assert.Null(DesignSurfaceStamp.TryParse("{}"));
            Assert.Null(DesignSurfaceStamp.TryParse("not json"));
        }

        [Fact]
        public async Task A_project_without_kubuno_views_is_not_applicable()
        {
            var runner = new FakeProcessRunner(new ProcessRunResult(0, new[] { @"{""reason"":""build-finished"",""success"":true}" }, new string[0]));
            var project = new DesignSurfaceProject(Path.Combine(Path.GetTempPath(), "none", "Cargo.toml"), "debug");

            var result = await new DesignSurfaceBuilder(runner).BuildAsync(project, null, CancellationToken.None);

            Assert.Equal(DesignSurfaceBuildStatus.NotApplicable, result.Status);
            Assert.StartsWith("build --manifest-path", runner.LastRequest!.Arguments);
        }

        [Fact]
        public async Task A_failed_project_build_without_inputs_fails()
        {
            var runner = new FakeProcessRunner(new ProcessRunResult(101, new string[0], new[] { "error: could not compile `app`" }));
            var project = new DesignSurfaceProject(Path.Combine(Path.GetTempPath(), "none", "Cargo.toml"), "debug") { TargetDirectory = @"C:\t" };

            var result = await new DesignSurfaceBuilder(runner).BuildAsync(project, null, CancellationToken.None);

            Assert.Equal(DesignSurfaceBuildStatus.Failed, result.Status);
            Assert.Equal(@"C:\t", runner.LastRequest!.EnvironmentVariables!["CARGO_TARGET_DIR"]);
        }

        [Fact]
        public void Profile_folder_and_identity_follow_the_project_properties()
        {
            var project = new DesignSurfaceProject(@"C:\p\app\Cargo.toml", "release") { TargetDirectory = @"C:\t\rsproj\app" };

            Assert.Equal("release", project.EffectiveProfileDirectoryName);
            Assert.Equal(@"C:\t\rsproj\app", project.EffectiveExpectedTargetDirectory);
            Assert.Equal(@"C:\t\rsproj\app\kubuno-design\release", DesignSurfaceBuilder.DesignDirectory(project.EffectiveExpectedTargetDirectory, project.EffectiveProfileDirectoryName));
            Assert.NotEqual(project.Identity, new DesignSurfaceProject(@"C:\p\app\Cargo.toml", "debug").Identity);
            Assert.Equal(@"C:\p\app\target", new DesignSurfaceProject(@"C:\p\app\Cargo.toml", "debug").EffectiveExpectedTargetDirectory);
        }
    }
}
