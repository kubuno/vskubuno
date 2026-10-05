using System.IO;
using Kubuno.Views.Designer.EditorFactory;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Views.Tests.Designer.DesignSurface
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

        [TestMethod]
        public void A_web_views_typescript_code_behind_is_its_code()
        {
            // docs/WEB-VIEWS.md section 2.2: WaffleMenu.kbcontrol + WaffleMenu.ts (or WaffleMenu.kbcontrol.ts).
            var control = Path.Combine(Src, "WaffleMenu.kbcontrol");
            var ts = Path.Combine(Src, "WaffleMenu.ts");
            var tsx = Path.Combine(Src, "WaffleMenu.tsx");
            var inner = control + ".ts";
            Assert.AreEqual(ts, DesignerViewSwitchCommandTarget.CodeFileOf(control, p => p == ts));
            Assert.AreEqual(tsx, DesignerViewSwitchCommandTarget.CodeFileOf(control, p => p == tsx));
            Assert.AreEqual(inner, DesignerViewSwitchCommandTarget.CodeFileOf(control, p => p == inner));
            Assert.AreEqual(Path.Combine(Src, "WaffleMenu.rs"), DesignerViewSwitchCommandTarget.CodeFileOf(control, _ => true), "Rust first (desktop)");

            Assert.AreEqual(control, DesignerViewSwitchCommandTarget.ViewFileOf(ts, p => p == control), "Shift+F7 from the .ts");
            Assert.AreEqual(control, DesignerViewSwitchCommandTarget.ViewFileOf(inner, p => p == control), "Shift+F7 from the .kbcontrol.ts");
            Assert.IsNull(DesignerViewSwitchCommandTarget.ViewFileOf(Path.Combine(Src, "WaffleMenu.kbcontrol.d.ts"), _ => true), "a declaration file is no code-behind");
            Assert.IsNull(DesignerViewSwitchCommandTarget.ViewFileOf(Path.Combine(Src, "model.ts"), p => p == control));
        }
    }
}
