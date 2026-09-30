using System;
using Kubuno.Launch;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Launch.Tests
{
    [TestClass]
    public sealed class CargoLayoutTests
    {
        [TestMethod]
        public void ResolveTargetDir_DefaultsToWorkspaceRootSlashTarget_WhenNoOverride()
        {
            var result = CargoLayout.ResolveTargetDir(@"Z:\projects\kubuno\desktop\windows", null);

            Assert.AreEqual(@"Z:\projects\kubuno\desktop\windows\target", result);
        }

        [TestMethod]
        public void ResolveTargetDir_DefaultsToWorkspaceRootSlashTarget_WhenOverrideIsEmpty()
        {
            var result = CargoLayout.ResolveTargetDir(@"Z:\projects\kubuno\desktop\windows", string.Empty);

            Assert.AreEqual(@"Z:\projects\kubuno\desktop\windows\target", result);
        }

        [TestMethod]
        public void ResolveTargetDir_HonoursCargoTargetDirOverride()
        {
            // The exact style CLAUDE.md documents for this machine's desktop workspace.
            var result = CargoLayout.ResolveTargetDir(
                @"Z:\projects\kubuno\desktop\windows",
                @"C:\kubuno-build\desktop-target");

            Assert.AreEqual(@"C:\kubuno-build\desktop-target", result);
        }

        [TestMethod]
        public void ResolveTargetDir_ThrowsOnNullWorkspaceRoot()
        {
            Assert.ThrowsExactly<ArgumentNullException>(() => CargoLayout.ResolveTargetDir(null!, null));
        }

        [TestMethod]
        [DataRow("dev", "debug")]
        [DataRow("test", "debug")]
        [DataRow("release", "release")]
        [DataRow("bench", "release")]
        [DataRow("perf", "perf")]
        public void ResolveProfileDirectoryName_MapsBuiltInAndCustomProfiles(string profile, string expectedDirectoryName)
        {
            Assert.AreEqual(expectedDirectoryName, CargoLayout.ResolveProfileDirectoryName(profile));
        }

        [TestMethod]
        public void ResolveProfileDirectoryName_ThrowsOnEmptyProfile()
        {
            Assert.ThrowsExactly<ArgumentException>(() => CargoLayout.ResolveProfileDirectoryName(string.Empty));
        }

        [TestMethod]
        public void ResolveProfileDir_WithoutTriple_IsTargetDirSlashProfileDir()
        {
            var result = CargoLayout.ResolveProfileDir(@"C:\kubuno-build\desktop-target", null, "dev");

            Assert.AreEqual(@"C:\kubuno-build\desktop-target\debug", result);
        }

        [TestMethod]
        public void ResolveProfileDir_WithTriple_InsertsTripleBeforeProfileDir()
        {
            var result = CargoLayout.ResolveProfileDir(
                @"C:\kubuno-build\desktop-target",
                "x86_64-pc-windows-msvc",
                "release");

            Assert.AreEqual(@"C:\kubuno-build\desktop-target\x86_64-pc-windows-msvc\release", result);
        }
    }
}
