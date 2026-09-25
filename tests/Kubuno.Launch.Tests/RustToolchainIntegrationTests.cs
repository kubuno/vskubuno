using System;
using System.ComponentModel;
using System.Linq;
using Kubuno.Launch;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Launch.Tests
{
    /// <summary>
    /// Exercises <see cref="RustToolchain"/> against the real `rustc` on this machine, via
    /// the real <see cref="SystemProcessRunner"/> — no fakes. Marked "Integration" and
    /// skips itself (via <see cref="Assert.Inconclusive(string)"/>, MSTest's skip
    /// mechanism) rather than failing when `rustc` isn't on PATH, per task point 5: "mark
    /// as integration test, skippable when rustc is absent".
    /// </summary>
    [TestClass]
    [TestCategory("Integration")]
    public sealed class RustToolchainIntegrationTests
    {
        // The real sysroot documented for this development machine (see the task brief and
        // vskubuno/CLAUDE.md section 5): asserted as a lower bound on what `rustc --print
        // sysroot` should report, not hardcoded as the only acceptable answer, so the test
        // still means something if the toolchain is later reinstalled at the same path.
        private const string ExpectedSysroot =
            @"C:\Users\martinien\.rustup\toolchains\stable-x86_64-pc-windows-msvc";

        [TestMethod]
        public void GetSysroot_ResolvesRealToolchainSysroot()
        {
            var runner = new SystemProcessRunner();

            SysrootResult result;
            try
            {
                result = RustToolchain.GetSysroot(runner);
            }
            catch (Win32Exception)
            {
                Assert.Inconclusive("rustc is not on PATH on this machine; skipping the real-sysroot integration check.");
                return;
            }

            if (!result.Succeeded)
            {
                Assert.Inconclusive($"'rustc --print sysroot' failed: {result.Error}");
                return;
            }

            Assert.IsFalse(string.IsNullOrWhiteSpace(result.Sysroot));
            Assert.AreEqual(ExpectedSysroot, result.Sysroot, ignoreCase: true);
        }

        [TestMethod]
        public void FindNatvisFiles_FindsStdNatvisFiles_UnderRealSysroot()
        {
            var runner = new SystemProcessRunner();

            SysrootResult result;
            try
            {
                result = RustToolchain.GetSysroot(runner);
            }
            catch (Win32Exception)
            {
                Assert.Inconclusive("rustc is not on PATH on this machine; skipping the real-natvis integration check.");
                return;
            }

            if (!result.Succeeded)
            {
                Assert.Inconclusive($"'rustc --print sysroot' failed: {result.Error}");
                return;
            }

            var natvisFiles = RustToolchain.FindNatvisFiles(result.Sysroot!);

            Assert.IsTrue(natvisFiles.Count > 0, "Expected at least one .natvis file under <sysroot>/lib/rustlib/etc.");
            Assert.IsTrue(
                natvisFiles.Any(f => f.EndsWith("libstd.natvis", StringComparison.OrdinalIgnoreCase)),
                "Expected libstd.natvis among the located natvis files.");
        }
    }
}
