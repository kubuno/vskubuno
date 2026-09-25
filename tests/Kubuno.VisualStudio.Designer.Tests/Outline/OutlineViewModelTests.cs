using Kubuno.VisualStudio.Designer.Editing;
using Kubuno.VisualStudio.Designer.Outline;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.VisualStudio.Designer.Tests.Outline
{
    [TestClass]
    public class OutlineViewModelTests
    {
        private static LspRange Range() => new LspRange(new LspPosition(0, 0), new LspPosition(0, 1));

        private static OutlineNode Node(string id, string name, params OutlineNode[] children) => new OutlineNode(id, name, null, Range(), children);

        [TestMethod]
        public void Load_PopulatesRoots()
        {
            var vm = new OutlineViewModel();

            vm.Load(new[] { Node("", "Stack", Node("0", "Button")) });

            Assert.AreEqual(1, vm.Roots.Count);
            Assert.AreEqual("Stack", vm.Roots[0].DisplayName);
            Assert.AreEqual(1, vm.Roots[0].Children.Count);
            Assert.AreEqual("Button", vm.Roots[0].Children[0].DisplayName);
        }

        [TestMethod]
        public void ActivateNode_MarksItSelectedAndRaisesNodeActivated()
        {
            var vm = new OutlineViewModel();
            vm.Load(new[] { Node("", "Stack", Node("0", "Button")) });
            string? activated = null;
            vm.NodeActivated += (_, id) => activated = id;

            vm.ActivateNode("0");

            Assert.AreEqual("0", activated);
            Assert.IsTrue(vm.Roots[0].Children[0].IsSelected);
            Assert.IsFalse(vm.Roots[0].IsSelected);
        }

        [TestMethod]
        public void ActivateNode_MovingSelection_UnselectsThePreviousNode()
        {
            var vm = new OutlineViewModel();
            vm.Load(new[] { Node("", "Stack", Node("0", "Button"), Node("1", "Switch")) });

            vm.ActivateNode("0");
            vm.ActivateNode("1");

            Assert.IsFalse(vm.Roots[0].Children[0].IsSelected);
            Assert.IsTrue(vm.Roots[0].Children[1].IsSelected);
        }

        [TestMethod]
        public void Select_HostDrivenHighlight_DoesNotRaiseNodeActivated()
        {
            var vm = new OutlineViewModel();
            vm.Load(new[] { Node("", "Stack", Node("0", "Button")) });
            var raised = false;
            vm.NodeActivated += (_, _) => raised = true;

            vm.Select("0");

            Assert.IsFalse(raised, "a host-driven highlight must never look like a fresh user click");
            Assert.IsTrue(vm.Roots[0].Children[0].IsSelected);
        }

        [TestMethod]
        public void Select_Null_ClearsTheHighlight()
        {
            var vm = new OutlineViewModel();
            vm.Load(new[] { Node("", "Stack", Node("0", "Button")) });
            vm.Select("0");

            vm.Select(null);

            Assert.IsFalse(vm.Roots[0].Children[0].IsSelected);
        }

        [TestMethod]
        public void Load_ReappliesAnExistingHighlightAfterAReload()
        {
            var vm = new OutlineViewModel();
            vm.Load(new[] { Node("", "Stack", Node("0", "Button")) });
            vm.Select("0");

            vm.Load(new[] { Node("", "Stack", Node("0", "Button"), Node("1", "Switch")) });

            Assert.IsTrue(vm.Roots[0].Children[0].IsSelected);
        }
    }
}
