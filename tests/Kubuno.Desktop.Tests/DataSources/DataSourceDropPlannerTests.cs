using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Kubuno.Desktop.Logic.DataSources;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Desktop.Tests.DataSources
{
    [TestClass]
    public sealed class DataSourceDropPlannerTests
    {
        [TestCleanup]
        public void Cleanup() => DataSourcesText.ForceFrench = null;

        private static DataDropPlan Plan(string text, DataDropRequest request, string parentId = "", int? index = null, double? x = 24, double? y = 80)
        {
            var plan = DataSourceDropPlanner.Plan(text, request, parentId, index, x, y, out var error);
            Assert.IsNotNull(plan, error);
            return plan!;
        }

        private static DataDropRequest Grid(string table, IReadOnlyDictionary<string, DataControlKind>? controls = null)
        {
            var shop = DataSourceFixtures.Shop();
            return new DataDropRequest(shop, shop.Table(table)!, DataTableDropMode.Grid, controls);
        }

        private static DataDropRequest Details(string table, IReadOnlyDictionary<string, DataControlKind>? controls = null)
        {
            var shop = DataSourceFixtures.Shop();
            return new DataDropRequest(shop, shop.Table(table)!, DataTableDropMode.Details, controls);
        }

        /// <summary>Applies the insertions the way kubuno-views-ls's insertFragment does (each at its parent's child index, one element per line).</summary>
        internal static string Apply(string text, DataDropPlan plan)
        {
            var outline = KbviewOutline.TryParse(text)!;
            var edits = new List<(int Offset, string Text)>();
            foreach (var insertion in plan.Insertions)
            {
                var parent = outline.Find(insertion.ParentId)!;
                string open = "<" + parent.Name;
                int offset;
                if (insertion.Index < parent.Children.Count)
                {
                    offset = OffsetOf(text, parent.Children[insertion.Index]);
                }
                else
                {
                    // Before the parent's end tag.
                    int start = OffsetOf(text, parent);
                    int depth = 0;
                    offset = -1;
                    for (int i = start; i < text.Length; i++)
                    {
                        if (string.CompareOrdinal(text, i, open, 0, open.Length) == 0 && (text[i + open.Length] == ' ' || text[i + open.Length] == '>'))
                        {
                            depth++;
                        }
                        else if (string.CompareOrdinal(text, i, "</" + parent.Name + ">", 0, parent.Name.Length + 3) == 0)
                        {
                            depth--;
                            if (depth == 0)
                            {
                                offset = i;
                                break;
                            }
                        }
                    }

                    Assert.IsTrue(offset > 0, "the parent has an end tag");
                }

                edits.Add((offset, insertion.Xml.Replace("\n", "\n  ") + "\n"));
            }

            var builder = new StringBuilder(text);
            foreach (var edit in edits.OrderByDescending(e => e.Offset))
            {
                builder.Insert(edit.Offset, "  " + edit.Text);
            }

            return builder.ToString();
        }

        private static int OffsetOf(string text, KbviewElement element)
        {
            // The n-th start tag of that name whose attributes match (enough for the fixtures).
            string name = element.XName is { } x ? "x:Name=\"" + x + "\"" : "<" + element.Name;
            int offset = text.IndexOf(name, System.StringComparison.Ordinal);
            return text.LastIndexOf('<', offset);
        }

        [TestMethod]
        public void AGridDropCreatesTheComponentsTheNavigatorAndTheTable()
        {
            var plan = Plan(DataSourceFixtures.TemplateView, Grid("customers"));

            Assert.AreEqual(2, plan.Insertions.Count, "components first in the tray, the controls at the drop point");
            var components = plan.Insertions[0];
            Assert.AreEqual((string.Empty, 0), (components.ParentId, components.Index));
            Assert.AreEqual(
                "<DbConnection x:Name=\"shop_connection\" Provider=\"Sqlite\" ConnectionStringName=\"Shop\"/>\n" +
                "<TableAdapter x:Name=\"customers_table_adapter\" Connection=\"shop_connection\" SelectCommand=\"SELECT id, name, email, age, birth_date, vip, balance FROM customers\" UpdateTable=\"customers\" PrimaryKey=\"id\"/>\n" +
                "<BindingSource x:Name=\"customers_binding_source\" DataSource=\"customers_table_adapter\" AutoFill=\"true\"/>\n" +
                "<ErrorProvider x:Name=\"customers_error_provider\" DataSource=\"customers_binding_source\"/>",
                components.Xml);

            var visuals = plan.Insertions[1];
            Assert.AreEqual((string.Empty, 2), (visuals.ParentId, visuals.Index));
            StringAssert.StartsWith(visuals.Xml, "<BindingNavigator x:Name=\"customers_binding_navigator\" BindingSource=\"customers_binding_source\" X=\"24\" Y=\"80\" Width=\"360\" Height=\"34\"/>\n<DataTable x:Name=\"customers_data_table\" ItemsSource=\"{Binding Source=customers_binding_source}\" SelectedIndex=\"{Binding Source=customers_binding_source, Path=Position, Mode=TwoWay}\" X=\"24\" Y=\"122\"");
            StringAssert.Contains(visuals.Xml, "\n  <Column Header=\"Id\" Binding=\"{Binding id}\" Width=\"80\" Alignment=\"Right\" ReadOnly=\"true\"/>");
            StringAssert.Contains(visuals.Xml, "<Column Header=\"Birth date\" Binding=\"{Binding birth_date}\" Width=\"100\" FormatString=\"d\"/>");
            StringAssert.Contains(visuals.Xml, "<Column Header=\"Vip\" Binding=\"{Binding vip}\" Width=\"70\"/>");
            StringAssert.Contains(visuals.Xml, "<Column Header=\"Balance\" Binding=\"{Binding balance}\" Width=\"100\" FormatString=\"N2\" Alignment=\"Right\"/>");
            StringAssert.Contains(visuals.Xml, "<Column Header=\"Age\" Binding=\"{Binding age}\" Width=\"80\" Alignment=\"Right\"/>");
            StringAssert.EndsWith(visuals.Xml, "\n</DataTable>");

            // The grid's id once the four components are inserted before the template's two controls.
            Assert.AreEqual("7", plan.SelectElementId);
            CollectionAssert.AreEqual(
                new[] { "shop_connection", "customers_table_adapter", "customers_binding_source", "customers_error_provider", "customers_binding_navigator", "customers_data_table" },
                plan.CreatedNames.ToArray());

            var after = KbviewOutline.TryParse(Apply(DataSourceFixtures.TemplateView, plan));
            Assert.IsNotNull(after, "the view stays well-formed");
            Assert.AreEqual("DataTable", after!.Find(plan.SelectElementId)!.Name);
            Assert.AreEqual(7, after.Find("7")!.Children.Count);
        }

        [TestMethod]
        public void ADetailsDropReusesTheTablesComponentsAndLaysOutOneRowPerColumn()
        {
            var first = Apply(DataSourceFixtures.TemplateView, Plan(DataSourceFixtures.TemplateView, Grid("customers")));
            var plan = Plan(first, Details("customers"), x: 520, y: 80);

            Assert.AreEqual(1, plan.Insertions.Count, "the connection, adapter, binding source, error provider and navigator exist already");
            string xml = plan.Insertions[0].Xml;
            Assert.IsFalse(xml.Contains("<DbConnection") || xml.Contains("<TableAdapter") || xml.Contains("<BindingSource") || xml.Contains("<BindingNavigator"), xml);
            // The grid of the first drop covers (520, 80): the rows move below it (a drop never lands on controls).
            StringAssert.Contains(xml, "<Label x:Name=\"id_label\" Text=\"Id\" X=\"520\" Y=\"354\" Width=\"112\" Height=\"26\"/>");
            StringAssert.Contains(xml, "<TextField x:Name=\"id_text_field\" Text=\"{Binding Source=customers_binding_source, Path=id}\" Enabled=\"false\" X=\"640\" Y=\"350\"");
            StringAssert.Contains(xml, "<TextField x:Name=\"name_text_field\" Text=\"{Binding Source=customers_binding_source, Path=name, Mode=TwoWay}\" X=\"640\" Y=\"390\"");
            StringAssert.Contains(xml, "<TextField x:Name=\"email_text_field\" Text=\"{Binding Source=customers_binding_source, Path=email, NullValue='', Mode=TwoWay}\"");
            StringAssert.Contains(xml, "<TextField x:Name=\"birth_date_text_field\" Text=\"{Binding Source=customers_binding_source, Path=birth_date, FormatString=d, NullValue='', Mode=TwoWay}\"");
            StringAssert.Contains(xml, "<CheckBox x:Name=\"vip_check_box\" Text=\"Vip\" Checked=\"{Binding Source=customers_binding_source, Path=vip, Mode=TwoWay}\"");
            StringAssert.Contains(xml, "Path=balance, FormatString=N2, NullValue='', Mode=TwoWay}");
            Assert.IsFalse(xml.Contains("vip_label"), "a check box carries its own text");
            Assert.IsNotNull(KbviewOutline.TryParse(Apply(first, plan)));
        }

        [TestMethod]
        public void ChosenControlsAndNoneAreHonoured()
        {
            var controls = new Dictionary<string, DataControlKind>
            {
                ["age"] = DataControlKind.NumericField,
                ["birth_date"] = DataControlKind.DatePicker,
                ["email"] = DataControlKind.None,
                ["name"] = DataControlKind.CheckBox, // not offered for text: the default is used
            };
            string xml = Plan(DataSourceFixtures.TemplateView, Details("customers", controls)).Insertions.Last().Xml;
            StringAssert.Contains(xml, "<NumericField x:Name=\"age_numeric_field\" Value=\"{Binding Source=customers_binding_source, Path=age, Mode=TwoWay}\" Minimum=\"-1000000000\" Maximum=\"1000000000\"");
            StringAssert.Contains(xml, "<DatePicker x:Name=\"birth_date_date_picker\" Date=\"{Binding Source=customers_binding_source, Path=birth_date, Mode=TwoWay}\"");
            Assert.IsFalse(xml.Contains("email"), xml);
            StringAssert.Contains(xml, "<TextField x:Name=\"name_text_field\"");
        }

        [TestMethod]
        public void NamesAreUniqueAndAnotherConnectionsAdapterIsNotReused()
        {
            string view =
                "<Panel DesignWidth=\"800\" DesignHeight=\"450\">\n" +
                "  <DbConnection x:Name=\"otherConnection\" Provider=\"Sqlite\" ConnectionStringName=\"Other\"/>\n" +
                "  <TableAdapter x:Name=\"customers_table_adapter\" Connection=\"otherConnection\" SelectCommand=\"SELECT * FROM customers\" UpdateTable=\"customers\"/>\n" +
                "  <Label x:Name=\"customers_data_table\" Text=\"taken\" X=\"0\" Y=\"0\" Width=\"10\" Height=\"10\"/>\n" +
                "</Panel>\n";
            var plan = Plan(view, Grid("customers"));
            CollectionAssert.AreEqual(
                new[] { "shop_connection", "customers_table_adapter1", "customers_binding_source", "customers_error_provider", "customers_binding_navigator", "customers_data_table1" },
                plan.CreatedNames.ToArray());
            Assert.AreEqual((string.Empty, 2), (plan.Insertions[0].ParentId, plan.Insertions[0].Index), "after the view's last data component");
            StringAssert.Contains(plan.Insertions[0].Xml, "DataSource=\"customers_table_adapter1\"");
        }

        [TestMethod]
        public void AnExistingConnectionForTheSameNameIsReused()
        {
            string view =
                "<Panel DesignWidth=\"800\" DesignHeight=\"450\">\n" +
                "  <DbConnection x:Name=\"db\" Provider=\"Sqlite\" ConnectionStringName=\"shop\"/>\n" +
                "</Panel>\n";
            var plan = Plan(view, Grid("orders"));
            Assert.IsFalse(plan.Insertions.Any(i => i.Xml.Contains("<DbConnection")));
            StringAssert.Contains(plan.Insertions[0].Xml, "<TableAdapter x:Name=\"orders_table_adapter\" Connection=\"db\"");
            // The root has one child (the connection): components and controls go in one insertion after it.
            Assert.AreEqual(1, plan.Insertions.Count);
            Assert.AreEqual(1, plan.Insertions[0].Index);
            Assert.AreEqual("5", plan.SelectElementId, "adapter, binding source, error provider, navigator, then the grid");
        }

        [TestMethod]
        public void AViewIsReadOnly()
        {
            var plan = Plan(DataSourceFixtures.TemplateView, Grid("v_orders"));
            string components = plan.Insertions[0].Xml;
            StringAssert.Contains(components, "<TableAdapter x:Name=\"v_orders_table_adapter\" Connection=\"shop_connection\" SelectCommand=\"SELECT id, name, item, amount FROM v_orders\"/>");
            string visuals = plan.Insertions[1].Xml;
            StringAssert.Contains(visuals, "ShowAddItem=\"false\" ShowDeleteItem=\"false\" ShowSaveItem=\"false\"");
            Assert.AreEqual(4, visuals.Split('\n').Count(l => l.Contains("<Column") && l.Contains("ReadOnly=\"true\"")));
        }

        [TestMethod]
        public void ASingleColumnBecomesALabelAndItsControlBoundToTheTablesSource()
        {
            var shop = DataSourceFixtures.Shop();
            var plan = Plan(DataSourceFixtures.TemplateView, new DataDropRequest(shop, shop.Table("orders")!, DataTableDropMode.Grid, null, column: "ordered"), x: 30, y: 300);
            string visuals = plan.Insertions[1].Xml;
            Assert.IsFalse(visuals.Contains("BindingNavigator"), "a column alone gets no navigator");
            Assert.AreEqual(
                "<Label x:Name=\"ordered_label\" Text=\"Ordered\" X=\"30\" Y=\"304\" Width=\"112\" Height=\"26\"/>\n" +
                "<TextField x:Name=\"ordered_text_field\" Text=\"{Binding Source=orders_binding_source, Path=ordered, FormatString=g, NullValue='', Mode=TwoWay}\" X=\"150\" Y=\"300\" Width=\"200\" Height=\"34\"/>",
                visuals);
            StringAssert.Contains(plan.Insertions[0].Xml, "<BindingSource x:Name=\"orders_binding_source\"");
            Assert.AreEqual("7", plan.SelectElementId, "4 components, the 2 template controls, the label, then the field");
        }

        [TestMethod]
        public void ADropIntoANestedContainerShiftsItsIdPastTheNewComponents()
        {
            string view =
                "<Panel DesignWidth=\"800\" DesignHeight=\"450\">\n" +
                "  <Label x:Name=\"title\" Text=\"Customers\" X=\"0\" Y=\"0\" Width=\"10\" Height=\"10\"/>\n" +
                "  <GroupBox x:Name=\"box\" X=\"10\" Y=\"40\" Width=\"600\" Height=\"400\">\n" +
                "    <Label x:Name=\"inner\" Text=\"x\" X=\"0\" Y=\"0\" Width=\"10\" Height=\"10\"/>\n" +
                "  </GroupBox>\n" +
                "</Panel>\n";
            var plan = Plan(view, Grid("orders"), parentId: "1", index: 1, x: 8, y: 30);
            Assert.AreEqual(("1", 1), (plan.Insertions[1].ParentId, plan.Insertions[1].Index));
            Assert.AreEqual("5.2", plan.SelectElementId, "the group box moves from 1 to 5; navigator at 1, grid at 2");
            var after = KbviewOutline.TryParse(Apply(view, plan))!;
            Assert.AreEqual("DataTable", after.Find("5.2")!.Name);
        }

        [TestMethod]
        public void AKubunoModuleSchemaAndQuotedIdentifiers()
        {
            var column = new KbdataColumnInfo("Order Date", "timestamptz", "chrono::DateTime<chrono::Utc>", true, false, false, 0);
            var key = new KbdataColumnInfo("id", "int4", "i32", false, false, false, 0);
            var select = new KbdataColumnInfo("select", "text", "String", false, false, false, 0);
            var table = new KbdataTableInfo("order", false, "Order", new[] { "id" }, new[] { key, column, select });
            var source = new KbdataSourceInfo(@"C:\m\src\data\calendar.kbdata", "calendar", "Calendar", "postgres", "calendar", new[] { table });
            var plan = Plan(DataSourceFixtures.TemplateView, new DataDropRequest(source, table, DataTableDropMode.Grid, null));
            string xml = plan.Insertions[0].Xml;
            StringAssert.Contains(xml, "<DbConnection x:Name=\"calendar_connection\" Provider=\"Postgres\" ConnectionStringName=\"Calendar\" Schema=\"calendar\"/>");
            StringAssert.Contains(xml, "SelectCommand=\"SELECT id, &quot;Order Date&quot;, &quot;select&quot; FROM &quot;order&quot;\" UpdateTable=\"order\" PrimaryKey=\"id\" AutoIncrementKey=\"false\"");
            Assert.AreEqual("[Order Date]", DataSourceDropPlanner.QuoteIdentifier("sqlserver", "Order Date"));
            Assert.AreEqual("`order`", DataSourceDropPlanner.QuoteIdentifier("mysql", "order"));
            Assert.AreEqual("customers", DataSourceDropPlanner.QuoteIdentifier("postgres", "customers"));
        }

        [TestMethod]
        public void AGridStaysWithinItsContainer()
        {
            string narrow = "<Panel DesignWidth=\"500\" DesignHeight=\"450\">\n</Panel>\n";
            StringAssert.Contains(Plan(narrow, Grid("customers"), x: 40, y: 20).Insertions[0].Xml, "X=\"40\" Y=\"62\" Width=\"444\" Height=\"220\"");
            string wide = "<Panel DesignWidth=\"1200\" DesignHeight=\"450\">\n</Panel>\n";
            StringAssert.Contains(Plan(wide, Grid("customers"), x: 40, y: 20).Insertions[0].Xml, "Width=\"770\" Height=\"220\"", "the columns' own width when it fits");
        }

        [TestMethod]
        public void AMalformedViewIsRefused()
        {
            DataSourcesText.ForceFrench = false;
            var plan = DataSourceDropPlanner.Plan("<Panel><Label></Panel>", Grid("customers"), string.Empty, null, 0, 0, out var error);
            Assert.IsNull(plan);
            StringAssert.Contains(error, "well-formed");
        }

        [TestMethod]
        public void WithoutAPointTheDropGoesBelowTheContent()
        {
            var plan = Plan(DataSourceFixtures.TemplateView, Grid("orders"), x: null, y: null);
            StringAssert.Contains(plan.Insertions[1].Xml, "<BindingNavigator x:Name=\"orders_binding_navigator\" BindingSource=\"orders_binding_source\" X=\"16\" Y=\"76\"");
        }

        [TestMethod]
        public void TheOutlineReadsIdsNamesAndEntities()
        {
            var outline = KbviewOutline.TryParse("<?xml version=\"1.0\"?>\n<!-- c <X/> -->\n<Stack x:Name='root'><A Text=\"a &amp; &quot;b&quot;\"/><B><![CDATA[<C/>]]><C x:Name=\"c\"/></B></Stack>")!;
            Assert.AreEqual("root", outline.Root.XName);
            Assert.AreEqual("a & \"b\"", outline.Find("0")!.Attribute("Text"));
            Assert.AreEqual("c", outline.Find("1.0")!.XName);
            Assert.IsNull(outline.Find("2"));
            CollectionAssert.AreEquivalent(new[] { "root", "c" }, outline.Names().ToArray());
            Assert.IsNull(KbviewOutline.TryParse("<A><B></A>"));
            Assert.IsNull(KbviewOutline.TryParse("<A/><B/>"));
        }

        [TestMethod]
        public void ADropOnExistingControlsMovesBelowThem()
        {
            // Dropped at (24, 30), on the template's Status field / Say hello row (y 24..60): the navigator starts below it.
            var plan = Plan(DataSourceFixtures.TemplateView, Grid("customers"), x: 24, y: 30);
            string xml = plan.Insertions.Last().Xml;
            StringAssert.Contains(xml, "<BindingNavigator x:Name=\"customers_binding_navigator\" BindingSource=\"customers_binding_source\" X=\"24\" Y=\"68\"");
            var outline = KbviewOutline.TryParse(Apply(DataSourceFixtures.TemplateView, plan))!;
            var status = outline.All().First(e => e.XName == "status");
            foreach (var added in outline.All().Where(e => e.XName is "customers_binding_navigator" or "customers_data_table"))
            {
                Assert.IsTrue(added.Number("Y") >= status.Number("Y") + status.Number("Height"), added.XName + " below the existing row");
            }
        }

        [TestMethod]
        public void AColumnJoinsTheDetailRowsOfItsList()
        {
            var first = Apply(DataSourceFixtures.TemplateView, Plan(DataSourceFixtures.TemplateView, Details("orders"), x: 24, y: 80));
            var outline = KbviewOutline.TryParse(first)!;
            var fields = outline.All().Where(e => e.Name != "Label" && e.Attributes.Values.Any(v => v.Contains("Source=orders_binding_source, Path="))).ToList();
            double fieldX = fields.Min(e => e.Number("X")!.Value);
            double lastY = fields.Max(e => e.Number("Y")!.Value);

            // Dropped far away: the column still lines up with the rows of `orders`, as their next row.
            var shop = DataSourceFixtures.Shop();
            var plan = Plan(first, new DataDropRequest(shop, shop.Table("orders")!, DataTableDropMode.Details, null, column: "item"), x: 600, y: 20);
            var after = KbviewOutline.TryParse(Apply(first, plan))!;
            var added = after.All().Where(e => e.XName is not null && e.XName.StartsWith("item_", StringComparison.Ordinal) && e.XName.EndsWith("1", StringComparison.Ordinal)).ToList();
            var label = added.Single(e => e.Name == "Label");
            var field = added.Single(e => e.Name != "Label");
            Assert.AreEqual(fieldX - DataSourceDropPlanner.LabelWidth, label.Number("X"));
            Assert.AreEqual(fieldX, field.Number("X"));
            Assert.AreEqual(lastY + DataSourceDropPlanner.RowHeight, field.Number("Y"));
        }
    }
}
