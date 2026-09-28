using Kubuno.VisualStudio.Core.ProjectGeneration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.VisualStudio.Tests.ProjectGeneration
{
    [TestClass]
    public class WorkspaceManifestScannerTests
    {
        [TestMethod]
        public void IsWorkspaceManifest_True_ForATopLevelWorkspaceTable()
        {
            var text = "[workspace]\nmembers = [\"a\", \"b\"]\n";

            Assert.IsTrue(WorkspaceManifestScanner.IsWorkspaceManifest(text));
        }

        [TestMethod]
        public void IsWorkspaceManifest_True_ForAWorkspaceSubTable()
        {
            var text = "[workspace.package]\nversion = \"0.1.0\"\n";

            Assert.IsTrue(WorkspaceManifestScanner.IsWorkspaceManifest(text));
        }

        [TestMethod]
        public void IsWorkspaceManifest_False_ForAPlainPackageManifest()
        {
            var text = "[package]\nname = \"app\"\nversion = \"0.1.0\"\n";

            Assert.IsFalse(WorkspaceManifestScanner.IsWorkspaceManifest(text));
        }

        [TestMethod]
        public void IsWorkspaceManifest_IgnoresCommentsAndIndentation()
        {
            var text = "# comment mentioning [workspace] inside a comment\n  [workspace]  \nmembers = []\n";

            Assert.IsTrue(WorkspaceManifestScanner.IsWorkspaceManifest(text));
        }

        [TestMethod]
        public void IsWorkspaceManifest_False_ForATableWhoseNameMerelyStartsWithWorkspace()
        {
            // "[workspace-extras]" is not "[workspace]" or "[workspace.*]".
            var text = "[workspace-extras]\nfoo = 1\n";

            Assert.IsFalse(WorkspaceManifestScanner.IsWorkspaceManifest(text));
        }

        [TestMethod]
        public void IsWorkspaceManifest_False_ForNullOrEmpty()
        {
            Assert.IsFalse(WorkspaceManifestScanner.IsWorkspaceManifest(null));
            Assert.IsFalse(WorkspaceManifestScanner.IsWorkspaceManifest(string.Empty));
        }
    }
}
