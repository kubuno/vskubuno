using Kubuno.Cargo.Commands;
using Xunit;

namespace Kubuno.Cargo.Tests.Commands
{
    /// <summary>The cargo options behind the .rsproj Project Properties (docs/RSPROJ.md, "Project properties like .NET").</summary>
    public class CargoCommandPropertiesTests
    {
        [Fact]
        public void Rustc_passes_link_args_to_the_final_crate_only()
        {
            CargoCommandLine line = CargoCommand.Rustc()
                .WithManifestPath(@"C:\app\Cargo.toml")
                .WithTarget(CargoTargetSelector.Bin("app"))
                .WithProfile("release")
                .WithRustcArgs("-C", @"link-arg=C:\app\obj\kubuno_win32_abc.res")
                .ToCommandLine();

            Assert.Equal(@"rustc --manifest-path C:\app\Cargo.toml --bin app --release -- -C link-arg=C:\app\obj\kubuno_win32_abc.res", line.Arguments);
        }

        [Fact]
        public void Rustc_without_args_has_no_separator()
        {
            Assert.Equal("rustc", CargoCommand.Rustc().ToCommandLine().Arguments);
        }

        [Fact]
        public void Build_options_follow_cargos_order()
        {
            CargoCommandLine line = CargoCommand.Build()
                .WithPackage("app")
                .WithTargetTriple("aarch64-pc-windows-msvc")
                .WithFeatures(new[] { "gui", "extra" })
                .WithNoDefaultFeatures()
                .WithConfig("build.rustflags=[\"-D\", \"warnings\"]")
                .WithExtraArgs("--locked")
                .ToCommandLine();

            Assert.Equal(
                "build -p app --target aarch64-pc-windows-msvc --features gui,extra --no-default-features --config \"build.rustflags=[\\\"-D\\\", \\\"warnings\\\"]\" --locked",
                line.Arguments);
        }

        [Fact]
        public void Clippy_is_its_own_subcommand()
        {
            Assert.Equal("clippy -p app", CargoCommand.Clippy().WithPackage("app").ToCommandLine().Arguments);
        }

        [Theory]
        [InlineData(null, null)]
        [InlineData("   ", null)]
        [InlineData("-D warnings", "build.rustflags=[\"-D\", \"warnings\"]")]
        [InlineData(" -C  target-cpu=native\r\n--cfg foo ", "build.rustflags=[\"-C\", \"target-cpu=native\", \"--cfg\", \"foo\"]")]
        [InlineData("--cfg feature=\"x\" C:\\a", "build.rustflags=[\"--cfg\", \"feature=\\\"x\\\"\", \"C:\\\\a\"]")]
        public void RustFlags_become_one_merged_config_array(string? flags, string? expected)
        {
            Assert.Equal(expected, RustFlagsConfig.Format(flags));
        }
    }
}
