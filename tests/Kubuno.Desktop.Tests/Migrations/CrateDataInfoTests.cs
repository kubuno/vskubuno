using System;
using System.IO;
using Kubuno.VisualStudio.Core.Data;
using Kubuno.VisualStudio.Core.Migrations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.VisualStudio.Tests.Migrations
{
    [TestClass]
    public sealed class CrateDataInfoTests
    {
        private string? _directory;

        [TestCleanup]
        public void Cleanup()
        {
            MigrationText.ForceFrench = null;
            if (_directory != null && Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }

        private string NewCrate(string cargoToml)
        {
            _directory = Path.Combine(Path.GetTempPath(), "kubuno-migrations-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_directory, "src", "data"));
            File.WriteAllText(Path.Combine(_directory, "Cargo.toml"), cargoToml);
            return _directory;
        }

        [TestMethod]
        public void KbdataConnectionProviderAndSchema()
        {
            var source = CrateDataInfo.ParseKbdata("version = 1\nname = \"Shop\"\nconnection = \"Shop\"\nprovider = \"postgres\"\nschema = \"shop\"\n", @"C:\a\shop.kbdata");
            Assert.IsNotNull(source);
            Assert.AreEqual("Shop", source!.Connection);
            Assert.AreEqual(DataProviderKind.Postgres, source.ProviderKind);
            Assert.AreEqual("shop", source.Schema);

            var sqlite = CrateDataInfo.ParseKbdata("name = \"Shop\"\nconnection = \"Local\"\nprovider = \"sqlite\"\n", "x");
            Assert.AreEqual(DataProviderKind.Sqlite, sqlite!.ProviderKind);
            Assert.IsNull(sqlite.Schema);

            Assert.IsNull(CrateDataInfo.ParseKbdata("name = \"x\"\n", "x"), "no connection");
            Assert.IsNull(CrateDataInfo.ParseKbdata("connection = \"", "x"), "not TOML");
        }

        [TestMethod]
        public void ManifestPackageNameAndUserSecretsId()
        {
            var (name, id) = CrateDataInfo.ParseManifest("[package]\nname = \"mig-live\"\nversion = \"0.1.0\"\n\n[package.metadata.kubuno]\nuser-secrets-id = \"mig-live-check\"\n");
            Assert.AreEqual("mig-live", name);
            Assert.AreEqual("mig-live-check", id);
            Assert.AreEqual((null, null), CrateDataInfo.ParseManifest("[workspace]\nmembers = []\n"));
            Assert.AreEqual((null, null), CrateDataInfo.ParseManifest("[package"));
        }

        [TestMethod]
        public void ReadsTheCrate()
        {
            var crate = NewCrate("[package]\nname = \"shop-app\"\n[package.metadata.kubuno]\nuser-secrets-id = \"shop-dev\"\n");
            File.WriteAllText(Path.Combine(crate, "src", "data", "shop.kbdata"), "name = \"Shop\"\nconnection = \"Shop\"\nprovider = \"postgres\"\nschema = \"shop\"\n");
            File.WriteAllText(Path.Combine(crate, "src", "data", "sales.kbdata"), "name = \"Sales\"\nconnection = \"shop\"\nprovider = \"postgres\"\n");
            File.WriteAllText(Path.Combine(crate, "src", "broken.kbdata"), "connection = ");

            var info = CrateDataInfo.Read(crate);
            Assert.AreEqual("shop-app", info.PackageName);
            Assert.AreEqual("shop-app", info.DisplayName);
            Assert.AreEqual("shop-dev", info.UserSecretsId);
            Assert.AreEqual(Path.Combine(crate, "migrations"), info.MigrationsDirectory);
            Assert.AreEqual(2, info.DataSources.Count);
            CollectionAssert.AreEqual(new[] { "shop" }, (System.Collections.ICollection)info.ConnectionNames, "distinct, case-insensitively, in path order (sales.kbdata first)");
            Assert.AreEqual(3, CrateDataInfo.KbdataFiles(crate).Count);
        }

        [TestMethod]
        public void ChoosingTheConnection()
        {
            var one = CrateDataInfo.Create(@"C:\a", "a", null, new[] { new CrateDataSource("Shop", "sqlite", null, "1"), new CrateDataSource("SHOP", "sqlite", null, "2") });
            var single = one.Choose(null);
            Assert.AreEqual(ConnectionChoiceKind.Single, single.Kind);
            Assert.AreEqual("Shop", single.Connection);

            var two = CrateDataInfo.Create(@"C:\a", "a", null, new[] { new CrateDataSource("Shop", null, null, "1"), new CrateDataSource("Sales", null, null, "2") });
            var several = two.Choose(null);
            Assert.AreEqual(ConnectionChoiceKind.Several, several.Kind);
            Assert.IsNull(several.Connection);
            CollectionAssert.AreEqual(new[] { "Shop", "Sales" }, (System.Collections.ICollection)several.Candidates);
            Assert.AreEqual("Sales", two.Choose(" Sales ").Connection, "a remembered choice wins");

            var none = CrateDataInfo.Create(@"C:\a", "a", null, Array.Empty<CrateDataSource>()).Choose("");
            Assert.AreEqual(ConnectionChoiceKind.None, none.Kind);
            Assert.AreEqual(0, none.Candidates.Count);
        }

        [TestMethod]
        public void SchemaOnlyForSchemaProviders()
        {
            var info = CrateDataInfo.Create(@"C:\a", "a", null, new[]
            {
                new CrateDataSource("Pg", "postgres", "crm", "1"),
                new CrateDataSource("My", "mysql", "crm", "2"),
                new CrateDataSource("Lite", "sqlite", "main", "3"),
                new CrateDataSource("Ms", "sqlserver", "dbo", "4"),
                new CrateDataSource("None", "postgres", null, "5"),
            });
            Assert.AreEqual("crm", info.SchemaFor("pg"));
            Assert.AreEqual("crm", info.SchemaFor("My"));
            Assert.IsNull(info.SchemaFor("Lite"), "SQLite has no CREATE SCHEMA");
            Assert.IsNull(info.SchemaFor("Ms"));
            Assert.IsNull(info.SchemaFor("None"));
            Assert.IsNull(info.SchemaFor("Unknown"));
            Assert.IsNull(info.SchemaFor(null));
        }

        [TestMethod]
        public void TheTargetIsTheProjectConnection()
        {
            var info = CrateDataInfo.Create(@"C:\src\shop", "shop", null, new[] { new CrateDataSource("Shop", "sqlite", null, "1") });
            Assert.AreEqual("{\"project\":{\"manifestDir\":\"C:\\\\src\\\\shop\",\"connection\":\"Shop\",\"provider\":\"sqlite\"}}", info.TargetFor("Shop").ToJson().ToJsonString());
            Assert.AreEqual("{\"project\":{\"manifestDir\":\"C:\\\\src\\\\shop\",\"connection\":\"Other\"}}", info.TargetFor("Other").ToJson().ToJsonString(), "no provider: the helper infers it");
            Assert.AreEqual("ConnectionStrings:Shop", CrateDataInfo.SecretKey("Shop"));
        }

        [TestMethod]
        public void ConnectionNames()
        {
            Assert.IsTrue(CrateDataInfo.IsValidConnectionName("Shop"));
            Assert.IsTrue(CrateDataInfo.IsValidConnectionName("My Shop_2.dev-1"));
            Assert.IsFalse(CrateDataInfo.IsValidConnectionName(""));
            Assert.IsFalse(CrateDataInfo.IsValidConnectionName("a:b"));
            Assert.IsFalse(CrateDataInfo.IsValidConnectionName("é"));
            Assert.IsFalse(CrateDataInfo.IsValidConnectionName(new string('x', 65)));
        }

        [TestMethod]
        public void UserSecretsConnectionNamesNeverReturnValues()
        {
            var names = CrateDataInfo.ConnectionNamesFromSecretsJson("{\"ConnectionStrings:Shop\":\"sqlite:C:\\\\db\\\\shop.db\",\"ConnectionStrings\":{\"Sales\":\"postgres://u:p@h/db\",\"shop\":\"x\"},\"Other:Key\":\"v\"}");
            CollectionAssert.AreEqual(new[] { "Shop", "Sales" }, (System.Collections.ICollection)names);
            Assert.AreEqual(0, CrateDataInfo.ConnectionNamesFromSecretsJson("not json").Count);
            Assert.AreEqual(0, CrateDataInfo.ConnectionNamesFromSecretsJson("[1]").Count);
            Assert.AreEqual(0, CrateDataInfo.UserSecretsConnectionNames(@"C:\does\not\exist\secrets.json").Count);
            Assert.AreEqual(Path.Combine(@"C:\Users\u\AppData\Roaming", "Kubuno", "UserSecrets", "mig-live-check", "secrets.json"), CrateDataInfo.UserSecretsPath(@"C:\Users\u\AppData\Roaming", "mig-live-check"));
        }
    }
}
