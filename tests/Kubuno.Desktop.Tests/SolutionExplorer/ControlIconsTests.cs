using System.IO;
using Kubuno.Desktop.Logic.SolutionExplorer;
using Kubuno.Rust.Logic.SolutionExplorer;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Desktop.Tests.SolutionExplorer
{
    /// <summary>The Kubuno control icons of .kbview elements (Toolbox and Solution Explorer) - split from the Rust symbol moniker tests.</summary>
    [TestClass]
    public class ControlIconsTests
    {
        [TestMethod]
        [DataRow("Button", 2)]
        [DataRow("TextField", 6)]
        [DataRow("Stack", 4)]
        [DataRow("Slider", 50)]
        [DataRow("MyFancyWidget", ControlIcons.FallbackId)]
        [DataRow(null, ControlIcons.FallbackId)]
        public void ViewElementsUseTheKubunoControlIcons(string? tag, int expectedId)
        {
            Assert.AreEqual(expectedId, ControlIcons.IdFor(tag));
        }

        [TestMethod]
        public void ANamedViewElementIsIconedByItsTagNotItsName()
        {
            var element = new SolutionSymbol("hello", SolutionSymbolKind.Element, SymbolVisibility.Public, "Button", 0, 0);
            Assert.AreEqual(ControlIcons.IdFor("Button"), ControlIcons.IdFor(element.ElementTag));
            Assert.AreEqual("hello (Button)", element.DisplayText);
        }

        /// <summary>Every kubuno-views component of the registry fixture has its own Kubuno control icon (tools/generate-control-icons.ps1).</summary>
        [TestMethod]
        public void EveryRegistryComponentHasAControlIcon()
        {
            var fixture = Path.Combine(Path.GetDirectoryName(SourceFile())!, "..", "Fixtures", "registry.sample.json");
            if (!File.Exists(fixture))
            {
                Assert.Inconclusive("registry fixture not found next to this test project: " + fixture);
            }

            var names = System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(fixture), "^    \"name\"\\s*:\\s*\"(\\w+)\"", System.Text.RegularExpressions.RegexOptions.Multiline);
            Assert.IsTrue(names.Count > 40);
            foreach (System.Text.RegularExpressions.Match name in names)
            {
                Assert.AreNotEqual(ControlIcons.FallbackId, ControlIcons.IdFor(name.Groups[1].Value), name.Groups[1].Value);
            }
        }

        private static string SourceFile([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
    }
}
