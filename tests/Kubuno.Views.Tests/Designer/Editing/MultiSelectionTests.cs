using System.Linq;
using Kubuno.Views.Designer.Editing;
using Kubuno.Views.Designer.Registry;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Views.Tests.Designer.Editing
{
    /// <summary>docs/DESIGNER.md §13: multi-selection helpers, the Layout commands' enabling and the multi-element structure gestures.</summary>
    [TestClass]
    public class MultiSelectionTests
    {
        // "0", "1": Anchor buttons; "2": a docked button; "3": a Stack (flow) with "3.0", "3.1".
        private const string View = "<Panel>\n  <Button X=\"10\" Y=\"10\"/>\n  <Button X=\"100\" Y=\"20\"/>\n  <Button Dock=\"Top\"/>\n  <Stack>\n    <Button/>\n    <Button/>\n  </Stack>\n</Panel>";

        private static readonly ComponentRegistry Registry = ComponentRegistry.FromJson(TestFixtures.ReadAllText("registry.sample.json"));

        private static LayoutSelectionInfo Layout(string primary, params string[] others) =>
            LayoutSelectionInfo.Build(View, new[] { primary }.Concat(others).ToList(), primary, Registry);

        // ── MultiSelection ────────────────────────────────────────────────

        [TestMethod]
        public void TopLevel_DropsTheRootNestedAndDuplicateIds_InDocumentOrder()
        {
            CollectionAssert.AreEqual(new[] { "1", "3" }, MultiSelection.TopLevel(new[] { "", "3.1", "3", "1", "1" }).ToArray());
            CollectionAssert.AreEqual(new[] { "1", "1.0", "2", "10" }, MultiSelection.InDocumentOrder(new[] { "10", "2", "1.0", "1" }).ToArray());
            Assert.IsTrue(MultiSelection.IsAncestor("", "0"));
            Assert.IsTrue(MultiSelection.IsAncestor("1", "1.0"));
            Assert.IsFalse(MultiSelection.IsAncestor("1", "10"));
            Assert.IsFalse(MultiSelection.IsAncestor("1", "1"));
        }

        // ── Layout commands: mapping ──────────────────────────────────────

        [TestMethod]
        public void LayoutCommands_MapToVisualStudiosStandardCommandsAndTheSurfaceNames()
        {
            Assert.IsTrue(DesignerLayoutCommands.TryFromStandardCommand(3, out var alignLeft));
            Assert.AreEqual(DesignerLayoutCommand.AlignLefts, alignLeft);
            Assert.IsFalse(DesignerLayoutCommands.TryFromStandardCommand(26, out _), "Paste is not a layout command");
            Assert.AreEqual(35u, DesignerLayoutCommands.StandardCommandId(DesignerLayoutCommand.MakeSameSize));
            Assert.AreEqual("horizontalSpacingEqual", DesignerLayoutCommands.FormatName(DesignerLayoutCommand.HorizontalSpacingEqual));
            Assert.AreEqual("alignLefts", DesignerLayoutCommands.FormatName(DesignerLayoutCommand.AlignLefts));
            Assert.IsNull(DesignerLayoutCommands.FormatName(DesignerLayoutCommand.BringToFront));

            // Every command has its own standard command id, and maps back to itself.
            var ids = DesignerLayoutCommands.All.Select(DesignerLayoutCommands.StandardCommandId).ToList();
            Assert.AreEqual(ids.Count, ids.Distinct().Count());
            foreach (var command in DesignerLayoutCommands.All)
            {
                Assert.IsTrue(DesignerLayoutCommands.TryFromStandardCommand(DesignerLayoutCommands.StandardCommandId(command), out var back));
                Assert.AreEqual(command, back);
            }
        }

        // ── Layout commands: enabling ─────────────────────────────────────

        [TestMethod]
        public void AlignAndSize_NeedTwoMovableElementsIncludingThePrimary()
        {
            var two = Layout("1", "0");
            Assert.AreEqual(2, two.MemberCount);
            Assert.IsTrue(two.PrimaryIsMember);
            Assert.IsTrue(two.IsEnabled(DesignerLayoutCommand.AlignLefts));
            Assert.IsTrue(two.IsEnabled(DesignerLayoutCommand.MakeSameSize));
            Assert.IsTrue(two.IsEnabled(DesignerLayoutCommand.HorizontalSpacingRemove));
            Assert.IsFalse(two.IsEnabled(DesignerLayoutCommand.HorizontalSpacingEqual), "making spacing equal needs three");

            var one = Layout("0");
            Assert.IsFalse(one.IsEnabled(DesignerLayoutCommand.AlignLefts));
            Assert.IsTrue(one.IsEnabled(DesignerLayoutCommand.CenterHorizontally), "a single Anchor child can be centred");

            // A docked element is not movable: it is left out, and as the primary it cannot be the reference.
            var docked = Layout("2", "0");
            Assert.AreEqual(1, docked.MemberCount);
            Assert.IsFalse(docked.PrimaryIsMember);
            Assert.IsFalse(docked.IsEnabled(DesignerLayoutCommand.AlignLefts));
        }

        [TestMethod]
        public void LayoutCommands_AreDisabledForFlowChildrenAndTheView()
        {
            var flow = Layout("3.0", "3.1");
            Assert.AreEqual(0, flow.MemberCount, "a Stack child has no X/Y");
            Assert.IsFalse(flow.IsEnabled(DesignerLayoutCommand.AlignTops));
            Assert.IsFalse(flow.IsEnabled(DesignerLayoutCommand.CenterVertically));
            Assert.IsFalse(Layout("").IsEnabled(DesignerLayoutCommand.CenterHorizontally));
            Assert.IsFalse(Layout("").IsEnabled(DesignerLayoutCommand.BringToFront));
            Assert.IsFalse(LayoutSelectionInfo.Empty.IsEnabled(DesignerLayoutCommand.SendToBack));
        }

        [TestMethod]
        public void ZOrder_MovesTheSelectedChildrenOfThePrimaryContainerTogether()
        {
            var info = Layout("0", "2");
            Assert.AreEqual("", info.OrderParentId);
            CollectionAssert.AreEqual(new[] { 1, 3, 0, 2 }, info.FrontOrder!.ToArray());
            CollectionAssert.AreEqual(new[] { 0, 2, 1, 3 }, info.BackOrder!.ToArray());

            var last = Layout("3.1");
            Assert.IsNull(last.FrontOrder, "already in front");
            Assert.IsFalse(last.IsEnabled(DesignerLayoutCommand.BringToFront));
            CollectionAssert.AreEqual(new[] { 1, 0 }, last.BackOrder!.ToArray());
            Assert.IsFalse(Layout("3.0", "3.1").IsEnabled(DesignerLayoutCommand.SendToBack), "every child selected: nothing moves");
        }

        // ── Multi-element structure gestures ──────────────────────────────

        [TestMethod]
        public void RootTags_ListEveryTopLevelElementOfAFragment()
        {
            CollectionAssert.AreEqual(new[] { "Button", "Stack" }, DesignerFragment.RootTags("<Button/>\n<Stack>\n  <Button/>\n</Stack>").ToArray());
            Assert.AreEqual(0, DesignerFragment.RootTags("no xml").Count);
            Assert.AreEqual("<A/>\n<B>\n  <C/>\n</B>", DesignerFragment.Join(new[] { "<A/>", " ", "<B>\n  <C/>\n</B>\n" }));
        }

        [TestMethod]
        public void PlanPasteMany_GoesIntoAContainerThatTakesThemAll_ElseNextToTheTarget()
        {
            var intoStack = DesignerStructurePlanner.PlanPasteMany(View, "3", new[] { "Button", "Button" }, Registry);
            Assert.AreEqual("3", intoStack!.ParentId);
            Assert.AreEqual(2, intoStack.Index);

            var nextTo = DesignerStructurePlanner.PlanPasteMany(View, "0", new[] { "Button", "Switch" }, Registry);
            Assert.AreEqual("", nextTo!.ParentId);
            Assert.AreEqual(1, nextTo.Index);

            Assert.IsNull(DesignerStructurePlanner.PlanPasteMany(View, "0", new[] { "Button", "NoSuchControl" }, Registry));
        }

        [TestMethod]
        public void PlanDuplicateMany_PutsTheCopiesAfterTheLastSelectedElement()
        {
            var plan = DesignerStructurePlanner.PlanDuplicateMany(View, new[] { "1", "0" }, "1", Registry);
            Assert.AreEqual("", plan!.ParentId);
            Assert.AreEqual(2, plan.Index);
            Assert.IsNull(DesignerStructurePlanner.PlanDuplicateMany(View, new[] { "" }, "", Registry), "the view itself is never duplicated");
        }
    }
}
