using System.IO;
using Kubuno.Desktop.TemplateWizard;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Desktop.Tests.Wizard
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
        public void Dependencies_are_read_from_every_dependency_table()
        {
            const string toml = "[package]\nname = \"app\"\n\n[dependencies]\nkubuno = { path = \"x\" }\n\n[dev-dependencies.kubuno-views]\npath = \"y\"\n";
            Assert.IsTrue(ControlItemNames.DependsOn(toml, "kubuno"));
            Assert.IsTrue(ControlItemNames.DependsOn(toml, "kubuno-views"));
            Assert.IsFalse(ControlItemNames.DependsOn(toml, "kubuno-ui"));
            Assert.IsFalse(ControlItemNames.DependsOn("[package]\nname = \"kubuno\"\n", "kubuno"), "the package's own name is no dependency");
        }

        [TestMethod]
        public void A_facade_project_names_kubuno_views_through_kubuno()
        {
            Assert.AreEqual(
                "use kubuno::views::prelude::*;\n#[kubuno::views::event_handlers]\nimpl X {}\nfn f() { crate::kubuno_views::g(); }\n",
                ControlItemNames.RetargetToFacade("use kubuno_views::prelude::*;\n#[kubuno_views::event_handlers]\nimpl X {}\nfn f() { crate::kubuno_views::g(); }\n"));
        }

        [TestMethod]
        public void A_form_added_to_an_older_project_brings_the_facade_next_to_kubuno_views()
        {
            const string toml = "[dependencies]\r\nkubuno-ui       = { path = \"Z:/src/desktop/windows/src/crates/kubuno-ui\" }\r\nkubuno-views    = { path = \"Z:/src/desktop/windows/src/crates/kubuno-views\" }\r\n";
            Assert.AreEqual(
                toml + "kubuno          = { path = \"Z:/src/desktop/windows/src/crates/kubuno\" }\r\n",
                ControlItemNames.AddFacadeDependency(toml));
            Assert.IsNull(ControlItemNames.AddFacadeDependency(toml + "kubuno = { path = \"k\" }\r\n"), "already there");
            Assert.IsNull(ControlItemNames.AddFacadeDependency("[dependencies]\nserde = \"1\"\n"), "no kubuno-views path to derive it from");
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

        /// <summary>
        /// Found live: Add New Item on the project node writes the user control next to Cargo.toml; it is declared from
        /// the crate root with a #[path], as Windows Forms compiles a UserControl added anywhere in the project.
        /// </summary>
        [TestMethod]
        public void A_control_added_at_the_project_root_is_declared_with_a_path()
        {
            var root = Path.Combine(Path.GetTempPath(), "app");
            bool Exists(string path) => path == Path.Combine(root, "Cargo.toml") || path == Path.Combine(root, "src", "main.rs");
            var file = Path.Combine(root, "address_editor.rs");
            var crateRoot = ControlItemNames.CrateRootFor(file, Exists);
            Assert.AreEqual(Path.Combine(root, "src", "main.rs"), crateRoot);
            Assert.AreEqual("../address_editor.rs", ControlItemNames.ModulePathFrom(crateRoot!, file));
            Assert.AreEqual("address_editor.rs", ControlItemNames.ModulePathFrom(crateRoot!, Path.Combine(root, "src", "address_editor.rs")));
            var text = ControlItemNames.DeclareModule("mod main_view;\n\nfn main() {}\n", "address_editor", "../address_editor.rs");
            StringAssert.Contains(text, "mod main_view;\n#[path = \"../address_editor.rs\"]\nmod address_editor;\n");
            Assert.AreEqual(root, ControlItemNames.PackageDirectoryFor(Path.Combine(root, "ui", "x.rs"), Exists));
        }
    }
}
