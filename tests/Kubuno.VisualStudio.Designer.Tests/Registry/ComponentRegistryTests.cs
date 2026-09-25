using System;
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

            // The fixture is now the real `kubuno-views` registry export (DSG-1,
            // `kubuno-views/src/registry/export.rs`), regenerated straight from
            // `cargo test -p kubuno-views`'s own component table rather than
            // hand-written - 49 components: the 5 phase-2a examples plus every
            // enabled family (choice 5, containers 14, data 8, display 9, text 8).
            Assert.AreEqual(49, registry.Components.Count);
            Assert.IsNotNull(registry.Find("Button"));
            Assert.IsNull(registry.Find("DoesNotExist"));
        }

        [TestMethod]
        public void FromJson_GroupsByFamily_InFirstSeenOrder()
        {
            var registry = LoadFixture();

            // First-seen order in the real export is `components::ALL` (-> "core")
            // followed by `families::ALL_FAMILIES`'s own declared order
            // (display, choice, text, containers, data -
            // `kubuno-views/src/registry/families/mod.rs`), not alphabetical.
            CollectionAssert.AreEqual(
                new[] { "core", "display", "choice", "text", "containers", "data" },
                registry.FamilyNames.ToArray());

            Assert.AreEqual(5, registry.Families["core"].Count);
            Assert.AreEqual(5, registry.Families["choice"].Count);
            Assert.AreEqual(14, registry.Families["containers"].Count);
            Assert.AreEqual(8, registry.Families["data"].Count);
            Assert.AreEqual(9, registry.Families["display"].Count);
            Assert.AreEqual(8, registry.Families["text"].Count);
        }

        [TestMethod]
        public void FromJson_ParsesScalarPropKinds()
        {
            var registry = LoadFixture();
            var button = registry.Find("Button")!;

            var text = button.Properties.Single(p => p.Name == "Text");
            Assert.AreEqual(PropKindTag.String, text.Kind.Tag);

            var loading = button.Properties.Single(p => p.Name == "Loading");
            Assert.AreEqual(PropKindTag.Bool, loading.Kind.Tag);
            Assert.AreEqual("false", loading.Default);

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
            CollectionAssert.AreEqual(
                new[] { "Primary", "Secondary", "Ghost", "Text", "Danger", "TextDanger" },
                variant.Kind.EnumVariants.ToArray());
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
        public void FromJson_ParsesAllowedChildren_EmptyWhenUngated_SetForAGatedListContainer()
        {
            var registry = LoadFixture();

            // `Stack` accepts any child (an ungated `List`); `Tabs` only ever hosts
            // `TabItem` (a gated `List`, `ChildrenModel::List(&["TabItem"])`).
            CollectionAssert.AreEqual(Array.Empty<string>(), registry.Find("Stack")!.AllowedChildren.ToArray());
            CollectionAssert.AreEqual(new[] { "TabItem" }, registry.Find("Tabs")!.AllowedChildren.ToArray());
        }

        [TestMethod]
        public void FromJson_ParsesLayoutKind_NullForLeavesAndSingleWidget_SetForListContainers()
        {
            var registry = LoadFixture();

            Assert.IsNull(registry.Find("Button")!.LayoutKind);
            Assert.IsNull(registry.Find("Card")!.LayoutKind);
            Assert.AreEqual(Designer.Registry.LayoutKind.Flow, registry.Find("Stack")!.LayoutKind);
            // `Panel`'s single Dock/Anchor engine - see `Registry/LayoutKind.cs`'s
            // own doc comment for why this is `DockAnchor`, not the originally
            // guessed `Dock`.
            Assert.AreEqual(Designer.Registry.LayoutKind.DockAnchor, registry.Find("Panel")!.LayoutKind);
            Assert.AreEqual(Designer.Registry.LayoutKind.Tabs, registry.Find("Tabs")!.LayoutKind);
        }

        [TestMethod]
        public void FromJson_ParsesEvents()
        {
            var registry = LoadFixture();
            var button = registry.Find("Button")!;

            Assert.AreEqual(1, button.Events.Count);
            Assert.AreEqual("OnClick", button.Events[0].Name);
        }

        [TestMethod]
        public void FromJson_DefaultIsNeverNull_RealPropertyMetaHasNoOptionalDefault()
        {
            // `kubuno_views::registry::PropertyMeta.default` is a plain
            // `&'static str` (never `Option`), so the real export never emits a
            // JSON `null` default - unlike the old illustrative fixture's
            // fictional `TextField.Width` (no such property exists on the real
            // `TextField`). `PropertyMeta.Default` stays nullable in C# for
            // robustness, but every property in a real export has a literal
            // string default, possibly empty.
            var registry = LoadFixture();
            var textField = registry.Find("TextField")!;

            var text = textField.Properties.Single(p => p.Name == "Text");
            Assert.AreEqual(string.Empty, text.Default);
            Assert.IsTrue(registry.Components.SelectMany(c => c.Properties).All(p => p.Default != null));
        }
    }
}
