using System.Collections.Generic;
using System.Linq;
using Kubuno.Rust.Launch;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Rust.Launch.Tests
{
    [TestClass]
    public sealed class RustDebugEnvironmentTests
    {
        private const string TargetDir = @"C:\kubuno-build\desktop-target";
        private const string Sysroot = @"C:\Users\martinien\.rustup\toolchains\stable-x86_64-pc-windows-msvc";
        private const string HostTriple = "x86_64-pc-windows-msvc";

        [TestMethod]
        public void BuildPathAdditions_ReturnsProfileDir_DepsDir_ThenSysrootLibDir_InOrder()
        {
            var additions = RustDebugEnvironment.BuildPathAdditions(TargetDir, null, "dev", Sysroot, HostTriple);

            CollectionAssert.AreEqual(
                new[]
                {
                    @"C:\kubuno-build\desktop-target\debug",
                    @"C:\kubuno-build\desktop-target\debug\deps",
                    @"C:\Users\martinien\.rustup\toolchains\stable-x86_64-pc-windows-msvc\lib\rustlib\x86_64-pc-windows-msvc\lib",
                },
                additions.ToArray());
        }

        [TestMethod]
        public void BuildPathAdditions_HonoursTargetTriple()
        {
            var additions = RustDebugEnvironment.BuildPathAdditions(TargetDir, "x86_64-pc-windows-msvc", "release", Sysroot, HostTriple);

            Assert.AreEqual(@"C:\kubuno-build\desktop-target\x86_64-pc-windows-msvc\release", additions[0]);
        }

        [TestMethod]
        public void Build_SetsRustBacktraceByDefault()
        {
            var env = RustDebugEnvironment.Build(TargetDir, null, "dev", Sysroot, HostTriple);

            Assert.AreEqual("1", env["RUST_BACKTRACE"]);
        }

        [TestMethod]
        public void Build_OmitsRustBacktrace_WhenExplicitlyNull()
        {
            var env = RustDebugEnvironment.Build(TargetDir, null, "dev", Sysroot, HostTriple, rustBacktrace: null);

            Assert.IsFalse(env.ContainsKey("RUST_BACKTRACE"));
        }

        [TestMethod]
        public void Build_AppendsExistingPath_AfterComputedEntries()
        {
            var env = RustDebugEnvironment.Build(TargetDir, null, "dev", Sysroot, HostTriple, existingPath: @"C:\Windows\System32");

            var expectedPrefix = @"C:\kubuno-build\desktop-target\debug;C:\kubuno-build\desktop-target\debug\deps;" +
                                  @"C:\Users\martinien\.rustup\toolchains\stable-x86_64-pc-windows-msvc\lib\rustlib\x86_64-pc-windows-msvc\lib;" +
                                  @"C:\Windows\System32";

            Assert.AreEqual(expectedPrefix, env["PATH"]);
        }

        [TestMethod]
        public void Build_OverridesWinOverComputedValues()
        {
            var overrides = new Dictionary<string, string>
            {
                ["RUST_BACKTRACE"] = "full",
                ["MY_VAR"] = "1",
            };

            var env = RustDebugEnvironment.Build(TargetDir, null, "dev", Sysroot, HostTriple, overrides: overrides);

            Assert.AreEqual("full", env["RUST_BACKTRACE"]);
            Assert.AreEqual("1", env["MY_VAR"]);
        }
    }
}
