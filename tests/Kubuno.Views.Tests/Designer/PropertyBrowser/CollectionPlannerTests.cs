using System.Collections.Generic;
using System.Linq;
using Kubuno.Desktop.Designer.PropertyBrowser;
using Kubuno.Desktop.Designer.Selection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Desktop.Tests.Designer.PropertyBrowser
{
    /// <summary>The text edits of the collection and string-list editors, checked on the resulting text.</summary>
    [TestClass]
    public class CollectionPlannerTests
    {
        private const string Tabs =
            "<Panel>\n" +
            "  <Tabs x:Name=\"tabs\">\n" +
            "    <TabItem Header=\"One\">\n" +
            "      <Button Text=\"inside\"/>\n" +
            "    </TabItem>\n" +
            "    <!-- the second page -->\n" +
            "    <TabItem Header=\"Two\"/>\n" +
            "    <TabItem Header=\"Three\"/>\n" +
            "  </Tabs>\n" +
            "</Panel>";

        private static CollectionMember Keep(int index, params (string Name, string? Value)[] changes) =>
            new CollectionMember(index, changes.Select(c => new KeyValuePair<string, string?>(c.Name, c.Value)));

        private static CollectionMember New(params (string Name, string? Value)[] attributes) =>
            new CollectionMember(null, attributes.Select(c => new KeyValuePair<string, string?>(c.Name, c.Value)));

        private static string Run(string text, string parent, string tag, params CollectionMember[] members) =>
            ChildCollectionPlanner.Apply(text, ChildCollectionPlanner.Plan(text, parent, tag, members));

        [TestMethod]
        public void Unchanged_IsNoEdit()
        {
            Assert.AreEqual(0, ChildCollectionPlanner.Plan(Tabs, "0", "TabItem", new[] { Keep(0), Keep(1), Keep(2) }).Count);
        }

        [TestMethod]
        public void InPlace_RemovesEditsAndAppends_KeepingContentAndComments()
        {
            var result = Run(Tabs, "0", "TabItem", Keep(0, ("Header", "First")), Keep(2), New(("Header", "Four \"4\"")));
            Assert.AreEqual(
                "<Panel>\n" +
                "  <Tabs x:Name=\"tabs\">\n" +
                "    <TabItem Header=\"First\">\n" +
                "      <Button Text=\"inside\"/>\n" +
                "    </TabItem>\n" +
                "    <!-- the second page -->\n" +
                "    <TabItem Header=\"Three\"/>\n" +
                "    <TabItem Header=\"Four &quot;4&quot;\"/>\n" +
                "  </Tabs>\n" +
                "</Panel>",
                result);
        }

        [TestMethod]
        public void AttributesAreAddedAndRemoved_InTheStartTag()
        {
            var text = "<ListView>\n  <Column Header=\"Name\" Width=\"80\" />\n</ListView>";
            Assert.AreEqual(
                "<ListView>\n  <Column Header=\"Name\" Binding=\"{Binding Name}\" />\n</ListView>",
                Run(text, string.Empty, "Column", Keep(0, ("Width", null), ("Binding", "{Binding Name}"))));
        }

        [TestMethod]
        public void Reordering_RewritesTheBlock_WithEveryChildsOwnText()
        {
            var result = Run(Tabs, "0", "TabItem", Keep(2), Keep(0), New(("Header", "New")));
            Assert.AreEqual(
                "<Panel>\n" +
                "  <Tabs x:Name=\"tabs\">\n" +
                "    <TabItem Header=\"Three\"/>\n" +
                "    <TabItem Header=\"One\">\n" +
                "      <Button Text=\"inside\"/>\n" +
                "    </TabItem>\n" +
                "    <TabItem Header=\"New\"/>\n" +
                "  </Tabs>\n" +
                "</Panel>",
                result);
        }

        [TestMethod]
        public void OtherChildren_StayWhereTheyAre()
        {
            var text = "<ListView>\n  <Column Header=\"A\"/>\n  <Item Text=\"row\"/>\n  <Column Header=\"B\"/>\n</ListView>";
            Assert.AreEqual(
                "<ListView>\n  <Column Header=\"A\"/>\n  <Item Text=\"row\"/>\n  <Column Header=\"B\"/>\n  <Column Header=\"C\"/>\n</ListView>",
                Run(text, string.Empty, "Column", Keep(0), Keep(1), New(("Header", "C"))));
            Assert.AreEqual(
                "<ListView>\n  <Column Header=\"B\"/>\n  <Column Header=\"A\"/>\n  <Item Text=\"row\"/>\n</ListView>",
                Run(text, string.Empty, "Column", Keep(1), Keep(0)));
        }

        [TestMethod]
        public void FirstChildren_GoIntoAnEmptyOrSelfClosingParent()
        {
            Assert.AreEqual(
                "<Panel>\n  <Tabs>\n    <TabItem Header=\"A\"/>\n  </Tabs>\n</Panel>",
                Run("<Panel>\n  <Tabs/>\n</Panel>", "0", "TabItem", New(("Header", "A"))));
            Assert.AreEqual(
                "<Tabs>\n  <TabItem Header=\"A\"/>\n  <TabItem Header=\"B\"/>\n</Tabs>",
                Run("<Tabs></Tabs>", string.Empty, "TabItem", New(("Header", "A")), New(("Header", "B"))));
            Assert.AreEqual(
                "<Tabs>\r\n  <TabItem Header=\"A\"/>\r\n</Tabs>",
                Run("<Tabs>\r\n</Tabs>", string.Empty, "TabItem", New(("Header", "A"))));
        }

        [TestMethod]
        public void RemovingEverything_LeavesTheParent()
        {
            Assert.AreEqual("<Panel>\n  <Tabs x:Name=\"tabs\">\n  </Tabs>\n</Panel>", Run(Tabs, "0", "TabItem").Replace("    <!-- the second page -->\n", string.Empty));
        }

        [TestMethod]
        public void AStaleParent_PlansNothing()
        {
            Assert.AreEqual(0, ChildCollectionPlanner.Plan(Tabs, "7", "TabItem", new[] { New(("Header", "x")) }).Count);
        }

        [TestMethod]
        public void StringList_KeepsMatchingLines_AndAddsTheOthers()
        {
            var combo = RichRegistry.Registry.Find("ComboBox");
            var option = RichRegistry.Registry.Find("Option");
            var text = "<ComboBox>\n  <Option Value=\"fr\" Label=\"Français\"/>\n  <Option Value=\"en\" Label=\"English\"/>\n</ComboBox>";
            Assert.AreEqual("Label", StringListPlanner.LabelAttribute(option));
            Assert.AreEqual("Value", StringListPlanner.ValueAttribute(option));
            Assert.AreEqual("Text", StringListPlanner.LabelAttribute(RichRegistry.Registry.Find("Item")));
            Assert.IsNull(StringListPlanner.ValueAttribute(RichRegistry.Registry.Find("Item")));
            CollectionAssert.AreEqual(new[] { "Français", "English" }, StringListPlanner.Lines(text, string.Empty, "Option", "Label", "Value").ToArray());

            var edits = StringListPlanner.Plan(text, string.Empty, "Option", option, new[] { "English", "", "Deutsch" });
            Assert.AreEqual(
                "<ComboBox>\n  <Option Value=\"en\" Label=\"English\"/>\n  <Option Value=\"Deutsch\" Label=\"Deutsch\"/>\n</ComboBox>",
                ChildCollectionPlanner.Apply(text, edits));
            Assert.IsNotNull(combo);
            Assert.AreEqual(0, StringListPlanner.Plan(text, string.Empty, "Option", option, new[] { "Français", "English" }).Count);
        }

        [TestMethod]
        public void TheDocumentReader_KnowsWhereEverythingIs()
        {
            var root = ViewDocument.Parse(Tabs)!;
            var tabs = ViewDocument.Find(root, "0")!;
            Assert.AreEqual("Tabs", tabs.Name);
            Assert.AreEqual(3, tabs.Children.Count, "the comment is not a child");
            Assert.AreEqual("0.2", tabs.Children[2].Id);
            Assert.AreEqual("<TabItem Header=\"Two\"/>", Tabs.Substring(tabs.Children[1].Start, tabs.Children[1].End - tabs.Children[1].Start));
            var header = tabs.Children[0].Attributes.Single();
            Assert.AreEqual("One", Tabs.Substring(header.ValueStart, header.ValueEnd - header.ValueStart));
            Assert.AreEqual("  </Tabs>", Tabs.Substring(tabs.CloseTagStart - 2, 9));
            Assert.IsNull(ViewDocument.Parse("no element"));
        }
    }
}
