using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kubuno.VisualStudio.Core.Data;
using Kubuno.VisualStudio.Core.DataSources;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.VisualStudio.Tests.DataSources
{
    [TestClass]
    public sealed class DataSourceWizardModelTests
    {
        private static readonly ExplorerConnectionInfo[] Connections =
        {
            new ExplorerConnectionInfo { Name = "Shop", Provider = "sqlite", Store = "usersecrets", Display = @"C:\db\shop.db" },
            new ExplorerConnectionInfo { Name = "Kubuno dev", Provider = "postgres", Store = "credman", Display = "localhost:5432/kubuno (user kubuno)" },
        };

        private static DatabaseSchemaInfo Schema(params (string Schema, string Table, string Kind)[] objects) => new DatabaseSchemaInfo
        {
            Provider = "postgres",
            Schemas = objects.GroupBy(o => o.Schema).Select(g => new SchemaInfo
            {
                Name = g.Key,
                Tables = g.Select(o => new TableInfo { Name = o.Table, Kind = o.Kind, Columns = new List<ColumnInfo> { new ColumnInfo { Name = "id" } } }).ToList(),
            }).ToList(),
        };

        [TestCleanup]
        public void Cleanup() => DataSourcesText.ForceFrench = null;

        [TestMethod]
        public void TheStepsRequireAConnectionANameObjectsAndAModuleName()
        {
            DataSourcesText.ForceFrench = false;
            var model = new DataSourceWizardModel(Connections, new[] { "shop" }, moduleSchema: null);
            Assert.AreEqual(DataSourceWizardStep.Connection, model.Step);
            Assert.AreEqual("Kubuno dev", model.ExplorerConnection, "sorted: the first one is selected");
            Assert.IsFalse(model.CanGoBack);

            model.ExplorerConnection = "Shop";
            Assert.AreEqual("Shop", model.ConnectionName, "the connection string name follows the connection");
            Assert.AreEqual("shop2", model.SourceName, "shop exists already");
            Assert.IsNull(model.Next());
            Assert.AreEqual(DataSourceWizardStep.Application, model.Step);
            Assert.AreEqual(CredentialStoreKind.UserSecrets, model.Store, "user secrets by default");

            model.ConnectionName = "bad name";
            Assert.IsNotNull(model.Next());
            Assert.AreEqual(DataSourceWizardStep.Application, model.Step);
            model.ConnectionName = "ShopDb";
            Assert.IsNull(model.Next());

            Assert.AreEqual(DataSourcesText.SchemaNotLoaded, model.Next(), "objects need the schema");
            model.LoadObjects(Schema(("public", "customers", "table"), ("public", "orders", "table"), ("public", "v_orders", "view")));
            Assert.AreEqual(DataSourcesText.ChooseObjects, model.Next());
            model.Objects.First(o => o.Name == "customers").Selected = true;
            Assert.IsNull(model.Next());
            Assert.IsTrue(model.IsLastStep);

            model.SourceName = "Shop";
            StringAssert.Contains(model.Validate(DataSourceWizardStep.Name), "snake_case");
            model.SourceName = "shop";
            StringAssert.Contains(model.Validate(DataSourceWizardStep.Name), "already has a data source");
            model.SourceName = "store";
            Assert.IsNull(model.ValidateAll());
            Assert.IsNull(model.SchemaParameter);

            model.Back();
            Assert.AreEqual(DataSourceWizardStep.Objects, model.Step);
            model.ExplorerConnection = "Kubuno dev";
            Assert.AreEqual("ShopDb", model.ConnectionName, "a typed name is kept");
            Assert.AreEqual("store", model.SourceName);
            Assert.IsFalse(model.Objects.Any(), "another connection: its objects must be loaded again");
        }

        [TestMethod]
        public void AKubunoModuleOnlyOffersItsOwnSchema()
        {
            var model = new DataSourceWizardModel(Connections, Array.Empty<string>(), moduleSchema: "calendar");
            model.LoadObjects(Schema(("public", "users", "table"), ("calendar", "events", "table"), ("calendar", "v_agenda", "view")));
            CollectionAssert.AreEqual(new[] { "events", "v_agenda" }, model.Objects.Select(o => o.Name).ToArray());
            Assert.AreEqual("calendar", model.SchemaParameter);
            Assert.IsTrue(model.Objects.All(o => o.Schema == "calendar"));

            DataSourcesText.ForceFrench = false;
            model.LoadObjects(Schema(("public", "users", "table")));
            Assert.AreEqual(0, model.Objects.Count);
            StringAssert.Contains(model.SchemaError, "no schema \"calendar\"");
        }

        [TestMethod]
        public void ConfigureKeepsTheNameAndPreselectsTheSourcesTables()
        {
            DataSourcesText.ForceFrench = false;
            var existing = new DataSourceWizardExisting("shop", "Shop", string.Empty, new[] { "customers", "other.audit" });
            var model = new DataSourceWizardModel(Connections, new[] { "shop" }, null, existing);
            Assert.IsTrue(model.IsReconfigure);
            Assert.AreEqual(("Shop", "shop", "Shop"), (model.ExplorerConnection, model.SourceName, model.ConnectionName));
            model.SourceName = "renamed";
            Assert.AreEqual("shop", model.SourceName, "the module name is fixed");

            var remembered = new DataSourceWizardModel(Connections, new[] { "shop" }, null, new DataSourceWizardExisting("shop", "Shop", string.Empty, new[] { "customers" }, explorerConnection: "Kubuno dev"));
            Assert.AreEqual("Kubuno dev", remembered.ExplorerConnection, "the connection it was built from wins over a same-named one");
            Assert.AreEqual("Shop", remembered.ConnectionName, "the application's connection string name is kept");

            model.LoadObjects(Schema(("main", "customers", "table"), ("main", "orders", "table"), ("other", "audit", "table"), ("main", "audit", "table")));
            CollectionAssert.AreEquivalent(new[] { "main.customers", "other.audit" }, model.SelectedObjects.Select(o => o.Schema + "." + o.Name).ToArray());
            Assert.IsNull(model.Validate(DataSourceWizardStep.Name), "its own name is not a clash");

            var summary = model.Summary(hasMainModule: true, modExists: true, cargoNeedsFeature: false, cargoNeedsSecretsId: false);
            Assert.IsTrue(summary.Any(l => l.Contains("shop.kbdata")));
            Assert.IsFalse(summary.Any(l => l.Contains("shop.rs") && !l.Contains("not")), "Configure never writes the user file");
        }

        [TestMethod]
        public void TheSummarySaysWhatIsWrittenAndNeverTheSecret()
        {
            DataSourcesText.ForceFrench = false;
            var model = new DataSourceWizardModel(Connections, Array.Empty<string>(), null);
            model.ExplorerConnection = "Shop";
            model.LoadObjects(Schema(("main", "customers", "table")));
            model.Objects[0].Selected = true;
            model.Store = CredentialStoreKind.CredentialManager;
            var lines = model.Summary(hasMainModule: false, modExists: false, cargoNeedsFeature: true, cargoNeedsSecretsId: true);
            CollectionAssert.Contains(lines.ToList(), DataSourcesText.SummaryWriteKbdata("shop"));
            CollectionAssert.Contains(lines.ToList(), DataSourcesText.SummaryWriteUserFile("shop"));
            CollectionAssert.Contains(lines.ToList(), DataSourcesText.SummaryCreateMod("shop"));
            CollectionAssert.Contains(lines.ToList(), DataSourcesText.SummaryEditMain);
            CollectionAssert.Contains(lines.ToList(), DataSourcesText.SummaryCargoFeature);
            CollectionAssert.Contains(lines.ToList(), DataSourcesText.SummaryCargoSecretsId);
            StringAssert.Contains(string.Join("\n", lines), "ConnectionStrings:Shop in the Windows Credential Manager");
            Assert.IsFalse(string.Join("\n", lines).Contains(@"C:\db\shop.db"), "not even the redacted display");

            DataSourcesText.ForceFrench = true;
            StringAssert.Contains(string.Join("\n", model.Summary(true, true, false, false)), "Gestionnaire d'informations d'identification");
        }

        [TestMethod]
        public void TheCrateIsFoundWithItsSourcesAndModuleSchema()
        {
            string root = Path.Combine(Path.GetTempPath(), "kubuno-ds-crate-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "app", "src", "data"));
                Directory.CreateDirectory(Path.Combine(root, "app", "src", ".hidden"));
                File.WriteAllText(Path.Combine(root, "Cargo.toml"), "[workspace]\nmembers = [\"app\"]\n");
                File.WriteAllText(Path.Combine(root, "app", "Cargo.toml"), "[package]\nname = \"calendar-module\"\nversion = \"0.1.0\"\n");
                File.WriteAllText(Path.Combine(root, "app", "module.toml"), "# Kubuno module\nid = \"calendar\"\nname = \"Calendar\"\n");
                File.WriteAllText(Path.Combine(root, "app", "src", "data", "events.kbdata"), "version = 1\n");
                File.WriteAllText(Path.Combine(root, "app", "src", "a.kbdata"), "version = 1\n");
                File.WriteAllText(Path.Combine(root, "app", "src", ".hidden", "b.kbdata"), "version = 1\n");
                File.WriteAllText(Path.Combine(root, "app", "src", "data", "mod.rs"), "pub mod events;\n");

                var crate = DataSourceCrate.Find(Path.Combine(root, "app", "src", "data", "mod.rs"))!;
                Assert.AreEqual(Path.Combine(root, "app"), crate.ManifestDirectory);
                Assert.AreEqual("calendar-module", crate.PackageName);
                Assert.AreEqual("calendar", crate.ModuleSchema);
                CollectionAssert.AreEqual(new[] { "a.kbdata", "events.kbdata" }, crate.KbdataFiles().Select(Path.GetFileName).ToArray());
                Assert.IsNull(DataSourceCrate.Find(root), "a virtual workspace manifest is not a crate");
                Assert.IsNull(DataSourceCrate.ReadModuleSchema(root));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [TestMethod]
        public void DropChoicesArePersistedPerProjectOutsideTheKbdata()
        {
            string root = Path.Combine(Path.GetTempPath(), "kubuno-ds-settings-" + Guid.NewGuid().ToString("N"));
            try
            {
                string kbdata = Path.Combine(root, "app", "src", "data", "shop.kbdata");
                var settings = DataSourcesSettings.Load(root);
                Assert.AreEqual(DataTableDropMode.Grid, settings.ModeOf(kbdata, "customers"), "grid by default");
                Assert.IsNull(settings.ControlOf(kbdata, "customers", "age"));
                settings.SetMode(kbdata, "customers", DataTableDropMode.Details);
                settings.SetControl(kbdata, "customers", "age", DataControlKind.NumericField);
                settings.SetControl(kbdata, "customers", "email", DataControlKind.None);
                Assert.IsNull(settings.ExplorerConnectionOf(kbdata));
                settings.SetExplorerConnection(kbdata, "DsLiveShop");
                settings.Save();
                Assert.AreEqual("DsLiveShop", DataSourcesSettings.Load(root).ExplorerConnectionOf(kbdata));
                Assert.AreEqual(Path.Combine(root, ".vs", "kubuno", "datasources.json"), settings.FilePath);
                StringAssert.Contains(File.ReadAllText(settings.FilePath), "\"app/src/data/shop.kbdata\"");

                var reloaded = DataSourcesSettings.Load(root);
                Assert.AreEqual(DataTableDropMode.Details, reloaded.ModeOf(kbdata, "customers"));
                Assert.AreEqual(DataTableDropMode.Grid, reloaded.ModeOf(kbdata, "orders"));
                Assert.AreEqual(DataControlKind.NumericField, reloaded.ControlOf(kbdata.ToUpperInvariant(), "customers", "age"), "paths compare without case");
                var controls = reloaded.ControlsOf(kbdata, "customers");
                Assert.AreEqual(2, controls.Count);
                Assert.AreEqual(DataControlKind.None, controls["email"]);

                File.WriteAllText(settings.FilePath, "{ not json");
                Assert.AreEqual(DataTableDropMode.Grid, DataSourcesSettings.Load(root).ModeOf(kbdata, "customers"), "a corrupt file only loses the choices");
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }
    }
}
