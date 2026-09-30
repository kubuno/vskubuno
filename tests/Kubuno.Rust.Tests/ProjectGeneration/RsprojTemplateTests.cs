using Kubuno.Rust.Logic.ProjectGeneration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Rust.Tests.ProjectGeneration
{
    [TestClass]
    public class RsprojTemplateTests
    {
        [TestMethod]
        public void Build_WritesTheSdkImportWithThePinnedVersion()
        {
            var content = RsprojTemplate.Build("app", "1.2.3", cargoBin: null, manifestPathRelativeToProject: null);

            StringAssert.StartsWith(content, "<Project Sdk=\"Kubuno.Rust.Sdk/1.2.3\">");
        }

        [TestMethod]
        public void Build_OmitsCargoBin_WhenNull()
        {
            var content = RsprojTemplate.Build("app", "1.0.0", cargoBin: null, manifestPathRelativeToProject: null);

            Assert.IsFalse(content.Contains("CargoBin"));
        }

        [TestMethod]
        public void Build_WritesCargoBin_WhenGiven()
        {
            var content = RsprojTemplate.Build("app", "1.0.0", cargoBin: "tool", manifestPathRelativeToProject: null);

            StringAssert.Contains(content, "<CargoBin>tool</CargoBin>");
        }

        [TestMethod]
        public void Build_OmitsCargoManifestPath_WhenItIsTheSiblingDefault()
        {
            var content = RsprojTemplate.Build("app", "1.0.0", cargoBin: null, manifestPathRelativeToProject: "Cargo.toml");

            Assert.IsFalse(content.Contains("CargoManifestPath"));
        }

        [TestMethod]
        public void Build_WritesCargoManifestPath_WhenItIsNotTheSiblingDefault()
        {
            var content = RsprojTemplate.Build("app", "1.0.0", cargoBin: null, manifestPathRelativeToProject: @"..\..\app\Cargo.toml");

            StringAssert.Contains(content, @"<CargoManifestPath>..\..\app\Cargo.toml</CargoManifestPath>");
        }

        [TestMethod]
        public void Build_EscapesXmlSpecialCharactersInThePackageName()
        {
            // Not a realistic crate name (Cargo itself would reject it), but the template must
            // never emit invalid XML regardless of what a caller hands it.
            var content = RsprojTemplate.Build("a&b", "1.0.0", cargoBin: null, manifestPathRelativeToProject: null);

            StringAssert.Contains(content, "<CargoPackage>a&amp;b</CargoPackage>");
        }

        [TestMethod]
        public void Build_IsWellFormedXml()
        {
            var content = RsprojTemplate.Build("app", "1.0.0", cargoBin: "tool", manifestPathRelativeToProject: @"..\Cargo.toml");

            // Throws on malformed XML - the real assertion.
            _ = System.Xml.Linq.XDocument.Parse(content);
        }
    }
}
