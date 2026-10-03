using System;
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

        private const string MainRs = "//! My app.\r\n#![windows_subsystem = \"windows\"]\r\n\r\nmod main_view;\r\n\r\nuse kubuno_desktop_ui::Rect;\r\n\r\nfn main() {}\r\n";

        [TestMethod]
        public void The_module_is_declared_after_the_last_one()
        {
            var updated = ControlItemNames.DeclareModule(MainRs, "round_button");
            Assert.AreEqual("//! My app.\r\n#![windows_subsystem = \"windows\"]\r\n\r\nmod main_view;\r\nmod round_button;\r\n\r\nuse kubuno_desktop_ui::Rect;\r\n\r\nfn main() {}\r\n", updated);
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
            const string toml = "[package]\nname = \"app\"\n\n[dependencies]\nkubuno-desktop = { path = \"x\" }\n\n[dev-dependencies.kubuno-desktop-views]\npath = \"y\"\n";
            Assert.IsTrue(ControlItemNames.DependsOn(toml, "kubuno-desktop"));
            Assert.IsTrue(ControlItemNames.DependsOn(toml, "kubuno-desktop-views"));
            Assert.IsFalse(ControlItemNames.DependsOn(toml, "kubuno-desktop-ui"));
            Assert.IsFalse(ControlItemNames.DependsOn("[package]\nname = \"kubuno\"\n", "kubuno"), "the package's own name is no dependency");
        }

        [TestMethod]
        public void A_facade_project_names_kubuno_views_through_kubuno()
        {
            Assert.AreEqual(
                "use kubuno_desktop::views::prelude::*;\n#[kubuno_desktop::views::event_handlers]\nimpl X {}\nfn f() { crate::kubuno_desktop_views::g(); }\n",
                ControlItemNames.RetargetToFacade("use kubuno_desktop_views::prelude::*;\n#[kubuno_desktop_views::event_handlers]\nimpl X {}\nfn f() { crate::kubuno_desktop_views::g(); }\n"));
        }

        [TestMethod]
        public void A_form_added_to_an_older_project_brings_the_facade_next_to_kubuno_views()
        {
            const string toml = "[dependencies]\r\nkubuno-desktop-ui       = { path = \"Z:/src/desktop/windows/src/crates/kubuno-desktop-ui\" }\r\nkubuno-desktop-views    = { path = \"Z:/src/desktop/windows/src/crates/kubuno-desktop-views\" }\r\n";
            Assert.AreEqual(
                toml + "kubuno-desktop          = { path = \"Z:/src/desktop/windows/src/crates/kubuno-desktop\" }\r\n",
                ControlItemNames.AddFacadeDependency(toml));
            Assert.IsNull(ControlItemNames.AddFacadeDependency(toml + "kubuno-desktop = { path = \"k\" }\r\n"), "already there");

            // A project of a desktop checkout older than the 2026-10 rename keeps the former names.
            const string legacy = "[dependencies]\r\nkubuno-ui       = { path = \"Z:/src/desktop/windows/src/crates/kubuno-ui\" }\r\nkubuno-views    = { path = \"Z:/src/desktop/windows/src/crates/kubuno-views\" }\r\n";
            Assert.AreEqual(
                legacy + "kubuno          = { path = \"Z:/src/desktop/windows/src/crates/kubuno\" }\r\n",
                ControlItemNames.AddFacadeDependency(legacy));
            Assert.IsTrue(ControlItemNames.UsesLegacyNames(legacy));
            Assert.AreEqual("use kubuno_views::prelude::*;\n#[kubuno::view]\n", ControlItemNames.RetargetToLegacyNames("use kubuno_desktop_views::prelude::*;\n#[kubuno_desktop::view]\n"));
            Assert.IsNull(ControlItemNames.AddFacadeDependency("[dependencies]\nserde = \"1\"\n"), "no kubuno-desktop-views path to derive it from");
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
        /// docs/DESKTOP-MIGRATION.md "Source layout": a view added to a sub-folder of <c>src</c> (Add New Item on the folder
        /// in Solution Explorer) is declared by that folder's module file, with the visibility its siblings have.
        /// </summary>
        [TestMethod]
        public void A_control_added_to_a_source_folder_is_declared_by_the_folder_module()
        {
            var root = Path.Combine(Path.GetTempPath(), "kubuno-wizard-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "src", "pages"));
                File.WriteAllText(Path.Combine(root, "Cargo.toml"), "[package]\nname = \"app\"\n");
                File.WriteAllText(Path.Combine(root, "src", "lib.rs"), "//! App.\n\npub mod pages;\n");
                File.WriteAllText(Path.Combine(root, "src", "pages", "mod.rs"), "//! Pages.\n\npub mod home_page;\n");
                var file = Path.Combine(root, "src", "pages", "account_row.rs");
                File.WriteAllText(file, "// row\n");

                Assert.AreEqual(Path.Combine(root, "src", "pages", "mod.rs"), ControlItemNames.ParentModuleFor(file, File.Exists));
                Assert.IsTrue(ControlItemNames.DeclareModuleOnDisk(file));
                Assert.AreEqual("//! Pages.\n\npub mod home_page;\npub mod account_row;\n", File.ReadAllText(Path.Combine(root, "src", "pages", "mod.rs")));
                Assert.AreEqual("//! App.\n\npub mod pages;\n", File.ReadAllText(Path.Combine(root, "src", "lib.rs")), "the crate root is left alone");
                Assert.IsFalse(ControlItemNames.DeclareModuleOnDisk(file), "already declared");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        /// <summary>A view added to a folder that is not a module yet creates its <c>mod.rs</c>, declared up to the crate root.</summary>
        [TestMethod]
        public void A_control_added_to_a_new_folder_creates_its_module_up_to_the_crate_root()
        {
            var root = Path.Combine(Path.GetTempPath(), "kubuno-wizard-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "src", "admin", "sections"));
                File.WriteAllText(Path.Combine(root, "Cargo.toml"), "[package]\nname = \"app\"\n");
                File.WriteAllText(Path.Combine(root, "src", "lib.rs"), "//! App.\n\npub mod views;\n");
                var file = Path.Combine(root, "src", "admin", "sections", "users_section.rs");
                File.WriteAllText(file, "// section\n");

                Assert.IsNull(ControlItemNames.ParentModuleFor(file, File.Exists));
                Assert.AreEqual(Path.Combine(root, "src", "admin", "sections", "mod.rs"), ControlItemNames.FolderModuleToCreate(file, File.Exists));
                Assert.IsTrue(ControlItemNames.DeclareModuleOnDisk(file));
                StringAssert.Contains(File.ReadAllText(Path.Combine(root, "src", "admin", "sections", "mod.rs")), "\npub mod users_section;\n");
                StringAssert.Contains(File.ReadAllText(Path.Combine(root, "src", "admin", "mod.rs")), "\npub mod sections;\n");
                Assert.AreEqual("//! App.\n\npub mod views;\npub mod admin;\n", File.ReadAllText(Path.Combine(root, "src", "lib.rs")));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        /// <summary>A folder declared the 2018 way (<c>pages.rs</c> next to <c>pages\</c>) declares its files without a #[path].</summary>
        [TestMethod]
        public void A_folder_with_a_sibling_module_file_declares_its_files_plainly()
        {
            var root = Path.Combine(Path.GetTempPath(), "kubuno-wizard-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "src", "pages"));
                File.WriteAllText(Path.Combine(root, "Cargo.toml"), "[package]\nname = \"app\"\n");
                File.WriteAllText(Path.Combine(root, "src", "main.rs"), "mod pages;\nfn main() {}\n");
                File.WriteAllText(Path.Combine(root, "src", "pages.rs"), "mod home_page;\n");
                var file = Path.Combine(root, "src", "pages", "login_page.rs");
                File.WriteAllText(file, "// page\n");

                Assert.IsTrue(ControlItemNames.DeclareModuleOnDisk(file));
                Assert.AreEqual("mod home_page;\nmod login_page;\n", File.ReadAllText(Path.Combine(root, "src", "pages.rs")));
            }
            finally
            {
                Directory.Delete(root, true);
            }
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
