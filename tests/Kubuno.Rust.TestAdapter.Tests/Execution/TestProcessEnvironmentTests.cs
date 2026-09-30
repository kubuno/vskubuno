using Kubuno.Rust.TestAdapter.Execution;

namespace Kubuno.Rust.TestAdapter.Tests.Execution
{
    /// <summary>
    /// A test program of a crate that links a Rust dylib (-C prefer-dynamic, the Kubuno desktop workspace) starts only with the
    /// dylib's folder and Rust's std-*.dll folder on PATH.
    /// </summary>
    public class TestProcessEnvironmentTests
    {
        [Fact]
        public void The_path_starts_with_the_deps_folder_the_profile_folder_and_the_toolchain_library()
        {
            string path = TestProcessEnvironment.BuildPath(
                @"C:\target\debug\deps\app-0123.exe",
                @"C:\rust\lib\rustlib\x86_64-pc-windows-msvc\lib",
                @"C:\Windows\system32");

            Assert.Equal(@"C:\target\debug\deps;C:\target\debug;C:\rust\lib\rustlib\x86_64-pc-windows-msvc\lib;C:\Windows\system32", path);
        }

        [Fact]
        public void An_unknown_toolchain_or_empty_path_is_left_out()
        {
            Assert.Equal(@"C:\target\debug\deps;C:\target\debug", TestProcessEnvironment.BuildPath(@"C:\target\debug\deps\app.exe", null, null));
        }

        [Fact]
        public void Caller_variables_are_kept_and_an_explicit_path_is_extended()
        {
            var environment = TestProcessEnvironment.For(
                @"C:\target\debug\deps\app.exe",
                @"C:\work",
                new System.Collections.Generic.Dictionary<string, string> { ["CARGO_TARGET_DIR"] = @"C:\target", ["PATH"] = @"C:\mine" });

            Assert.Equal(@"C:\target", environment["CARGO_TARGET_DIR"]);
            Assert.StartsWith(@"C:\target\debug\deps;C:\target\debug;", environment["PATH"]);
            Assert.EndsWith(@";C:\mine", environment["PATH"]);
        }
    }
}
