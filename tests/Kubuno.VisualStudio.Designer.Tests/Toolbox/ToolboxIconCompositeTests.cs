using System.Drawing;
using Kubuno.VisualStudio.Designer.Toolbox;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.VisualStudio.Designer.Tests.Toolbox
{
    [TestClass]
    public class ToolboxIconCompositeTests
    {
        private static readonly Color Dark = Color.FromArgb(0x28, 0x28, 0x28);

        [TestMethod]
        public void UncoveredPixel_BecomesTheTransparencyKey()
        {
            var (r, _, _) = NativeToolboxInstaller.Composite(new byte[] { 0, 0, 0, 0 }, 0, Dark);

            Assert.IsNull(r);
        }

        [TestMethod]
        public void PartlyCoveredPixel_IsKeptAsASolidBlendOntoTheBackground()
        {
            // 25% of #C5C5C5, premultiplied (0xC5 * 64 / 255 = 49).
            var (r, g, b) = NativeToolboxInstaller.Composite(new byte[] { 49, 49, 49, 64 }, 0, Dark);

            Assert.AreEqual(49 + (int)System.Math.Round(0x28 * (191 / 255.0)), r);
            Assert.AreEqual(r, g);
            Assert.AreEqual(r, b);
        }

        [TestMethod]
        public void OpaquePixel_KeepsItsColour_ButNeverTheKeyColour()
        {
            Assert.AreEqual(((int?)77, 155, 240), NativeToolboxInstaller.Composite(new byte[] { 240, 155, 77, 255 }, 0, Dark));
            Assert.AreEqual(((int?)254, 0, 255), NativeToolboxInstaller.Composite(new byte[] { 255, 0, 255, 255 }, 0, Dark));
        }
    }
}
