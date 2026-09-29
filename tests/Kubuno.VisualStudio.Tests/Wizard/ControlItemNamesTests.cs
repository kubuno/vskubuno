using System.IO;
using Kubuno.VisualStudio.TemplateWizard;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.VisualStudio.Tests.Wizard
{
    /// <summary>docs/EVENTS.md EVT-7b: the control item templates' wizard (names, and the module declared in the crate root).</summary>
    [TestClass]
    public class ControlItemNamesTests
    {
        [TestMethod]
        public void Names_follow_the_rust_conventions()
        {
            Assert.AreEqual("RoundButton", ControlItemNames.ClassName("RoundButton.rs"));
            Assert.AreEqual("round_button", ControlItemNames.ModuleName("RoundButton.rs"));
            Assert.AreEqual("RoundButton", ControlItemNames.ClassName("round_button"));
            Assert.AreEqual("RatingBar", ControlItemNames.ClassName("rating bar.kbview"));
            Assert.AreEqual("http_client", ControlItemNames.ModuleName("HTTPClient"));
            Assert.AreEqual("HttpClient", ControlItemNames.ClassName("http_client"));
            Assert.AreEqual("Control3d", ControlItemNames.ClassName("3d"));
            Assert.AreEqual("control_3d", ControlItemNames.ModuleName("3d"));
            Assert.AreEqual("CustomControl", ControlItemNames.ClassName(".rs"));
        }

        private const string MainRs = "//! My app.\r\n#![windows_subsystem = \"windows\"]\r\n\r\nmod main_view;\r\n\r\nuse kubuno_ui::Rect;\r\n\r\nfn main() {}\r\n";

        [TestMethod]
        public void The_module_is_declared_after_the_last_one()
        {
            var updated = ControlItemNames.DeclareModule(MainRs, "round_button");
            Assert.AreEqual("//! My app.\r\n#![windows_subsystem = \"windows\"]\r\n\r\nmod main_view;\r\nmod round_button;\r\n\r\nuse kubuno_ui::Rect;\r\n\r\nfn main() {}\r\n", updated);
            Assert.IsNull(ControlItemNames.DeclareModule(updated!, "round_button"), "declared once");
            Assert.IsNull(ControlItemNames.DeclareModule("pub mod round_button;\n", "round_button"));
        }

        [TestMethod]
        public void A_file_not_named_after_its_module_gets_a_path_attribute()
        {
            Assert.AreEqual("mod a;\nmod round_button;\n", ControlItemNames.DeclareModule("mod a;\n", "round_button", "round_button.rs"));
            Assert.AreEqual("mod a;\n#[path = \"RoundButton.rs\"]\nmod round_button;\n", ControlItemNames.DeclareModule("mod a;\n", "round_button", "RoundButton.rs"));
        }

        [TestMethod]
        public void Without_modules_it_goes_after_the_uses_or_the_crate_doc()
        {
            Assert.AreEqual("use a::b;\nmod x;\n\nfn main() {}\n", ControlItemNames.DeclareModule("use a::b;\n\nfn main() {}\n", "x"));
            Assert.AreEqual("//! Doc.\n\nmod x;\n\nfn main() {}\n", ControlItemNames.DeclareModule("//! Doc.\n\nfn main() {}\n", "x"));
            Assert.AreEqual("mod x;\n\nfn main() {}\n", ControlItemNames.DeclareModule("fn main() {}\n", "x"));
        }

        [TestMethod]
        public void Only_a_file_directly_in_src_of_a_package_has_a_crate_root()
        {
            var root = Path.Combine(Path.GetTempPath(), "app");
            bool Exists(string path) => path == Path.Combine(root, "Cargo.toml") || path == Path.Combine(root, "src", "main.rs");
            Assert.AreEqual(Path.Combine(root, "src", "main.rs"), ControlItemNames.CrateRootFor(Path.Combine(root, "src", "round_button.rs"), Exists));
            Assert.IsNull(ControlItemNames.CrateRootFor(Path.Combine(root, "src", "controls", "round_button.rs"), Exists));
            Assert.IsNull(ControlItemNames.CrateRootFor(Path.Combine(root, "src", "main.rs"), Exists), "the root itself");
        }
    }
}
