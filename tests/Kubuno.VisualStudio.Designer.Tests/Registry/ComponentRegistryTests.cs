using System.Linq;
using Kubuno.VisualStudio.Designer.Registry;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.VisualStudio.Designer.Tests.Registry
{
    [TestClass]
    public class ComponentRegistryTests
    {
        private static ComponentRegistry LoadFixture() =>
            ComponentRegistry.FromJson(TestFixtures.ReadAllText("registry.sample.json"));

        [TestMethod]
        public void FromJson_ParsesEveryComponent()
        {
            var registry = LoadFixture();

            Assert.AreEqual(10, registry.Components.Count);
            Assert.IsNotNull(registry.Find("Button"));
            Assert.IsNull(registry.Find("DoesNotExist"));
        }

        [TestMethod]
        public void FromJson_GroupsByFamily_InFirstSeenOrder()
        {
            var registry = LoadFixture();

            CollectionAssert.AreEqual(
                new[] { "core", "choice", "containers", "data", "display", "text" },
                registry.FamilyNames.ToArray());

            Assert.AreEqual(5, registry.Families["core"].Count);
            Assert.AreEqual(1, registry.Families["choice"].Count);
        }

        [TestMethod]
        public void FromJson_ParsesScalarPropKinds()
        {
            var registry = LoadFixture();
            var button = registry.Find("Button")!;

            var text = button.Properties.Single(p => p.Name == "Text");
            Assert.AreEqual(PropKindTag.String, text.Kind.Tag);

            var enabled = button.Properties.Single(p => p.Name == "Enabled");
            Assert.AreEqual(PropKindTag.Bool, enabled.Kind.Tag);
            Assert.AreEqual("true", enabled.Default);

            var stack = registry.Find("Stack")!;
            var gap = stack.Properties.Single(p => p.Name == "Gap");
            Assert.AreEqual(PropKindTag.F32, gap.Kind.Tag);
        }

        [TestMethod]
        public void FromJson_ParsesEnumPropKind_WithItsVariants()
        {
            var registry = LoadFixture();
            var button = registry.Find("Button")!;

            var variant = button.Properties.Single(p => p.Name == "Variant");
            Assert.AreEqual(PropKindTag.Enum, variant.Kind.Tag);
            CollectionAssert.AreEqual(new[] { "Primary", "Secondary", "Ghost" }, variant.Kind.EnumVariants.ToArray());
        }

        [TestMethod]
        public void FromJson_ParsesChildrenModel_ForAllThreeVariants()
        {
            var registry = LoadFixture();

            Assert.AreEqual(ChildrenModel.None, registry.Find("Button")!.Children);
            Assert.AreEqual(ChildrenModel.SingleWidget, registry.Find("Card")!.Children);
            Assert.AreEqual(ChildrenModel.List, registry.Find("Stack")!.Children);
        }

        [TestMethod]
        public void FromJson_ParsesLayoutKind_NullForLeavesAndSingleWidget_SetForListContainers()
        {
            var registry = LoadFixture();

            Assert.IsNull(registry.Find("Button")!.LayoutKind);
            Assert.IsNull(registry.Find("Card")!.LayoutKind);
            Assert.AreEqual(Designer.Registry.LayoutKind.Flow, registry.Find("Stack")!.LayoutKind);
            Assert.AreEqual(Designer.Registry.LayoutKind.Dock, registry.Find("Panel")!.LayoutKind);
        }

        [TestMethod]
        public void FromJson_ParsesEvents()
        {
            var registry = LoadFixture();
            var button = registry.Find("Button")!;

            Assert.AreEqual(1, button.Events.Count);
            Assert.AreEqual("Click", button.Events[0].Name);
        }

        [TestMethod]
        public void FromJson_ParsesNullDefault()
        {
            var registry = LoadFixture();
            var textField = registry.Find("TextField")!;

            var width = textField.Properties.Single(p => p.Name == "Width");
            Assert.IsNull(width.Default);
        }
    }
}
