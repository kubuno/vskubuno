using Kubuno.Cargo.Registry;

namespace Kubuno.Cargo.Tests.Registry
{
    public class SemanticVersionTests
    {
        [Theory]
        [InlineData("1.0.0", "1.0.1")]
        [InlineData("1.9.0", "1.10.0")]
        [InlineData("1.0.0-alpha", "1.0.0")]
        [InlineData("1.0.0-alpha", "1.0.0-alpha.1")]
        [InlineData("1.0.0-alpha.1", "1.0.0-alpha.beta")]
        [InlineData("1.0.0-beta.2", "1.0.0-beta.11")]
        [InlineData("1.0.0-rc.1", "1.0.0")]
        [InlineData("0.9.99", "1.0.0")]
        public void Precedence_follows_semver(string lower, string higher)
        {
            Assert.True(SemanticVersion.TryParse(lower, out var a));
            Assert.True(SemanticVersion.TryParse(higher, out var b));
            Assert.True(a.CompareTo(b) < 0);
            Assert.True(b.CompareTo(a) > 0);
        }

        [Fact]
        public void Build_metadata_is_ignored()
        {
            Assert.Equal(SemanticVersion.ParseOrNull("1.2.3+abc"), SemanticVersion.ParseOrNull("1.2.3"));
        }

        [Theory]
        [InlineData("")]
        [InlineData("1.2")]
        [InlineData("^1.0")]
        [InlineData("x.y.z")]
        public void Requirements_and_garbage_do_not_parse(string text)
        {
            Assert.Null(SemanticVersion.ParseOrNull(text));
        }

        [Fact]
        public void Max_skips_pre_releases_unless_asked()
        {
            var versions = new[] { "1.0.0", "1.1.0-rc.1", "0.9.0", "garbage" };

            Assert.Equal("1.0.0", SemanticVersion.Max(versions, includePreRelease: false)!.Original);
            Assert.Equal("1.1.0-rc.1", SemanticVersion.Max(versions, includePreRelease: true)!.Original);
        }
    }
}
