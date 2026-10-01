using System.IO;
using System.Linq;
using Kubuno.Desktop.Logic;
using Kubuno.Desktop.TemplateWizard;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Desktop.Tests.Views
{
    /// <summary>docs/VIEWS-SPEC.md, "File kinds": forms are <c>.kbview</c> files, user controls <c>.kbcontrol</c> files.</summary>
    [TestClass]
    public class ViewFilesTests
    {
        [TestMethod]
        public void Both_extensions_are_views_of_their_own_kind()
        {
            Assert.IsTrue(ViewFiles.IsViewFile(@"C:\app\src\main_view.kbview"));
            Assert.IsTrue(ViewFiles.IsViewFile(@"C:\app\src\row.KBCONTROL"));
            Assert.IsFalse(ViewFiles.IsViewFile(@"C:\app\src\resources.kbres"));
            Assert.IsFalse(ViewFiles.IsViewFile(null));
            Assert.IsTrue(ViewFiles.IsView(@"a.kbview") && !ViewFiles.IsControl(@"a.kbview"));
            Assert.IsTrue(ViewFiles.IsControl(@"a.kbcontrol") && !ViewFiles.IsView(@"a.kbcontrol"));
        }

        [TestMethod]
        public void The_view_of_a_code_behind_is_found_under_either_extension()
        {
            Assert.AreEqual(@"C:\app\src\row.kbcontrol", ViewFiles.ViewOf(@"C:\app\src\row.rs", p => p.EndsWith(".kbcontrol")));
            Assert.AreEqual(@"C:\app\src\main_view.kbview", ViewFiles.ViewOf(@"C:\app\src\main_view.rs", p => p.EndsWith(".kbview")));
            Assert.IsNull(ViewFiles.ViewOf(@"C:\app\src\main.rs", _ => false));
            Assert.IsNull(ViewFiles.ViewOf(@"C:\app\src\row.kbcontrol", _ => true), "only from a .rs file");
        }

        [TestMethod]
        public void The_inheritance_picker_lists_kbcontrol_files_too()
        {
            var root = Path.Combine(Path.GetTempPath(), "kubuno-viewfiles-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "src"));
            Directory.CreateDirectory(Path.Combine(root, "target"));
            try
            {
                File.WriteAllText(Path.Combine(root, "src", "main_view.kbview"), "<Panel/>");
                File.WriteAllText(Path.Combine(root, "src", "row.kbcontrol"), "<UserControl x:Class=\"Row\"/>");
                File.WriteAllText(Path.Combine(root, "target", "copy.kbcontrol"), "<UserControl/>");
                var names = InheritedViewNames.ViewFiles(root).Select(Path.GetFileName).OrderBy(n => n).ToArray();
                CollectionAssert.AreEqual(new[] { "main_view.kbview", "row.kbcontrol" }, names);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
