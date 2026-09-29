using System;
using Kubuno.VisualStudio.Designer.DesignSurface;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.VisualStudio.Designer.Tests.DesignSurface
{
    /// <summary>
    /// docs/DESIGNER.md section 15: the <c>surfaceInfo</c> ABI handshake - its wire shape (as
    /// <c>view_embed.rs</c>'s <c>send_surface_info</c> writes it) and the check that refuses a surface
    /// whose loaded <c>kubuno_ui.dll</c> is not the one it was linked against.
    /// </summary>
    [TestClass]
    public class SurfaceInfoHandshakeTests
    {
        private const string Exe = @"C:\t\rsproj\app\kubuno-design\debug\0123456789abcdef\kubuno-design-surface.exe";
        private const string Dll = @"C:\t\rsproj\app\kubuno-design\debug\0123456789abcdef\kubuno_ui.dll";
        private const string Sha = "0EB972E1D386A45A118548579C2CEE6C88E237CFDC39C291C9C7F33D7F1AC6B3";

        [TestMethod]
        public void TryParseSurfaceInfo_ReadsTheRustWireShape()
        {
            var line = @"{""type"":""surfaceInfo"",""version"":1,""uiDll"":""C:\\t\\kubuno_ui.dll"",""uiDllSha256"":""" + Sha + @"""}";
            Assert.IsTrue(DesignSurfaceProtocol.TryParseSurfaceInfo(line, out var version, out var dll, out var sha));
            Assert.AreEqual(1, version);
            Assert.AreEqual(@"C:\t\kubuno_ui.dll", dll);
            Assert.AreEqual(Sha, sha);
        }

        [TestMethod]
        public void TryParseSurfaceInfo_AcceptsNullHashAndRejectsOtherTypes()
        {
            Assert.IsTrue(DesignSurfaceProtocol.TryParseSurfaceInfo(@"{""type"":""surfaceInfo"",""version"":1,""uiDll"":null,""uiDllSha256"":null}", out _, out var dll, out var sha));
            Assert.IsNull(dll);
            Assert.IsNull(sha);
            Assert.IsFalse(DesignSurfaceProtocol.TryParseSurfaceInfo(@"{""type"":""selectionChanged"",""id"":null}", out _, out _, out _));
            Assert.IsFalse(DesignSurfaceProtocol.TryParseSurfaceInfo("not json", out _, out _, out _));
        }

        [TestMethod]
        public void Check_PassesWhenTheLoadedCopyHasTheRecordedHash()
        {
            Assert.IsNull(DesignSurfaceProtocol.CheckSurfaceInfo(1, Dll, Sha, Exe, Sha, _ => Sha.ToLowerInvariant()));
        }

        [TestMethod]
        public void Check_RefusesAModifiedDll()
        {
            var problem = DesignSurfaceProtocol.CheckSurfaceInfo(1, Dll, Sha, Exe, Sha, _ => "FFFF" + Sha.Substring(4));
            StringAssert.Contains(problem, "linked against");
        }

        [TestMethod]
        public void Check_RefusesAnExeLinkedAgainstAnotherDll()
        {
            var problem = DesignSurfaceProtocol.CheckSurfaceInfo(1, Dll, "AAAA" + Sha.Substring(4), Exe, Sha, _ => Sha);
            StringAssert.Contains(problem, "the design build recorded");
        }

        [TestMethod]
        public void Check_RefusesADllLoadedFromAnotherFolder()
        {
            var problem = DesignSurfaceProtocol.CheckSurfaceInfo(1, @"C:\Windows\System32\kubuno_ui.dll", Sha, Exe, Sha, _ => Sha);
            StringAssert.Contains(problem, "instead of the copy");
        }

        [TestMethod]
        public void Check_RefusesAnUnknownVersionOrAMissingDll()
        {
            Assert.IsNotNull(DesignSurfaceProtocol.CheckSurfaceInfo(2, Dll, Sha, Exe, Sha, _ => Sha));
            Assert.IsNotNull(DesignSurfaceProtocol.CheckSurfaceInfo(1, null, Sha, Exe, Sha, _ => Sha));
        }

        [TestMethod]
        public void Check_BundledSurfaceWithoutHashOnlyNeedsItsOwnCopy()
        {
            const string bundledExe = @"C:\ext\tools\surface\view_embed.exe";
            Assert.IsNull(DesignSurfaceProtocol.CheckSurfaceInfo(1, @"C:\ext\tools\surface\kubuno_ui.dll", null, bundledExe, null, _ => throw new InvalidOperationException("no hash expected")));
        }

        [TestMethod]
        public void Check_RefusesAnUnreadableDll()
        {
            StringAssert.Contains(DesignSurfaceProtocol.CheckSurfaceInfo(1, Dll, null, Exe, Sha, _ => null), "could not be read");
        }
    }
}
