using System.Collections.Generic;
using System.Linq;
using Kubuno.Views.Designer.PropertyBrowser;
using Kubuno.Views.Designer.Registry;
using Kubuno.Views.Designer.Ribbon;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Views.Tests.Designer.Ribbon
{
    /// <summary>The ribbon's designer tasks (docs/RIBBON.md section 9): "Add" choices, polymorphic collections, "Create Command".</summary>
    [TestClass]
    public class RibbonDesignerTasksTests
    {
        private static string Entry(string name, string family, params string[] children) =>
            "{\"name\":\"" + name + "\",\"family\":\"" + family + "\",\"children\":\"" + (children.Length == 0 ? "None" : "List") +
            "\",\"allowed_children\":[" + string.Join(",", children.Select(c => "\"" + c + "\"")) + "],\"properties\":[],\"events\":[]}";

        private static readonly ComponentRegistry Registry = ComponentRegistry.FromJson("[" + string.Join(",", new[]
        {
            Entry("Panel", "core", "Ribbon", "Command"),
            Entry("Command", "ribbon"),
            Entry("Ribbon", "ribbon", "RibbonTab", "RibbonContextualTabGroup", "RibbonQuickAccessToolbar", "RibbonBackstage"),
            Entry("RibbonTab", "ribbon", "RibbonGroup"),
            Entry("RibbonContextualTabGroup", "ribbon", "RibbonTab"),
            Entry("RibbonQuickAccessToolbar", "ribbon", "RibbonButton"),
            Entry("RibbonBackstage", "ribbon", "BackstageTab"),
            Entry("BackstageTab", "ribbon"),
            Entry("RibbonGroup", "ribbon", "RibbonButton", "RibbonToggleButton", "RibbonGallery"),
            Entry("RibbonButton", "ribbon"),
            Entry("RibbonToggleButton", "ribbon"),
            Entry("RibbonGallery", "ribbon", "RibbonGalleryItem"),
            Entry("RibbonGalleryItem", "ribbon"),
            Entry("RibbonComboBox", "ribbon", "Option"),
            Entry("Option", "core"),
        }) + "]");

        private const string View =
            "<Panel>\n" +
            "  <Ribbon>\n" +
            "    <RibbonQuickAccessToolbar/>\n" +
            "    <RibbonTab Header=\"Accueil\">\n" +
            "      <RibbonGroup Header=\"Presse-papiers\">\n" +
            "        <RibbonButton x:Name=\"paste\" Label=\"Coller\" LargeIcon=\"ClipboardPaste\" Size=\"Large\"/>\n" +
            "        <RibbonToggleButton Label=\"Gras épais\" SmallIcon=\"Bold\" Checked=\"{Binding Bold}\"/>\n" +
            "        <RibbonButton Label=\"Copier\" Command=\"cmd_copy\"/>\n" +
            "      </RibbonGroup>\n" +
            "    </RibbonTab>\n" +
            "  </Ribbon>\n" +
            "</Panel>";

        [TestMethod]
        public void AddChoices_OfferWhatTheElementTakes_AndASingletonOnlyOnce()
        {
            CollectionAssert.AreEqual(new[] { "RibbonTab", "RibbonContextualTabGroup", "RibbonBackstage" }, RibbonDesignerTasks.AddChoices(View, "0", Registry).ToList());
            CollectionAssert.AreEqual(new[] { "RibbonGroup" }, RibbonDesignerTasks.AddChoices(View, "0.1", Registry).ToList());
            CollectionAssert.AreEqual(new[] { "RibbonButton", "RibbonToggleButton", "RibbonGallery" }, RibbonDesignerTasks.AddChoices(View, "0.1.0", Registry).ToList());
            Assert.AreEqual(0, RibbonDesignerTasks.AddChoices(View, "0.1.0.0", Registry).Count);
            Assert.AreEqual(0, RibbonDesignerTasks.AddChoices(View, "", Registry).Count, "not a ribbon element");
        }

        [TestMethod]
        public void Collections_AreOnePolymorphicRowPerElement()
        {
            var group = RibbonDesignerTasks.CollectionOf(Registry.Find("RibbonGroup")!)!;
            Assert.AreEqual("Items", group.Row);
            CollectionAssert.AreEqual(new[] { "RibbonButton", "RibbonToggleButton", "RibbonGallery" }, group.Tags.ToList());
            var ribbon = RibbonDesignerTasks.CollectionOf(Registry.Find("Ribbon")!)!;
            Assert.AreEqual("Tabs", ribbon.Row);
            CollectionAssert.AreEqual(new[] { "RibbonTab", "RibbonContextualTabGroup" }, ribbon.Tags.ToList());
            Assert.AreEqual("Groups", RibbonDesignerTasks.CollectionOf(Registry.Find("RibbonTab")!)!.Row);
            Assert.IsNull(RibbonDesignerTasks.CollectionOf(Registry.Find("RibbonComboBox")!), "options stay a string list");
            Assert.IsNull(RibbonDesignerTasks.CollectionOf(Registry.Find("RibbonButton")!));
        }

        [TestMethod]
        public void PolymorphicPlan_KeepsOrder_AndWritesEachNewMemberWithItsTag()
        {
            var tags = new[] { "RibbonButton", "RibbonToggleButton", "RibbonGallery" };
            var members = new[]
            {
                new CollectionMember(1),
                new CollectionMember(0),
                new CollectionMember(null, new[] { new KeyValuePair<string, string?>("Label", "Styles") }, "RibbonGallery"),
            };
            var result = ChildCollectionPlanner.Apply(View, ChildCollectionPlanner.Plan(View, "0.1.0", tags, members));
            var toggle = result.IndexOf("<RibbonToggleButton", System.StringComparison.Ordinal);
            var paste = result.IndexOf("x:Name=\"paste\"", System.StringComparison.Ordinal);
            var gallery = result.IndexOf("<RibbonGallery Label=\"Styles\"/>", System.StringComparison.Ordinal);
            Assert.IsTrue(toggle >= 0 && toggle < paste && paste < gallery, result);
            Assert.IsFalse(result.Contains("Label=\"Copier\""), "a member left out is removed: " + result);
        }

        [TestMethod]
        public void ScalingPolicy_IsAddedRewrittenAndRemoved_AsAPropertyElement()
        {
            const string Tab =
                "<Ribbon>\n" +
                "  <RibbonTab Header=\"Accueil\">\n" +
                "    <RibbonGroup x:Name=\"clip\"/>\n" +
                "    <RibbonGroup x:Name=\"font\"/>\n" +
                "  </RibbonTab>\n" +
                "</Ribbon>";
            var tab = Kubuno.Views.Designer.Selection.ViewDocument.Find(Kubuno.Views.Designer.Selection.ViewDocument.Parse(Tab), "0")!;
            CollectionAssert.AreEqual(new[] { "clip", "font" }, RibbonScalingPolicy.GroupNames(tab).ToList());

            var added = ChildCollectionPlanner.Apply(Tab, RibbonScalingPolicy.Plan(Tab, "0", new[] { ("font", "Medium"), ("clip", "Collapsed") }));
            StringAssert.Contains(added,
                "  <RibbonTab Header=\"Accueil\">\n    <RibbonTab.ScalingPolicy>\n      <Scale Group=\"font\" Size=\"Medium\"/>\n      <Scale Group=\"clip\" Size=\"Collapsed\"/>\n    </RibbonTab.ScalingPolicy>\n    <RibbonGroup x:Name=\"clip\"/>");

            var withPolicy = Kubuno.Views.Designer.Selection.ViewDocument.Find(Kubuno.Views.Designer.Selection.ViewDocument.Parse(added), "0")!;
            var originals = RibbonScalingPolicy.Steps(withPolicy);
            Assert.AreEqual(2, originals.Count);
            var merged = RibbonScalingPolicy.Merge(originals, new[] { new CollectionMember(1), new CollectionMember(0, new[] { new KeyValuePair<string, string?>("Size", "Small") }) });
            CollectionAssert.AreEqual(new[] { ("clip", "Collapsed"), ("font", "Small") }, merged.ToList());
            var rewritten = ChildCollectionPlanner.Apply(added, RibbonScalingPolicy.Plan(added, "0", merged));
            StringAssert.Contains(rewritten, "<Scale Group=\"clip\" Size=\"Collapsed\"/>\n      <Scale Group=\"font\" Size=\"Small\"/>");

            Assert.AreEqual(Tab, ChildCollectionPlanner.Apply(added, RibbonScalingPolicy.Plan(added, "0", new (string, string)[0])));
            Assert.AreEqual(1, RibbonScalingPolicy.Diagnostics(new[] { ("font", "Small"), ("font", "Medium") }, new[] { "font" }).Count, "grows back");
            Assert.AreEqual(1, RibbonScalingPolicy.Diagnostics(new[] { ("nope", "Small") }, new[] { "font" }).Count, "unknown group");
        }

        [TestMethod]
        public void CreateCommand_MovesLabelIconsAndCheckState_IntoANewCommand()
        {
            Assert.IsTrue(RibbonDesignerTasks.CanCreateCommand(View, "0.1.0.0"));
            Assert.IsFalse(RibbonDesignerTasks.CanCreateCommand(View, "0.1.0.2"), "already has a command");
            Assert.IsFalse(RibbonDesignerTasks.CanCreateCommand(View, "0.1.0"), "a group");

            var paste = ChildCollectionPlanner.Apply(View, RibbonDesignerTasks.PlanCreateCommand(View, "0.1.0.0", out var name));
            Assert.AreEqual("cmd_paste", name);
            StringAssert.Contains(paste, "<Panel>\n  <Command x:Name=\"cmd_paste\" Label=\"Coller\" LargeIcon=\"ClipboardPaste\"/>\n  <Ribbon>");
            StringAssert.Contains(paste, "<RibbonButton x:Name=\"paste\" Size=\"Large\" Command=\"cmd_paste\"/>");

            var bold = ChildCollectionPlanner.Apply(View, RibbonDesignerTasks.PlanCreateCommand(View, "0.1.0.1", out var boldName));
            Assert.AreEqual("cmd_gras_epais", boldName);
            StringAssert.Contains(bold, "<Command x:Name=\"cmd_gras_epais\" Label=\"Gras épais\" SmallIcon=\"Bold\" IsCheckable=\"true\" Checked=\"{Binding Bold}\"/>");
            StringAssert.Contains(bold, "<RibbonToggleButton Command=\"cmd_gras_epais\"/>");
        }
    }
}
