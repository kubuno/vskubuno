using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Markup;
using Kubuno.VisualStudio.Designer.Toolbox;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.VisualStudio.Designer.Tests.Toolbox
{
    [TestClass]
    public class ToolboxIconRasterizerTests
    {
        private const string Head = "<Viewbox xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Width=\"16\" Height=\"16\"><Canvas Width=\"24\" Height=\"24\">";
        private const string Tail = "</Canvas></Viewbox>";

        private static byte[] Render(string shapes)
        {
            byte[]? pixels = null;
            Exception? error = null;
            var thread = new Thread(() =>
            {
                try
                {
                    pixels = ToolboxIconRasterizer.Render((FrameworkElement)XamlReader.Parse(Head + shapes + Tail));
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (error is not null)
            {
                throw new AssertFailedException(error.ToString());
            }

            return pixels!;
        }

        private static byte Alpha(byte[] pixels, int x, int y) => pixels[(((y * 16) + x) * 4) + 3];

        [TestMethod]
        public void MapsTheLucideContentBoxToPixelCentresWithAOnePixelMargin()
        {
            Assert.AreEqual(1.5, ToolboxIconRasterizer.Map(2));
            Assert.AreEqual(14.5, ToolboxIconRasterizer.Map(22), 1e-9);
            Assert.AreEqual(8.5, ToolboxIconRasterizer.Snap(8.0));
            Assert.AreEqual(5.5, ToolboxIconRasterizer.Snap(5.4));
        }

        [TestMethod]
        public void ACircleIsASymmetricOnePixelRingInsideTheMargin()
        {
            var pixels = Render("<Ellipse Canvas.Left=\"2\" Canvas.Top=\"2\" Width=\"20\" Height=\"20\" Stroke=\"#FFFFFF\" StrokeThickness=\"2\"/>");
            foreach (var (x, y, _) in ToolboxIconRasterizer.CoveredPixels(pixels))
            {
                Assert.IsTrue(x >= 1 && x <= 14 && y >= 1 && y <= 14, $"pixel {x},{y} is in the 1-px margin");
            }

            // Solid at the four extremes (on pixel centres), mirror-symmetric everywhere.
            Assert.IsTrue(Alpha(pixels, 1, 8) > 200 && Alpha(pixels, 14, 8) > 200 && Alpha(pixels, 8, 1) > 200 && Alpha(pixels, 8, 14) > 200);
            for (var y = 0; y < 16; y++)
            {
                for (var x = 0; x < 16; x++)
                {
                    Assert.AreEqual(Alpha(pixels, x, y), Alpha(pixels, 15 - x, y), 1, $"{x},{y}: {Alpha(pixels, x, y)} vs {Alpha(pixels, 15 - x, y)}");
                    Assert.AreEqual(Alpha(pixels, x, y), Alpha(pixels, x, 15 - y), 1, $"{x},{y}: {Alpha(pixels, x, y)} vs {Alpha(pixels, x, 15 - y)}");
                }
            }
        }

        [TestMethod]
        public void StraightLinesCoverWholePixels()
        {
            var pixels = Render("<Rectangle Canvas.Left=\"3\" Canvas.Top=\"3\" Width=\"18\" Height=\"18\" Stroke=\"#FFFFFF\" StrokeThickness=\"2\"/><Path Data=\"M12 16v-4\" Stroke=\"#FFFFFF\" StrokeThickness=\"2\"/>");
            // No half-covered pixel: every covered pixel is (almost) opaque.
            Assert.IsTrue(ToolboxIconRasterizer.CoveredPixels(pixels).All(p => p.Alpha > 240), string.Join(" ", ToolboxIconRasterizer.CoveredPixels(pixels).Where(p => p.Alpha <= 240)));
        }

        [TestMethod]
        public void ALucideDotIsASolidPixelBlock()
        {
            var pixels = Render("<Path Data=\"M12 8h.01\" Stroke=\"#FFFFFF\" StrokeThickness=\"2\"/>");
            var covered = ToolboxIconRasterizer.CoveredPixels(pixels).ToList();
            Assert.AreEqual(4, covered.Count, "2x2: its centre is on a pixel boundary horizontally");
            Assert.IsTrue(covered.All(p => p.Alpha == 255));
        }

        [TestMethod]
        public void DotsAreCentredWholePixelRuns()
        {
            Assert.AreEqual(2, ToolboxIconRasterizer.DotSize(8.0, 5.4, 1));
            Assert.AreEqual(1, ToolboxIconRasterizer.DotSize(8.5, 5.5, 1));
            Assert.AreEqual(3, ToolboxIconRasterizer.DotSize(8.5, 5.5, 4));
            Assert.AreEqual(7, ToolboxIconRasterizer.DotStart(8.0, 2));
        }

        [TestMethod]
        public void EveryShippedIconRenders()
        {
            var dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\..\src\Kubuno.VisualStudio.RustProjectSystem\Resources\Icons\Controls"));
            if (!Directory.Exists(dir))
            {
                dir = @"Z:\src\vskubuno\src\Kubuno.VisualStudio.RustProjectSystem\Resources\Icons\Controls";
            }

            if (!Directory.Exists(dir))
            {
                Assert.Inconclusive("icon sources not found");
            }

            foreach (var file in Directory.GetFiles(dir, "*.xaml"))
            {
                var text = File.ReadAllText(file);
                var start = text.IndexOf("<Canvas", StringComparison.Ordinal);
                var end = text.LastIndexOf("</Canvas>", StringComparison.Ordinal);
                var inner = text.Substring(text.IndexOf('>', start) + 1, end - text.IndexOf('>', start) - 1);
                var covered = ToolboxIconRasterizer.CoveredPixels(Render(inner)).ToList();
                Assert.IsTrue(covered.Count > 8, Path.GetFileName(file));
                Assert.IsTrue(covered.All(p => p.X >= 1 && p.X <= 14 && p.Y >= 1 && p.Y <= 14), $"{Path.GetFileName(file)} draws in the 1-px margin");
            }
        }
    }
}
