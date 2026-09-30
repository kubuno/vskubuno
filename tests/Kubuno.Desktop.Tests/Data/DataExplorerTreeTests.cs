using System.IO;
using System.Linq;
using System.Text.Json;
using Kubuno.Desktop.Logic.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Desktop.Tests.Data
{
    [TestClass]
    public sealed class DataExplorerTreeTests
    {
        [TestCleanup]
        public void Cleanup() => DataText.ForceFrench = null;

        private static DatabaseSchemaInfo SqliteFixture()
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "Data", "Fixtures", "schema-sqlite.json")));
            return DataToolJson.Parse<DatabaseSchemaInfo>(document.RootElement);
        }

        [TestMethod]
        public void TheSchemaFixtureParses()
        {
            var schema = SqliteFixture();

            Assert.AreEqual(DataProviderKind.Sqlite, schema.ProviderKind);
            Assert.AreEqual("shop.db", schema.Database);
            var orders = schema.Schemas[0].Tables.Single(t => t.Name == "orders");
            Assert.AreEqual("customers", orders.ForeignKeys[0].RefTable);
            Assert.AreEqual("0", orders.Columns.Single(c => c.Name == "amount").DefaultText);
            Assert.IsNull(orders.Columns[0].DefaultText);
            Assert.IsTrue(schema.Schemas[0].Tables.Single(t => t.Name == "big_orders").IsView);
        }

        [TestMethod]
        public void SqliteShowsItsMainSchemaFoldersDirectlyInFrench()
        {
            DataText.ForceFrench = true;

            var nodes = DataExplorerTreeBuilder.BuildConnectionChildren("Shop", SqliteFixture());

            CollectionAssert.AreEqual(new[] { "Tables", "Vues" }, nodes.Select(n => n.Text).ToArray(), "no Fonctions folder for SQLite");
            CollectionAssert.AreEqual(new[] { DataNodeKind.TablesFolder, DataNodeKind.ViewsFolder }, nodes.Select(n => n.Kind).ToArray());
            CollectionAssert.AreEqual(new[] { "customers", "orders" }, nodes[0].Children.Select(n => n.Text).ToArray(), "tables sorted by name");
            Assert.AreEqual("big_orders", nodes[1].Children.Single().Text);
            Assert.AreEqual(DataNodeKind.View, nodes[1].Children.Single().Kind);

            var customers = nodes[0].Children[0];
            Assert.AreEqual(DataNodeKind.Table, customers.Kind);
            Assert.AreEqual("main", customers.Schema);
            Assert.AreEqual("customers", customers.ObjectName);
            Assert.IsTrue(customers.IsTableOrView);
            CollectionAssert.AreEqual(new[] { "Colonnes", "Clés", "Index" }, customers.Children.Select(n => n.Text).ToArray());

            var columns = customers.Children[0].Children;
            CollectionAssert.AreEqual(
                new[]
                {
                    "id (INTEGER, PK, auto-incrément, non NULL)",
                    "name (TEXT, non NULL)",
                    "email (TEXT, NULL)",
                    "age (INTEGER, NULL)",
                    "birth_date (DATE, NULL)",
                },
                columns.Select(c => c.Text).ToArray(),
                "columns keep the table's order");
            Assert.AreEqual(DataNodeKind.PrimaryKeyColumn, columns[0].Kind);
            Assert.AreEqual(DataNodeKind.Column, columns[1].Kind);
            Assert.AreEqual("Rust: Option<String>", columns[2].ToolTip);

            CollectionAssert.AreEqual(new[] { "PK (id)" }, customers.Children[1].Children.Select(n => n.Text).ToArray());
            CollectionAssert.AreEqual(new[] { "ix_customers_age (age)", "sqlite_autoindex_customers_1 (email), unique" }, customers.Children[2].Children.Select(n => n.Text).ToArray());
            CollectionAssert.AreEqual(new[] { DataNodeKind.Index, DataNodeKind.Index }, customers.Children[2].Children.Select(n => n.Kind).ToArray());
        }

        [TestMethod]
        public void ForeignKeysPointToTheirTableInEnglish()
        {
            DataText.ForceFrench = false;

            var nodes = DataExplorerTreeBuilder.BuildConnectionChildren("Shop", SqliteFixture());
            var orders = nodes[0].Children[1];

            CollectionAssert.AreEqual(new[] { "Columns", "Keys", "Indexes" }, orders.Children.Select(n => n.Text).ToArray());
            Assert.AreEqual("customer_id (INTEGER, FK, not null)", orders.Children[0].Children[1].Text);
            Assert.AreEqual("id (INTEGER, PK, identity, not null)", orders.Children[0].Children[0].Text);
            var keys = orders.Children[1].Children;
            CollectionAssert.AreEqual(new[] { DataNodeKind.PrimaryKey, DataNodeKind.ForeignKey }, keys.Select(k => k.Kind).ToArray());
            Assert.AreEqual("fk_orders_customer (customer_id) → customers (id)", keys[1].Text);
            // An empty folder shows a placeholder rather than an expander that opens on nothing.
            Assert.AreEqual(DataNodeKind.Message, orders.Children[2].Children.Single().Kind);
            Assert.AreEqual("(none)", orders.Children[2].Children.Single().Text);
        }

        [TestMethod]
        public void PostgresShowsSchemasFunctionsAndProcedures()
        {
            DataText.ForceFrench = true;
            var database = new DatabaseSchemaInfo
            {
                Provider = "postgres",
                Schemas =
                {
                    new SchemaInfo { Name = "shop", Tables = { new TableInfo { Name = "items", Columns = { new ColumnInfo { Name = "label", DbType = "varchar", MaxLength = 80, Nullable = false } } } }, Functions = { new FunctionInfo { Name = "total", ReturnType = "numeric", Arguments = "order_id integer" }, new FunctionInfo { Name = "purge", Kind = "procedure" } } },
                    new SchemaInfo { Name = "public" },
                },
            };

            var nodes = DataExplorerTreeBuilder.BuildConnectionChildren("Pg", database);

            CollectionAssert.AreEqual(new[] { "public", "shop" }, nodes.Select(n => n.Text).ToArray());
            Assert.IsTrue(nodes.All(n => n.Kind == DataNodeKind.Schema));
            var shop = nodes[1];
            CollectionAssert.AreEqual(new[] { "Tables", "Vues", "Fonctions", "Procédures stockées" }, shop.Children.Select(n => n.Text).ToArray());
            Assert.AreEqual("total(order_id integer) → numeric", shop.Children[2].Children.Single().Text);
            Assert.AreEqual(DataNodeKind.Procedure, shop.Children[3].Children.Single().Kind);
            Assert.AreEqual("label (varchar(80), non NULL)", shop.Children[0].Children.Single().Children[0].Children.Single().Text);
            Assert.AreEqual("shop", shop.Children[0].Children.Single().Schema);
        }
    }
}
