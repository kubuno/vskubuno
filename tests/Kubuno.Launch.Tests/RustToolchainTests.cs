using System;
using System.IO;
using Kubuno.Launch;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Launch.Tests
{
    [TestClass]
    public sealed class RustToolchainTests
    {
        [TestMethod]
        public void GetSysroot_TrimsProcessOutput_OnSuccess()
        {
            var runner = new FakeProcessRunner().Enqueue(0, "C:\\Users\\me\\.rustup\\toolchains\\stable-x86_64-pc-windows-msvc\r\n");

            var result = RustToolchain.GetSysroot(runner, workingDirectory: @"Z:\projects\kubuno\desktop\windows");

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(@"C:\Users\me\.rustup\toolchains\stable-x86_64-pc-windows-msvc", result.Sysroot);
            Assert.AreEqual(1, runner.Calls.Count);
            Assert.AreEqual("rustc", runner.Calls[0].FileName);
            Assert.AreEqual("--print sysroot", runner.Calls[0].Arguments);
            Assert.AreEqual(@"Z:\projects\kubuno\desktop\windows", runner.Calls[0].WorkingDirectory);
        }

        [TestMethod]
        public void GetSysroot_FailsWithDiagnostic_OnNonZeroExitCode()
        {
            var runner = new FakeProcessRunner().Enqueue(1, string.Empty, "error: no toolchain installed");

            var result = RustToolchain.GetSysroot(runner);

            Assert.IsFalse(result.Succeeded);
            Assert.IsNull(result.Sysroot);
            StringAssert.Contains(result.Error, "no toolchain installed");
        }

        [TestMethod]
        public void GetSysroot_FailsWithDiagnostic_OnEmptyOutput()
        {
            var runner = new FakeProcessRunner().Enqueue(0, "   ");

            var result = RustToolchain.GetSysroot(runner);

            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains(result.Error, "no output");
        }

        [TestMethod]
        public void GetSysroot_ThrowsOnNullProcessRunner()
        {
            Assert.ThrowsExactly<ArgumentNullException>(() => RustToolchain.GetSysroot(null!));
        }

        [TestMethod]
        public void GetTargetLibDir_CombinesSysrootRustlibTripleLib()
        {
            var path = RustToolchain.GetTargetLibDir(
                @"C:\Users\martinien\.rustup\toolchains\stable-x86_64-pc-windows-msvc",
                "x86_64-pc-windows-msvc");

            Assert.AreEqual(
                @"C:\Users\martinien\.rustup\toolchains\stable-x86_64-pc-windows-msvc\lib\rustlib\x86_64-pc-windows-msvc\lib",
                path);
        }

        [TestMethod]
        public void GetNatvisDirectory_CombinesSysrootRustlibEtc()
        {
            var path = RustToolchain.GetNatvisDirectory(@"C:\Users\martinien\.rustup\toolchains\stable-x86_64-pc-windows-msvc");

            Assert.AreEqual(@"C:\Users\martinien\.rustup\toolchains\stable-x86_64-pc-windows-msvc\lib\rustlib\etc", path);
        }

        [TestMethod]
        public void FindNatvisFiles_ReturnsEmpty_WhenDirectoryDoesNotExist()
        {
            var missingSysroot = Path.Combine(Path.GetTempPath(), "kubuno-launch-tests-" + Guid.NewGuid().ToString("N"));

            var files = RustToolchain.FindNatvisFiles(missingSysroot);

            Assert.AreEqual(0, files.Count);
        }

        [TestMethod]
        public void FindNatvisFiles_ListsOnlyNatvisFiles_SortedByName()
        {
            var fakeSysroot = Path.Combine(Path.GetTempPath(), "kubuno-launch-tests-" + Guid.NewGuid().ToString("N"));
            var etcDir = Path.Combine(fakeSysroot, "lib", "rustlib", "etc");
            Directory.CreateDirectory(etcDir);
            try
            {
                File.WriteAllText(Path.Combine(etcDir, "libstd.natvis"), "<AutoVisualizer/>");
                File.WriteAllText(Path.Combine(etcDir, "liballoc.natvis"), "<AutoVisualizer/>");
                File.WriteAllText(Path.Combine(etcDir, "gdb_lookup.py"), "# not natvis");

                var files = RustToolchain.FindNatvisFiles(fakeSysroot);

                Assert.AreEqual(2, files.Count);
                Assert.AreEqual(Path.Combine(etcDir, "liballoc.natvis"), files[0]);
                Assert.AreEqual(Path.Combine(etcDir, "libstd.natvis"), files[1]);
            }
            finally
            {
                Directory.Delete(fakeSysroot, recursive: true);
            }
        }
    }
}
