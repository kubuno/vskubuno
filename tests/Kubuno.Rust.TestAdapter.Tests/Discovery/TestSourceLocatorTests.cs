using System;
using System.IO;
using Kubuno.Rust.TestAdapter.Discovery;

namespace Kubuno.Rust.TestAdapter.Tests.Discovery
{
    public class TestSourceLocatorTests : IDisposable
    {
        private readonly string _tempFile = Path.Combine(Path.GetTempPath(), $"kubuno-test-source-locator-{Guid.NewGuid():N}.rs");

        public void Dispose()
        {
            if (File.Exists(_tempFile))
            {
                File.Delete(_tempFile);
            }
        }

        [Fact]
        public void Finds_a_plain_fn_inside_a_cfg_test_module()
        {
            File.WriteAllLines(_tempFile, new[]
            {
                "pub fn greet(name: &str) -> String {",
                "    format!(\"Hello, {name}!\")",
                "}",
                "",
                "#[cfg(test)]",
                "mod tests {",
                "    use super::*;",
                "",
                "    #[test]",
                "    fn greet_includes_the_name() {",
                "        assert_eq!(greet(\"Kubuno\"), \"Hello, Kubuno!\");",
                "    }",
                "}",
            });

            TestSourceLocation? location = TestSourceLocator.TryLocate(_tempFile, "tests::greet_includes_the_name");

            Assert.NotNull(location);
            Assert.Equal(_tempFile, location!.Value.FilePath);
            Assert.Equal(10, location.Value.LineNumber); // 1-based
        }

        [Fact]
        public void Finds_an_async_pub_fn()
        {
            File.WriteAllLines(_tempFile, new[]
            {
                "#[test]",
                "pub(crate) async fn it_works() {",
                "    assert!(true);",
                "}",
            });

            TestSourceLocation? location = TestSourceLocator.TryLocate(_tempFile, "it_works");

            Assert.NotNull(location);
            Assert.Equal(2, location!.Value.LineNumber);
        }

        [Fact]
        public void Returns_null_when_the_function_is_not_in_this_file()
        {
            File.WriteAllLines(_tempFile, new[] { "pub fn greet() {}" });

            Assert.Null(TestSourceLocator.TryLocate(_tempFile, "tests::declared_in_another_module"));
        }

        [Fact]
        public void Returns_null_when_the_file_does_not_exist()
        {
            Assert.Null(TestSourceLocator.TryLocate(@"Z:\does\not\exist.rs", "tests::anything"));
        }

        [Fact]
        public void Matches_only_the_bare_name_after_the_last_double_colon()
        {
            File.WriteAllLines(_tempFile, new[]
            {
                "#[test]",
                "fn it_works() {}",
            });

            TestSourceLocation? location = TestSourceLocator.TryLocate(_tempFile, "tests::nested::it_works");

            Assert.NotNull(location);
            Assert.Equal(2, location!.Value.LineNumber);
        }
    }
}
