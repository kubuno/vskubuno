using Kubuno.Rust.Cargo.Toolchain;

namespace Kubuno.Rust.Cargo.Tests.Toolchain
{
    public class RustcVersionInfoTests
    {
        [Fact]
        public void Parses_real_rustc_verbose_version_output()
        {
            // rustc -vV, rustc 1.98.1 on Windows.
            var info = RustcVersionInfo.Parse(new[]
            {
                "rustc 1.98.1 (48a229cea 2026-09-01)",
                "binary: rustc",
                "commit-hash: 48a229ceaefd4985c50990b14116b6d856af0985",
                "commit-date: 2026-09-01",
                "host: x86_64-pc-windows-msvc",
                "release: 1.98.1",
                "LLVM version: 22.1.8",
            });

            Assert.NotNull(info);
            Assert.Equal("1.98.1", info!.Release);
            Assert.Equal("1.98.1", info.Version);
            Assert.Equal("stable", info.Channel);
            Assert.Equal("x86_64-pc-windows-msvc", info.Host);
            Assert.Equal("2026-09-01", info.CommitDate);
            Assert.Equal("22.1.8", info.LlvmVersion);
        }

        [Theory]
        [InlineData("1.100.0-nightly", "nightly", "1.100.0")]
        [InlineData("1.99.0-beta.3", "beta", "1.99.0")]
        public void Channel_comes_from_the_release_suffix(string release, string channel, string version)
        {
            var info = RustcVersionInfo.Parse(new[] { "release: " + release, "host: aarch64-apple-darwin", "commit-hash: unknown" })!;

            Assert.Equal(channel, info.Channel);
            Assert.Equal(version, info.Version);
            Assert.Null(info.CommitHash);
        }

        [Fact]
        public void Unrelated_output_gives_null()
        {
            Assert.Null(RustcVersionInfo.Parse(new[] { "error: toolchain 'foo' is not installed" }));
        }
    }
}
