using System.Collections.Generic;
using System.Linq;
using Kubuno.Desktop.Logic.DataSources;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Desktop.Tests.DataSources
{
    [TestClass]
    public sealed class DataSourceNamesTests
    {
        [TestCleanup]
        public void Cleanup() => DataSourcesText.ForceFrench = null;

        [TestMethod]
        public void SourceNamesAreLowerSnakeCaseRustIdentifiers()
        {
            DataSourcesText.ForceFrench = false;
            Assert.IsNull(DataSourceNames.ValidateSourceName("shop"));
            Assert.IsNull(DataSourceNames.ValidateSourceName("shop_2"));
            Assert.IsNull(DataSourceNames.ValidateSourceName("_private"));
            StringAssert.Contains(DataSourceNames.ValidateSourceName("Shop"), "\"shop\"");
            StringAssert.Contains(DataSourceNames.ValidateSourceName("2shop"), "not a Rust identifier");
            StringAssert.Contains(DataSourceNames.ValidateSourceName("my shop"), "not a Rust identifier");
            StringAssert.Contains(DataSourceNames.ValidateSourceName("type"), "not a Rust identifier");
            StringAssert.Contains(DataSourceNames.ValidateSourceName("data"), "reserved");
            StringAssert.Contains(DataSourceNames.ValidateSourceName("mod"), "not a Rust identifier");
            Assert.AreEqual(DataSourcesText.NameRequired, DataSourceNames.ValidateSourceName("  "));
            Assert.IsNotNull(DataSourceNames.ValidateSourceName(new string('a', 65)));
        }

        [TestMethod]
        public void ConnectionNamesFollowTheToolsRules()
        {
            Assert.IsNull(DataSourceNames.ValidateConnectionName("Shop"));
            Assert.IsNull(DataSourceNames.ValidateConnectionName("shop-db.v2_main"));
            Assert.IsNotNull(DataSourceNames.ValidateConnectionName("2shop"));
            Assert.IsNotNull(DataSourceNames.ValidateConnectionName("my shop"), "a space would not survive ConnectionStrings__Name");
            Assert.IsNotNull(DataSourceNames.ValidateConnectionName("a\"b"));
            Assert.IsNotNull(DataSourceNames.ValidateConnectionName(string.Empty));
        }

        [TestMethod]
        public void SuggestedNamesComeFromAConnectionName()
        {
            Assert.AreEqual("shop", DataSourceNames.ToSnakeCase("Shop"));
            Assert.AreEqual("shop_db", DataSourceNames.ToSnakeCase("Shop DB"));
            Assert.AreEqual("order_lines", DataSourceNames.ToSnakeCase("OrderLines"));
            Assert.AreEqual("kubuno_dev_local", DataSourceNames.ToSnakeCase("Kubuno dev (local)"));
            Assert.AreEqual("db_2026", DataSourceNames.ToSnakeCase("2026"));
            Assert.AreEqual("type_db", DataSourceNames.ToSnakeCase("Type"));
            Assert.AreEqual("data_db", DataSourceNames.ToSnakeCase("Data"));
            Assert.AreEqual("Kubuno_dev", DataSourceNames.ToConnectionName("Kubuno dev"));
            Assert.AreEqual("Db2026", DataSourceNames.ToConnectionName("2026"));
            Assert.AreEqual("shop2", DataSourceNames.UniqueSourceName("shop", new[] { "Shop" }));
            Assert.AreEqual("shop3", DataSourceNames.UniqueSourceName("shop", new[] { "shop", "shop2" }));
        }

        [TestMethod]
        public void ElementNamesLabelsAndRowNames()
        {
            Assert.AreEqual("customers", DataSourceNames.ToCamelCase("customers"));
            Assert.AreEqual("orderLines", DataSourceNames.ToCamelCase("order_lines"));
            Assert.AreEqual("customers", DataSourceNames.ToCamelCase("Customers"));
            Assert.AreEqual("id", DataSourceNames.ToCamelCase("ID"));
            Assert.AreEqual("t2026Sales", DataSourceNames.ToCamelCase("2026_sales"));
            Assert.AreEqual("Birth date", DataSourceNames.Humanize("birth_date"));
            Assert.AreEqual("Customer id", DataSourceNames.Humanize("customerId"));
            Assert.AreEqual("Id", DataSourceNames.Humanize("id"));
            Assert.AreEqual("Order URL", DataSourceNames.Humanize("order_URL"));
            Assert.AreEqual("Customer", DataSourceNames.RowName("customers"));
            Assert.AreEqual("Category", DataSourceNames.RowName("categories"));
            Assert.AreEqual("OrderLine", DataSourceNames.RowName("order_lines"));
            Assert.AreEqual("VOrder", DataSourceNames.RowName("v_orders"));
            Assert.AreEqual("Address", DataSourceNames.RowName("shop.addresses"));
            Assert.AreEqual("Status", DataSourceNames.RowName("status"));
        }

        [TestMethod]
        public void ElementNamesAreSnakeCaseFieldsOfTheView()
        {
            Assert.AreEqual("customers", DataSourceNames.ToFieldName("customers"));
            Assert.AreEqual("customers", DataSourceNames.ToFieldName("Customers"));
            Assert.AreEqual("customer_id", DataSourceNames.ToFieldName("customerId"));
            Assert.AreEqual("order_date", DataSourceNames.ToFieldName("Order Date"));
            Assert.AreEqual("v_orders", DataSourceNames.ToFieldName("v_orders"));
            Assert.AreEqual("id", DataSourceNames.ToFieldName("ID"));
            Assert.AreEqual("t2026_sales", DataSourceNames.ToFieldName("2026_sales"));
            Assert.IsTrue(DataSourceNames.IsRustIdentifier(DataSourceNames.ToFieldName("type") + "_label"));
        }

        [TestMethod]
        public void UniqueNamesGetASuffix()
        {
            var taken = new HashSet<string> { "customersTableAdapter", "customersTableAdapter1" };
            Assert.AreEqual("customersTableAdapter2", DataSourceNames.Unique("customersTableAdapter", taken));
            Assert.AreEqual("customersBindingSource", DataSourceNames.Unique("customersBindingSource", taken));
            Assert.IsTrue(taken.Contains("customersTableAdapter2") && taken.Contains("customersBindingSource"));
        }

        [TestMethod]
        public void TheRealKbdataReadAnswerIsParsed()
        {
            var shop = DataSourceFixtures.Shop();
            Assert.AreEqual(("shop", "Shop", "sqlite", string.Empty), (shop.Name, shop.Connection, shop.Provider, shop.Schema));
            CollectionAssert.AreEqual(new[] { "customers", "orders", "v_orders" }, shop.Tables.Select(t => t.Name).ToArray());
            CollectionAssert.AreEqual(new[] { "Customer", "Order", "VOrder" }, shop.Tables.Select(t => t.RowName).ToArray());
            var customers = shop.Table("customers")!;
            CollectionAssert.AreEqual(new[] { "id" }, customers.Key.ToArray());
            Assert.IsTrue(customers.Column("id")!.AutoIncrement);
            Assert.IsTrue(shop.Table("v_orders")!.IsView);
            Assert.AreEqual(DataColumnKind.Integer, customers.Column("age")!.Kind);
            Assert.AreEqual(DataColumnKind.Date, customers.Column("birth_date")!.Kind);
            Assert.AreEqual(DataColumnKind.Boolean, customers.Column("vip")!.Kind);
            Assert.AreEqual(DataColumnKind.Decimal, customers.Column("balance")!.Kind, "NUMERIC stays a decimal even as f64");
            Assert.AreEqual(DataColumnKind.Text, customers.Column("email")!.Kind);
            Assert.AreEqual(DataColumnKind.DateTime, shop.Table("orders")!.Column("ordered")!.Kind);
            Assert.AreEqual("shop", shop.ModuleName);
        }

        [TestMethod]
        public void ColumnsAreClassifiedFromTheirRustThenDatabaseTypes()
        {
            Assert.AreEqual(DataColumnKind.Decimal, DataColumnKinds.Classify("numeric", "String"), "PostgreSQL numeric travels as text");
            Assert.AreEqual(DataColumnKind.DateTime, DataColumnKinds.Classify("timestamptz", "chrono::DateTime<chrono::Utc>"));
            Assert.AreEqual(DataColumnKind.Date, DataColumnKinds.Classify("DATE", "String"), "a SQLite date declared as text");
            Assert.AreEqual(DataColumnKind.Time, DataColumnKinds.Classify("time", "chrono::NaiveTime"));
            Assert.AreEqual(DataColumnKind.Float, DataColumnKinds.Classify("REAL", "f64"));
            Assert.AreEqual(DataColumnKind.Integer, DataColumnKinds.Classify("int4", "Option<i32>"));
            Assert.AreEqual(DataColumnKind.Binary, DataColumnKinds.Classify("bytea", "Vec<u8>"));
            Assert.AreEqual(DataColumnKind.Text, DataColumnKinds.Classify("uuid", "uuid::Uuid"));
            Assert.AreEqual(DataColumnKind.Boolean, DataColumnKinds.Classify("bit", null));
            Assert.AreEqual("d", DataColumnKinds.FormatString(DataColumnKind.Date));
            Assert.AreEqual("g", DataColumnKinds.FormatString(DataColumnKind.DateTime));
            Assert.AreEqual("N2", DataColumnKinds.FormatString(DataColumnKind.Decimal));
            Assert.IsNull(DataColumnKinds.FormatString(DataColumnKind.Integer));
            CollectionAssert.Contains(DataColumnKinds.ControlsFor(DataColumnKind.Date).ToList(), DataControlKind.DatePicker);
            CollectionAssert.DoesNotContain(DataColumnKinds.ControlsFor(DataColumnKind.Text).ToList(), DataControlKind.CheckBox);
        }

        [TestMethod]
        public void DefaultControlsFollowWindowsForms()
        {
            var customers = DataSourceFixtures.Shop().Table("customers")!;
            Assert.AreEqual(DataControlKind.TextField, DataColumnKinds.DefaultControl(customers.Column("id")!), "a generated key is a read-only text field (WinForms' ReadOnly TextBox)");
            Assert.AreEqual(DataControlKind.TextField, DataColumnKinds.DefaultControl(customers.Column("age")!));
            Assert.AreEqual(DataControlKind.TextField, DataColumnKinds.DefaultControl(customers.Column("birth_date")!));
            Assert.AreEqual(DataControlKind.CheckBox, DataColumnKinds.DefaultControl(customers.Column("vip")!));
        }
    }
}
