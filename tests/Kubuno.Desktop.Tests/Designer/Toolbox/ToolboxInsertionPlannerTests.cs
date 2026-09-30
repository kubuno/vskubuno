using System.Linq;
using Kubuno.VisualStudio.Designer.Registry;
using Kubuno.VisualStudio.Designer.Toolbox;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.VisualStudio.Designer.Tests.Toolbox
{
    [TestClass]
    public class ToolboxInsertionPlannerTests
    {
        private const string View = "<Card Title=\"App\">\n  <Stack Gap=\"16\">\n    <TextField x:Name=\"status\" />\n    <Button x:Name=\"hello\"/>\n  </Stack>\n</Card>";

        private static readonly ComponentRegistry Registry = ComponentRegistry.FromJson(TestFixtures.ReadAllText("registry.sample.json"));

        [TestMethod]
        public void SelectedFlowContainer_ReceivesTheComponentAtTheEnd()
        {
            var plan = ToolboxInsertionPlanner.Plan(View, "0", "Button", Registry)!;

            Assert.AreEqual("0", plan.ParentId);
            Assert.AreEqual(2, plan.Index);
            Assert.AreEqual("<Button/>", plan.Xml);
            Assert.AreEqual("0.2", plan.NewElementId);
        }

        [TestMethod]
        public void SelectedLeaf_InsertsIntoItsContainer()
        {
            var plan = ToolboxInsertionPlanner.Plan(View, "0.1", "Switch", Registry)!;
            Assert.AreEqual("0", plan.ParentId);
            Assert.AreEqual("<Switch/>", plan.Xml);
        }

        [TestMethod]
        public void FullSingleWidgetContainer_IsSkipped_AndNothingElseAccepts()
        {
            // The root Card (SingleWidget) already holds its Stack: with the root selected, nothing accepts.
            Assert.IsNull(ToolboxInsertionPlanner.Plan(View, string.Empty, "Button", Registry));
            Assert.IsNull(ToolboxInsertionPlanner.Plan(View, null, "Button", Registry));
        }

        [TestMethod]
        public void EmptySingleWidgetContainer_Accepts()
        {
            var plan = ToolboxInsertionPlanner.Plan("<Card/>", null, "Stack", Registry)!;
            Assert.AreEqual(string.Empty, plan.ParentId);
            Assert.AreEqual(0, plan.Index);
            Assert.AreEqual("0", plan.NewElementId);
        }

        [TestMethod]
        public void AnchorContainer_GetsPositionAndSize()
        {
            var plan = ToolboxInsertionPlanner.Plan("<Panel><Button/></Panel>", string.Empty, "Button", Registry)!;
            Assert.AreEqual("<Button X=\"32\" Y=\"32\" Width=\"100\" Height=\"36\" Anchor=\"Top, Left\"/>", plan.Xml);
        }

        [TestMethod]
        public void AnchorContainer_UsesTheComponentDefaultSize()
        {
            Assert.AreEqual("<TextField X=\"8\" Y=\"8\" Width=\"200\" Height=\"36\" Anchor=\"Top, Left\"/>", ToolboxInsertionPlanner.Plan("<Panel/>", null, "TextField", Registry)!.Xml);
            Assert.AreEqual((200, 100), ToolboxInsertionPlanner.DefaultSize("Stack", Registry), "a container gets room for children");
            Assert.AreEqual((120, 36), ToolboxInsertionPlanner.DefaultSize("Unknown", Registry));
        }

        [TestMethod]
        public void GatedChild_OnlyGoesIntoItsOwnContainer()
        {
            var gated = Registry.Components.First(c => c.AllowedChildren.Count > 0);
            var child = gated.AllowedChildren[0];

            Assert.IsNull(ToolboxInsertionPlanner.Plan(View, "0", child, Registry), $"<{child}> is only allowed in <{gated.Name}>");
            Assert.IsNotNull(ToolboxInsertionPlanner.Plan($"<{gated.Name}/>", null, child, Registry));
        }

        [TestMethod]
        public void UnknownComponent_IsRefused()
        {
            Assert.IsNull(ToolboxInsertionPlanner.Plan(View, "0", "NoSuchControl", Registry));
        }

        [TestMethod]
        public void ItemFormat_RoundTrips_AndRejectsGarbage()
        {
            var bytes = ToolboxItemFormat.Encode("TextField");
            Assert.IsTrue(ToolboxItemFormat.TryDecode(bytes.Concat(new byte[] { 0, 0, 0 }).ToArray(), out var name));
            Assert.AreEqual("TextField", name);

            Assert.IsFalse(ToolboxItemFormat.TryDecode(null, out _));
            Assert.IsFalse(ToolboxItemFormat.TryDecode(System.Text.Encoding.UTF8.GetBytes("<Button/>"), out _));
        }

        [TestMethod]
        public void Layout_GroupsComponentsInLocalizedTabs_InRegistryOrder()
        {
            DesignerText.ForceFrench = true;
            try
            {
                var layout = NativeToolboxInstaller.Layout(Registry).ToList();
                Assert.AreEqual(Registry.Components.Count, layout.Count);
                Assert.AreEqual(("Contrôles communs", "Button"), layout.First(l => l.Component == "Button"));
                CollectionAssert.IsSubsetOf(new[] { "Affichage", "Choix", "Texte", "Conteneurs", "Données" }, layout.Select(l => l.Tab).Distinct().ToList());
            }
            finally
            {
                DesignerText.ForceFrench = null;
            }
        }
    }
}
