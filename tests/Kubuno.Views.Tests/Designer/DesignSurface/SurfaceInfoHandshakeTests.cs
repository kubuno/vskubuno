using Kubuno.Views.Designer.DesignSurface;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Views.Tests.Designer.DesignSurface
{
    /// <summary>
    /// docs/DESIGNER.md sections 15-16: the <c>surfaceInfo</c> handshake - its wire shape (as
    /// <c>view_embed.rs</c>'s <c>send_surface_info</c> writes it) and the check that refuses a surface
    /// speaking another protocol version. Since kubuno_desktop_ui is linked statically there is no DLL to check:
    /// a version 1 surface (one that loaded a <c>kubuno_ui-&lt;hash&gt;.dll</c>) is refused.
    /// </summary>
    [TestClass]
    public class SurfaceInfoHandshakeTests
    {
        [TestMethod]
        public void TryParseSurfaceInfo_ReadsTheRustWireShape()
        {
            Assert.IsTrue(DesignSurfaceProtocol.TryParseSurfaceInfo(@"{""type"":""surfaceInfo"",""version"":2}", out var version));
            Assert.AreEqual(2, version);
        }

        [TestMethod]
        public void TryParseSurfaceInfo_ReadsAVersion1LineAndRejectsOtherTypes()
        {
            Assert.IsTrue(DesignSurfaceProtocol.TryParseSurfaceInfo(@"{""type"":""surfaceInfo"",""version"":1,""uiDll"":""C:/t/kubuno_desktop_ui-00000000deadbeef.dll"",""uiDllSha256"":null}", out var version));
            Assert.AreEqual(1, version);
            Assert.IsFalse(DesignSurfaceProtocol.TryParseSurfaceInfo(@"{""type"":""selectionChanged"",""id"":null}", out _));
            Assert.IsFalse(DesignSurfaceProtocol.TryParseSurfaceInfo("not json", out _));
        }

        [TestMethod]
        public void Check_PassesTheCurrentVersion()
        {
            Assert.IsNull(DesignSurfaceProtocol.CheckSurfaceInfo(2));
        }

        [TestMethod]
        public void Check_RefusesADllSurfaceAndANewerOne()
        {
            StringAssert.Contains(DesignSurfaceProtocol.CheckSurfaceInfo(1), "kubuno_ui DLL");
            StringAssert.Contains(DesignSurfaceProtocol.CheckSurfaceInfo(0), "expected 2");
            StringAssert.Contains(DesignSurfaceProtocol.CheckSurfaceInfo(3), "update the Kubuno extension");
        }
    }
}
