using System.IO;
using Kubuno.Desktop.Designer.EditorFactory;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Desktop.Tests.Designer.DesignSurface
{
    /// <summary>docs/PROGRAMMING-MODEL.md §7: F7 in the designer opens the view's code, Shift+F7 in the code opens the designer.</summary>
    [TestClass]
    public class DesignerViewSwitchTests
    {
        private static readonly string Src = Path.Combine(Path.GetTempPath(), "app", "src");

        [TestMethod]
        public void F7_opens_the_same_stem_rust_file_when_there_is_one()
        {
            var view = Path.Combine(Src, "main_view.kbview");
            var code = Path.Combine(Src, "main_view.rs");
            Assert.AreEqual(code, DesignerViewSwitchCommandTarget.CodeFileOf(view, p => p == code));
            Assert.IsNull(DesignerViewSwitchCommandTarget.CodeFileOf(view, _ => false), "no code: the XML");
            Assert.IsNull(DesignerViewSwitchCommandTarget.CodeFileOf(string.Empty, _ => true));
        }

        [TestMethod]
        public void Shift_F7_in_a_views_code_opens_the_view()
        {
            var view = Path.Combine(Src, "main_view.kbview");
            var code = Path.Combine(Src, "main_view.rs");
            Assert.AreEqual(view, DesignerViewSwitchCommandTarget.ViewFileOf(code, p => p == view));
            Assert.IsNull(DesignerViewSwitchCommandTarget.ViewFileOf(Path.Combine(Src, "main.rs"), p => p == view), "not a view's code");
            Assert.IsNull(DesignerViewSwitchCommandTarget.ViewFileOf(view, _ => true), "only from a .rs file");
        }

        [TestMethod]
        public void Shift_F7_in_a_user_controls_code_opens_its_kbcontrol()
        {
            var control = Path.Combine(Src, "message_row.kbcontrol");
            var code = Path.Combine(Src, "message_row.rs");
            Assert.AreEqual(control, DesignerViewSwitchCommandTarget.ViewFileOf(code, p => p == control));
            Assert.AreEqual(Path.Combine(Src, "message_row.rs"), DesignerViewSwitchCommandTarget.CodeFileOf(control, p => p == code), "F7 from the user control");
        }
    }
}
