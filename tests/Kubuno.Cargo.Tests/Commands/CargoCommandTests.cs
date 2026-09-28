using Kubuno.Cargo.Commands;
using Xunit;

namespace Kubuno.Cargo.Tests.Commands
{
    public class CargoCommandTests
    {
        [Theory]
        [InlineData(CargoCommandKind.Build, "build")]
        [InlineData(CargoCommandKind.Check, "check")]
        [InlineData(CargoCommandKind.Test, "test")]
        [InlineData(CargoCommandKind.Clean, "clean")]
        [InlineData(CargoCommandKind.Run, "run")]
        [InlineData(CargoCommandKind.Fetch, "fetch")]
        public void Subcommand_name_matches_kind(CargoCommandKind kind, string expectedSubcommand)
        {
            var command = kind switch
            {
                CargoCommandKind.Build => CargoCommand.Build(),
                CargoCommandKind.Check => CargoCommand.Check(),
                CargoCommandKind.Test => CargoCommand.Test(),
                CargoCommandKind.Clean => CargoCommand.Clean(),
                CargoCommandKind.Run => CargoCommand.Run(),
                CargoCommandKind.Fetch => CargoCommand.Fetch(),
                _ => throw new System.NotSupportedException(),
            };

            CargoCommandLine line = command.ToCommandLine();
            Assert.Equal("cargo", line.FileName);
            Assert.Equal(expectedSubcommand, line.ArgumentList[0]);
        }

        [Fact]
        public void Bare_build_emits_no_extra_flags()
        {
            CargoCommandLine line = CargoCommand.Build().ToCommandLine();
            Assert.Equal("build", line.Arguments);
        }

        [Fact]
        public void Debug_profile_emits_no_flag()
        {
            CargoCommandLine line = CargoCommand.Build().WithProfile("debug").ToCommandLine();
            Assert.Equal("build", line.Arguments);
        }

        [Fact]
        public void Release_profile_emits_dash_dash_release()
        {
            CargoCommandLine line = CargoCommand.Build().WithProfile("release").ToCommandLine();
            Assert.Equal("build --release", line.Arguments);
        }

        [Fact]
        public void Custom_profile_emits_dash_dash_profile_with_name()
        {
            CargoCommandLine line = CargoCommand.Build().WithProfile("bench-like").ToCommandLine();
            Assert.Equal("build --profile bench-like", line.Arguments);
        }

        [Fact]
        public void Package_selector_emits_dash_p()
        {
            CargoCommandLine line = CargoCommand.Build().WithPackage("core-lib").ToCommandLine();
            Assert.Equal("build -p core-lib", line.Arguments);
        }

        [Fact]
        public void Workspace_flag_is_emitted()
        {
            CargoCommandLine line = CargoCommand.Test().WithWorkspace().ToCommandLine();
            Assert.Equal("test --workspace", line.Arguments);
        }

        [Fact]
        public void Bin_target_selector_emits_dash_dash_bin_with_name()
        {
            CargoCommandLine line = CargoCommand.Build().WithTarget(CargoTargetSelector.Bin("app")).ToCommandLine();
            Assert.Equal("build --bin app", line.Arguments);
        }

        [Fact]
        public void Example_target_selector_emits_dash_dash_example_with_name()
        {
            CargoCommandLine line = CargoCommand.Run().WithTarget(CargoTargetSelector.Example("demo")).ToCommandLine();
            Assert.Equal("run --example demo", line.Arguments);
        }

        [Fact]
        public void Test_target_selector_emits_dash_dash_test_with_name()
        {
            CargoCommandLine line = CargoCommand.Test().WithTarget(CargoTargetSelector.Test("integration")).ToCommandLine();
            Assert.Equal("test --test integration", line.Arguments);
        }

        [Fact]
        public void Lib_selector_takes_no_name()
        {
            CargoCommandLine line = CargoCommand.Build().WithTarget(CargoTargetSelector.Lib).ToCommandLine();
            Assert.Equal("build --lib", line.Arguments);
        }

        [Fact]
        public void Features_are_comma_joined()
        {
            CargoCommandLine line = CargoCommand.Build().WithFeature("a").WithFeature("b").ToCommandLine();
            Assert.Equal("build --features a,b", line.Arguments);
        }

        [Fact]
        public void All_features_and_no_default_features_flags()
        {
            CargoCommandLine line = CargoCommand.Build().WithAllFeatures().WithNoDefaultFeatures().ToCommandLine();
            Assert.Equal("build --all-features --no-default-features", line.Arguments);
        }

        [Fact]
        public void Message_format_is_emitted()
        {
            CargoCommandLine line = CargoCommand.Build().WithMessageFormat("json-diagnostic-rendered-ansi").ToCommandLine();
            Assert.Equal("build --message-format json-diagnostic-rendered-ansi", line.Arguments);
        }

        [Fact]
        public void Extra_args_are_appended_verbatim()
        {
            CargoCommandLine line = CargoCommand.Build().WithExtraArgs("--locked", "--frozen").ToCommandLine();
            Assert.Equal("build --locked --frozen", line.Arguments);
        }

        [Fact]
        public void Run_args_are_appended_after_a_double_dash_separator()
        {
            CargoCommandLine line = CargoCommand.Run().WithTarget(CargoTargetSelector.Bin("app")).WithRunArgs("--flag", "value").ToCommandLine();
            Assert.Equal("run --bin app -- --flag value", line.Arguments);
        }

        [Fact]
        public void Run_args_are_ignored_for_non_run_commands()
        {
            // WithRunArgs only makes sense for `cargo run`; a stray call on another kind must not leak "--".
            CargoCommandLine line = CargoCommand.Build().WithRunArgs("ignored").ToCommandLine();
            Assert.Equal("build", line.Arguments);
        }

        [Fact]
        public void Manifest_path_is_emitted()
        {
            CargoCommandLine line = CargoCommand.Check().WithManifestPath(@"C:\repo\Cargo.toml").ToCommandLine();
            Assert.Equal(@"check --manifest-path C:\repo\Cargo.toml", line.Arguments);
        }

        [Fact]
        public void Full_pipeline_matches_realistic_error_list_build_invocation()
        {
            CargoCommandLine line = CargoCommand.Build()
                .WithWorkspace()
                .WithProfile("release")
                .WithMessageFormat("json-diagnostic-rendered-ansi")
                .ToCommandLine();

            Assert.Equal("build --workspace --release --message-format json-diagnostic-rendered-ansi", line.Arguments);
        }

        // --- Windows argument quoting, exercised through the public CargoCommand surface ---

        [Fact]
        public void Argument_with_a_space_is_wrapped_in_quotes()
        {
            CargoCommandLine line = CargoCommand.Build().WithManifestPath(@"C:\path with space\Cargo.toml").ToCommandLine();
            Assert.Equal(@"build --manifest-path ""C:\path with space\Cargo.toml""", line.Arguments);
        }

        [Fact]
        public void Argument_with_an_embedded_quote_is_escaped()
        {
            CargoCommandLine line = CargoCommand.Run().WithRunArgs("say \"hi\"").ToCommandLine();
            Assert.Equal("run -- \"say \\\"hi\\\"\"", line.Arguments);
        }

        [Fact]
        public void Trailing_backslashes_before_a_closing_quote_are_doubled()
        {
            // "C:\a\" needs its trailing backslash doubled so it doesn't escape the closing quote:
            // -> "C:\a\\"
            CargoCommandLine line = CargoCommand.Run().WithRunArgs(@"C:\a with space\").ToCommandLine();
            Assert.Equal("run -- \"C:\\a with space\\\\\"", line.Arguments);
        }

        [Fact]
        public void Literal_backslashes_not_followed_by_a_quote_are_left_untouched()
        {
            CargoCommandLine line = CargoCommand.Build().WithManifestPath(@"C:\no\spaces\here").ToCommandLine();
            Assert.Equal(@"build --manifest-path C:\no\spaces\here", line.Arguments);
        }

        [Fact]
        public void Empty_argument_is_still_quoted_so_it_is_not_lost()
        {
            CargoCommandLine line = CargoCommand.Run().WithRunArgs(string.Empty).ToCommandLine();
            Assert.Equal("run -- \"\"", line.Arguments);
        }

        [Fact]
        public void ArgumentList_exposes_the_unquoted_argv_for_direct_inspection()
        {
            CargoCommandLine line = CargoCommand.Build().WithPackage("core-lib").ToCommandLine();
            Assert.Equal(new[] { "build", "-p", "core-lib" }, line.ArgumentList);
        }
    }
}
