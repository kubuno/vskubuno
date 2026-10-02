using Kubuno.Desktop.Designer.Editing;
using Kubuno.Desktop.Designer.Registry;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Desktop.Tests.Designer.Editing
{
    /// <summary>docs/DESIGNER.md §12: the registry rules behind paste, duplicate, wrap, unwrap and z-order.</summary>
    [TestClass]
    public class DesignerStructurePlannerTests
    {
        private const string View = "<Card Title=\"App\">\n  <Stack>\n    <TextField x:Name=\"status\"/>\n    <Button x:Name=\"hello\"/>\n  </Stack>\n</Card>";
        private const string TabsView = "<Stack>\n  <Tabs>\n    <TabItem Header=\"A\"><Button/></TabItem>\n  </Tabs>\n</Stack>";

        private static readonly ComponentRegistry Registry = ComponentRegistry.FromJson(TestFixtures.ReadAllText("registry.sample.json"));

        [TestMethod]
        public void Paste_IntoAContainerAppends()
        {
            var plan = DesignerStructurePlanner.PlanPaste(View, "0", "Button", Registry)!;
            Assert.AreEqual("0", plan.ParentId);
            Assert.AreEqual(2, plan.Index);
            Assert.AreEqual("0.2", plan.NewElementId);
        }

        [TestMethod]
        public void Paste_OnALeafGoesRightAfterIt()
        {
            var plan = DesignerStructurePlanner.PlanPaste(View, "0.0", "Button", Registry)!;
            Assert.AreEqual("0", plan.ParentId);
            Assert.AreEqual(1, plan.Index);
        }

        [TestMethod]
        public void Paste_IsRefusedWhereTheRegistryForbidsIt()
        {
            Assert.IsNull(DesignerStructurePlanner.PlanPaste(View, "0", "TabItem", Registry), "a TabItem only goes in Tabs");
            Assert.IsNull(DesignerStructurePlanner.PlanPaste(View, null, "Button", Registry), "the root Card already has its one child");
            Assert.IsNull(DesignerStructurePlanner.PlanPaste(View, "0", "NoSuchThing", Registry));
        }

        [TestMethod]
        public void Duplicate_GoesAfterTheElementUnlessTheContainerIsFull()
        {
            Assert.AreEqual("0.2", DesignerStructurePlanner.PlanDuplicate(View, "0.1", Registry)!.NewElementId);
            Assert.IsNull(DesignerStructurePlanner.PlanDuplicate(View, "0", Registry), "the Card body is a single widget");
            Assert.IsNull(DesignerStructurePlanner.PlanDuplicate(View, "", Registry), "the root");
        }

        [TestMethod]
        public void Wrap_ChecksBothTheWrapperAndTheContainer()
        {
            Assert.IsTrue(DesignerStructurePlanner.CanWrap(View, "0.1", "Stack", Registry));
            Assert.IsTrue(DesignerStructurePlanner.CanWrap(View, "0", "ScrollArea", Registry), "replaces the Card's single child");
            Assert.IsTrue(DesignerStructurePlanner.CanWrap(View, "", "Stack", Registry), "the root can be wrapped");
            Assert.IsFalse(DesignerStructurePlanner.CanWrap(TabsView, "0.0", "Stack", Registry), "a TabItem only lives directly in Tabs");
        }

        [TestMethod]
        public void Unwrap_NeedsChildrenTheParentAccepts()
        {
            Assert.IsFalse(DesignerStructurePlanner.CanUnwrap(View, "0", Registry), "two children cannot replace the Card's single child");
            Assert.IsTrue(DesignerStructurePlanner.CanUnwrap(View, "", Registry), "a root with one child");
            Assert.IsFalse(DesignerStructurePlanner.CanUnwrap(View, "0.1", Registry), "no children");
            Assert.IsFalse(DesignerStructurePlanner.CanUnwrap(TabsView, "0", Registry), "TabItem children cannot go into a Stack");
        }

        [TestMethod]
        public void ZOrder_MovesToTheEnds()
        {
            Assert.AreEqual(1, DesignerStructurePlanner.BringToFrontIndex(View, "0.0"));
            Assert.IsNull(DesignerStructurePlanner.BringToFrontIndex(View, "0.1"));
            Assert.AreEqual(0, DesignerStructurePlanner.SendToBackIndex(View, "0.1"));
            Assert.IsNull(DesignerStructurePlanner.SendToBackIndex(View, "0.0"));
            Assert.IsNull(DesignerStructurePlanner.BringToFrontIndex(View, ""));
        }

        [TestMethod]
        public void Ancestors_AreNearestFirstWithNames()
        {
            var ancestors = DesignerStructurePlanner.Ancestors("<Stack x:Name=\"root\"><Card><Button/></Card></Stack>", "0.0");
            Assert.AreEqual(2, ancestors.Count);
            Assert.AreEqual(("0", "Card"), ancestors[0]);
            Assert.AreEqual(("", "root (Stack)"), ancestors[1]);
        }

        [TestMethod]
        public void Fragment_IsNormalizedToColumnZero()
        {
            var text = "<Stack>\n    <Card>\n      <Button/>\n    </Card>\n</Stack>";
            var start = text.IndexOf("<Card", System.StringComparison.Ordinal);
            var prefix = DesignerFragment.LinePrefix(text, start);
            Assert.AreEqual("    ", prefix);
            var element = text.Substring(start, text.IndexOf("</Card>", System.StringComparison.Ordinal) + 7 - start);
            Assert.AreEqual("<Card>\n  <Button/>\n</Card>", DesignerFragment.Normalize(element, prefix));
            Assert.IsNull(DesignerFragment.LinePrefix("<Stack><Card/>", 7));
        }

        [TestMethod]
        public void Fragment_RootTagSkipsCommentsAndRejectsText()
        {
            Assert.AreEqual("Button", DesignerFragment.RootTag("  <!-- x --> <Button Text=\"a\"/>"));
            Assert.AreEqual("x:Thing", DesignerFragment.RootTag("<x:Thing/>"));
            Assert.IsNull(DesignerFragment.RootTag("hello"));
            Assert.IsNull(DesignerFragment.RootTag(null));
        }

        [TestMethod]
        public void DesignSize_PrefersRealSizePerAxis()
        {
            var size = DesignSizeInfo.Read("<Card Width=\"400\" DesignHeight=\"300\"/>");
            Assert.AreEqual(400, size.Width);
            Assert.AreEqual("Width", size.WidthAttribute);
            Assert.AreEqual(300, size.Height);
            Assert.AreEqual("DesignHeight", size.HeightAttribute);

            var defaults = DesignSizeInfo.Read("<Card Width=\"{Binding W}\"/>");
            Assert.AreEqual(DesignSizeInfo.DefaultWidth, defaults.Width);
            Assert.AreEqual("DesignWidth", defaults.WidthAttribute);
            Assert.AreEqual("640", DesignSizeInfo.Format(640.4));
        }
    }
}
