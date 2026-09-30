using System.Collections.Generic;
using Kubuno.Desktop.Logic.DataSources;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Desktop.Tests.DataSources
{
    [TestClass]
    public sealed class DataSourceCodeWriterTests
    {
        /// <summary>The current desktop application template's main.rs.</summary>
        private const string TemplateMain =
            "//! app - a Kubuno desktop application. `main_view.kbview` is the main window (edit it in\n" +
            "//! the designer), `main_view.rs` its code (`MainView`, like a Windows Forms `Form1`).\n" +
            "#![windows_subsystem = \"windows\"]\n" +
            "\n" +
            "mod main_view;\n" +
            "\n" +
            "fn main() -> kubuno::Result {\n" +
            "    kubuno::Application::run(main_view::MainView::new())\n" +
            "}\n";

        /// <summary>The current desktop application template's Cargo.toml (its comments shortened).</summary>
        private const string TemplateCargo =
            "[package]\n" +
            "name = \"app\"\n" +
            "version = \"0.1.0\"\n" +
            "edition = \"2021\"\n" +
            "\n" +
            "[dependencies]\n" +
            "# Kubuno, the one dependency of a Kubuno desktop application.\n" +
            "kubuno = { path = \"Z:/src/desktop/windows/src/crates/kubuno\" }\n";

        [TestMethod]
        public void ModDataIsAddedAfterTheLastModLineOnce()
        {
            string once = DataSourceCodeWriter.EnsureDataModule(TemplateMain);
            Assert.AreEqual(TemplateMain.Replace("mod main_view;\n", "mod main_view;\nmod data;\n"), once);
            Assert.AreEqual(once, DataSourceCodeWriter.EnsureDataModule(once), "idempotent");

            string crlf = TemplateMain.Replace("\n", "\r\n");
            Assert.AreEqual(crlf.Replace("mod main_view;\r\n", "mod main_view;\r\nmod data;\r\n"), DataSourceCodeWriter.EnsureDataModule(crlf), "line endings kept");
        }

        [TestMethod]
        public void ModDataGoesAfterTheHeaderWhenThereIsNoModLine()
        {
            string main = "//! A tool.\n#![allow(dead_code)]\n\nfn main() {}\n";
            Assert.AreEqual("//! A tool.\n#![allow(dead_code)]\n\nmod data;\n\nfn main() {}\n", DataSourceCodeWriter.EnsureDataModule(main));
            Assert.AreEqual("mod data;\n\nfn main() {}\n", DataSourceCodeWriter.EnsureDataModule("fn main() {}\n"));
            Assert.AreEqual("// c\n\nmod data;\n\nfn main() {}", DataSourceCodeWriter.EnsureDataModule("// c\nfn main() {}"));
        }

        [TestMethod]
        public void MainRegistersTheUserSecretsIdOnce()
        {
            string once = DataSourceCodeWriter.EnsureUserSecretsRegistration(DataSourceCodeWriter.EnsureDataModule(TemplateMain));
            StringAssert.Contains(once,
                "fn main() -> kubuno::Result {\n" +
                "    // The data sources find their connection strings in the environment, the Windows Credential Manager\n" +
                "    // and the user secrets of this application (Cargo.toml [package.metadata.kubuno] user-secrets-id).\n" +
                "    kubuno::data::set_user_secrets_id(kubuno::data::user_secrets_id!().as_deref());\n" +
                "    kubuno::Application::run(main_view::MainView::new())\n" +
                "}\n");
            StringAssert.StartsWith(once, TemplateMain.Substring(0, TemplateMain.IndexOf("fn main", System.StringComparison.Ordinal)).Replace("mod main_view;\n", "mod main_view;\nmod data;\n"));
            Assert.AreEqual(once, DataSourceCodeWriter.EnsureUserSecretsRegistration(once), "idempotent");

            string direct = DataSourceCodeWriter.EnsureUserSecretsRegistration("fn main() {\n\trun();\n}\n", "kubuno_data");
            Assert.AreEqual("fn main() {\n\t// The data sources find their connection strings in the environment, the Windows Credential Manager\n\t// and the user secrets of this application (Cargo.toml [package.metadata.kubuno] user-secrets-id).\n\tkubuno_data::set_user_secrets_id(kubuno_data::user_secrets_id!().as_deref());\n\trun();\n}\n", direct, "the body's indentation");
            string library = "pub fn helper() {}\n";
            Assert.AreSame(library, DataSourceCodeWriter.EnsureUserSecretsRegistration(library), "no fn main: nothing to do");
        }

        [TestMethod]
        public void AnyExistingDeclarationOfTheModuleCounts()
        {
            Assert.IsTrue(DataSourceCodeWriter.DeclaresModule("pub mod data;\n", "data"));
            Assert.IsTrue(DataSourceCodeWriter.DeclaresModule("pub(crate) mod data;\n", "data"));
            Assert.IsTrue(DataSourceCodeWriter.DeclaresModule("#[cfg(feature = \"db\")]\nmod data {\n}\n", "data"));
            Assert.IsTrue(DataSourceCodeWriter.DeclaresModule("  mod data ;", "data"));
            Assert.IsFalse(DataSourceCodeWriter.DeclaresModule("mod database;\n// mod data;\n", "data"));
            string declared = "#![allow(unused)]\npub mod data;\nfn main() {}\n";
            Assert.AreSame(declared, DataSourceCodeWriter.EnsureDataModule(declared));
        }

        [TestMethod]
        public void DataModRsIsCreatedThenExtendedSurgically()
        {
            string created = DataSourceCodeWriter.EnsureModDeclaration(null, "shop");
            StringAssert.StartsWith(created, "//! The data sources of this application");
            StringAssert.EndsWith(created, "\npub mod shop;\n");

            string edited = "//! My data.\n\npub mod shop; // the shop\n\npub fn helper() {}\n";
            string withCrm = DataSourceCodeWriter.EnsureModDeclaration(edited, "crm");
            Assert.AreEqual("//! My data.\n\npub mod shop; // the shop\npub mod crm;\n\npub fn helper() {}\n", withCrm, "comments and code are kept");
            Assert.AreEqual(withCrm, DataSourceCodeWriter.EnsureModDeclaration(withCrm, "crm"));
            Assert.AreEqual(withCrm, DataSourceCodeWriter.EnsureModDeclaration(withCrm, "shop"));
        }

        [TestMethod]
        public void TheUserFileHasTheMacroAndOneImplPerRow()
        {
            string text = DataSourceCodeWriter.UserSourceFile("shop", "shop.kbdata", new[]
            {
                new KeyValuePair<string, string>("customers", "Customer"),
                new KeyValuePair<string, string>("orders", "Order"),
            });
            StringAssert.StartsWith(text, "//! The `shop` data source");
            StringAssert.Contains(text, "\nkubuno::data::data_source!(\"shop.kbdata\");\n");
            StringAssert.Contains(text, "impl Customer {\n    // Your code: methods of the `customers` rows");
            StringAssert.Contains(text, "impl Order {\n");
            StringAssert.Contains(text, "never rewritten");
        }

        [TestMethod]
        public void TheKbdataKeepsTheDevelopersHeaderOnReconfigure()
        {
            string generated = "# Typed data source \"shop\" (generated header).\n\nversion = 1\nname = \"shop\"\n";
            Assert.AreEqual(generated, DataSourceCodeWriter.KbdataText(null, generated), "a new file takes the tool's text");

            string existing = "# My own notes about shop.\r\n# Second line.\r\n\r\nversion = 1\r\nname = \"shop\"\r\n[[tables]]\r\nname = \"old\"\r\n";
            string rewritten = DataSourceCodeWriter.KbdataText(existing, generated + "[[tables]]\nname = \"customers\"\n");
            Assert.AreEqual("# My own notes about shop.\r\n# Second line.\r\n\r\nversion = 1\r\nname = \"shop\"\r\n[[tables]]\r\nname = \"customers\"\r\n", rewritten);
            Assert.AreEqual("# a\n#b\n", DataSourceCodeWriter.LeadingComments("# a\n#b\nx = 1\n# c\n"));
        }

        [TestMethod]
        public void CargoTomlGetsTheDataFeatureAndAUserSecretsId()
        {
            var update = DataSourceCodeWriter.EnsureCargoManifest(TemplateCargo, () => "11111111-2222-3333-4444-555555555555");
            Assert.IsTrue(update.Changed && update.HasKubunoDependency && update.AddedDataFeature && update.AddedUserSecretsId);
            Assert.AreEqual("11111111-2222-3333-4444-555555555555", update.UserSecretsId);
            StringAssert.Contains(update.Text, "# Kubuno, the one dependency of a Kubuno desktop application.\n", "comments kept");
            StringAssert.Contains(update.Text, "kubuno = { path = \"Z:/src/desktop/windows/src/crates/kubuno\", features = [\"data\"] }");
            StringAssert.Contains(update.Text, "user-secrets-id = \"11111111-2222-3333-4444-555555555555\"");
            var reparsed = Kubuno.Rust.Cargo.Toml.TomlDocument.Parse(update.Text);
            Assert.AreEqual("11111111-2222-3333-4444-555555555555", reparsed.GetValue("package", "metadata", "kubuno", "user-secrets-id")!.AsString());
            Assert.AreEqual("app", reparsed.GetValue("package", "name")!.AsString());

            var again = DataSourceCodeWriter.EnsureCargoManifest(update.Text, () => "other");
            Assert.IsFalse(again.Changed, "idempotent");
            Assert.AreEqual(update.Text, again.Text);
            Assert.AreEqual("11111111-2222-3333-4444-555555555555", again.UserSecretsId);
        }

        [TestMethod]
        public void ExistingFeaturesAreMergedAndOtherShapesHandled()
        {
            string withFeatures = "[package]\nname = \"a\"\n\n[package.metadata.kubuno]\nuser-secrets-id = \"keep\"\n\n[dependencies]\nkubuno = { path = \"../kubuno\", features = [\"tray\"] }\n";
            var merged = DataSourceCodeWriter.EnsureCargoManifest(withFeatures, () => "new");
            StringAssert.Contains(merged.Text, "features = [\"tray\", \"data\"]");
            Assert.IsFalse(merged.AddedUserSecretsId);
            Assert.AreEqual("keep", merged.UserSecretsId);

            string version = "[package]\nname = \"a\"\n\n[dependencies]\nkubuno = \"0.1\"\n";
            var converted = DataSourceCodeWriter.EnsureCargoManifest(version, () => "id");
            var document = Kubuno.Rust.Cargo.Toml.TomlDocument.Parse(converted.Text);
            Assert.AreEqual("0.1", document.GetValue("dependencies", "kubuno", "version")!.AsString());
            Assert.AreEqual("data", document.GetValue("dependencies", "kubuno", "features")!.AsArray()![0].AsString());

            string direct = "[package]\nname = \"a\"\n\n[dependencies]\nkubuno-data = { path = \"x\" }\n";
            var directUpdate = DataSourceCodeWriter.EnsureCargoManifest(direct, () => "id");
            Assert.IsTrue(directUpdate.HasKubunoDependency && !directUpdate.AddedDataFeature);
            Assert.AreEqual("kubuno_data", directUpdate.DataCrate);
            Assert.AreEqual("kubuno::data", merged.DataCrate);
            StringAssert.Contains(DataSourceCodeWriter.UserSourceFile("shop", "shop.kbdata", new KeyValuePair<string, string>[0], directUpdate.DataCrate), "\nkubuno_data::data_source!(\"shop.kbdata\");\n");

            var none = DataSourceCodeWriter.EnsureCargoManifest("[package]\nname = \"a\"\n", () => "id");
            Assert.IsFalse(none.HasKubunoDependency);
            Assert.AreEqual("a", DataSourceCodeWriter.PackageName(none.Text));
            Assert.IsNull(DataSourceCodeWriter.PackageName("[workspace]\nmembers = []\n"));
        }
    }
}
