using System.Linq;
using Kubuno.VisualStudio.Core.Overrides;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.VisualStudio.Tests.Overrides
{
    /// <summary>docs/EVENTS.md EVT-7b, "Override assistance": "Substituer des membres…" without rust-analyzer.</summary>
    [TestClass]
    public class OverrideAssistantTests
    {
        private const string Round = @"//! RoundButton.
use kubuno_views::prelude::*;

/// A pill.
#[derive(Component, Default)]
#[kubuno(extends = Button)]
pub struct RoundButton {
    base: Button,
    /// A brace in a comment: {
    #[property]
    pub label: String, // ""}""
}

impl RoundButton {
    pub fn new() -> Self {
        Self::default()
    }
}
";

        private static OverrideCatalog Catalog => OverrideCatalog.Default;

        [TestMethod]
        public void The_embedded_catalogue_matches_the_rust_table()
        {
            Assert.AreEqual(62, Catalog.Members.Count);
            var print = Catalog.Members.Single(m => m.Level == "Control" && m.Name == "on_print");
            Assert.AreEqual("self.paint_layers(e);", print.BaseCall, "on_print's base rendering must reach the class's own on_paint");
            Assert.IsTrue(Catalog.Members.Any(m => m.Name == "on_drag_drop" && m.Event == "OnDragDrop"));
            CollectionAssert.AreEqual(new[] { "Button", "ButtonBase", "Control", "Component" }, (Catalog.Chains["Button"]).ToList());
            CollectionAssert.AreEqual(new[] { "Control", "Component" }, (Catalog.Chains["Control"]).ToList());
            var click = Catalog.Members.Single(m => m.Level == "Control" && m.Name == "on_click");
            Assert.AreEqual("fn on_click(&mut self, e: &mut EventCx<'_, MouseEventArgs>)", click.Signature);
            Assert.AreEqual("self.base_mut().on_click(e);", click.BaseCall);
            Assert.AreEqual("OnClick", click.Event);
            StringAssert.StartsWith(click.LocalizedDoc(french: true), "Déclenche Click");
        }

        [TestMethod]
        public void The_scanner_ignores_comments_and_strings()
        {
            var scan = RustItemScanner.Scan(Round);
            Assert.AreEqual(1, scan.Structs.Count);
            var s = scan.Structs[0];
            Assert.AreEqual("RoundButton", s.Name);
            Assert.IsTrue(s.IsComponent);
            Assert.AreEqual("Button", s.Extends);
            Assert.AreEqual(Round.IndexOf("/// A pill.", System.StringComparison.Ordinal), s.ItemStart);
            Assert.AreEqual(Round.IndexOf("impl RoundButton", System.StringComparison.Ordinal) - 2, s.ItemEnd);
            Assert.AreEqual(1, scan.Impls.Count);
            var impl = scan.Impls[0];
            Assert.IsNull(impl.Trait);
            CollectionAssert.AreEqual(new[] { "new" }, (impl.Methods).ToList());
            Assert.IsTrue(scan.HasPrelude);
            Assert.AreEqual("x = '{'; let s = r#\"}\"#;".Length, RustItemScanner.Mask("x = '{'; let s = r#\"}\"#;").Length);
            Assert.IsFalse(RustItemScanner.Mask("x = '{'; let s = r#\"}\"#; /* { /* } */ */ fn f<'a>(x: &'a u8) {}").Substring(0, 30).Contains("{"));
        }

        [TestMethod]
        public void On_the_struct_every_member_of_its_chain_is_offered()
        {
            var context = OverrideAssistant.Analyze(Round, Round.IndexOf("base: Button", System.StringComparison.Ordinal), Catalog);

            Assert.IsNotNull(context);
            Assert.AreEqual("RoundButton", context!.TypeName);
            CollectionAssert.AreEqual(new[] { "RoundButton", "Button", "ButtonBase", "Control", "Component" }, (context.Chain).ToList());
            Assert.AreEqual("on_checked_changed", context.Available.First().Name);
            Assert.IsTrue(context.Available.Any(m => m.Name == "on_paint"));
            Assert.IsFalse(context.Available.Any(m => m.Level == "ListControl"));
            Assert.IsNull(OverrideAssistant.Analyze(Round, 0, Catalog));
        }

        [TestMethod]
        public void Overriding_creates_the_impl_and_lists_the_level_in_overrides()
        {
            var context = OverrideAssistant.Analyze(Round, Round.IndexOf("pub struct", System.StringComparison.Ordinal), Catalog)!;
            var paint = context.Available.Single(m => m.Name == "on_paint");
            var click = context.Available.Single(m => m.Name == "on_click");

            var edits = OverrideAssistant.Plan(context, new[] { paint, click }, "\n", out var caret);
            var result = OverrideAssistant.Apply(Round, edits);

            StringAssert.Contains(result, "#[kubuno(extends = Button, overrides(Control))]");
            StringAssert.Contains(
                result,
                "\nimpl Control for RoundButton {\n    fn on_paint(&mut self, e: &mut PaintEventCx<'_>) {\n        self.base_mut().on_paint(e);\n    }\n\n    fn on_click(&mut self, e: &mut EventCx<'_, MouseEventArgs>) {\n        self.base_mut().on_click(e);\n    }\n}\n");
            Assert.IsTrue(result.IndexOf("impl Control", System.StringComparison.Ordinal) > result.IndexOf("pub struct", System.StringComparison.Ordinal));
            Assert.AreEqual(result.IndexOf("self.base_mut().on_paint(e);", System.StringComparison.Ordinal), caret);

            // Inside the new impl, the members already written are no longer offered, and new ones join it.
            var again = OverrideAssistant.Analyze(result, result.IndexOf("fn on_click", System.StringComparison.Ordinal), Catalog)!;
            Assert.IsFalse(again.Available.Any(m => m.Name == "on_paint" || m.Name == "on_click"));
            var more = OverrideAssistant.Apply(result, OverrideAssistant.Plan(again, new[] { again.Available.Single(m => m.Name == "on_mouse_down") }, "\n", out _));
            StringAssert.Contains(more, "        self.base_mut().on_click(e);\n    }\n\n    fn on_mouse_down(&mut self, e: &mut EventCx<'_, MouseEventArgs>) {\n        self.base_mut().on_mouse_down(e);\n    }\n}\n");
            Assert.AreEqual(1, System.Text.RegularExpressions.Regex.Matches(more, "overrides\\(").Count);

            // A second level extends `overrides(…)`.
            var level = OverrideAssistant.Analyze(more, more.IndexOf("pub struct", System.StringComparison.Ordinal), Catalog)!;
            var withLevel = OverrideAssistant.Apply(more, OverrideAssistant.Plan(level, new[] { level.Available.Single(m => m.Name == "on_checked_changed") }, "\n", out _));
            StringAssert.Contains(withLevel, "overrides(Control, ButtonBase)");
            StringAssert.Contains(withLevel, "impl ButtonBase for RoundButton {");
        }

        [TestMethod]
        public void A_user_control_without_a_kubuno_attribute_gets_one_and_the_prelude_is_added()
        {
            const string text = "/// Stars.\r\n#[derive(UserControl, Default)]\r\n#[user_control(view = \"stars.kbview\")]\r\npub struct Stars {\r\n    base: UserControlCore,\r\n}\r\n";
            var context = OverrideAssistant.Analyze(text, text.IndexOf("base:", System.StringComparison.Ordinal), Catalog)!;
            CollectionAssert.AreEqual(new[] { "Stars", "UserControl", "ContainerControl", "ScrollableControl", "Control", "Component" }, (context.Chain).ToList());
            var load = context.Available.Single(m => m.Level == "UserControl" && m.Name == "on_load");

            var result = OverrideAssistant.Apply(text, OverrideAssistant.Plan(context, new[] { load }, "\r\n", out _));

            StringAssert.Contains(result, "#[user_control(view = \"stars.kbview\")]\r\n#[kubuno(overrides(UserControl))]\r\npub struct Stars");
            StringAssert.Contains(result, "impl UserControl for Stars {\r\n    fn on_load(&mut self, e: &mut EventCx<'_, EmptyEventArgs>) {\r\n        self.base_mut().on_load(e);\r\n    }\r\n}\r\n");
            StringAssert.StartsWith(result.Substring(result.IndexOf("use kubuno", System.StringComparison.Ordinal)), "use kubuno_views::prelude::*;\r\n");
        }

        [TestMethod]
        public void A_class_extending_a_class_of_the_same_file_gets_its_chain()
        {
            const string text = "#[derive(Component)]\n#[kubuno(extends = Control, overrides(Control))]\nstruct Gauge { base: ControlCore }\nimpl Control for Gauge {}\n#[derive(Component)]\n#[kubuno(extends = Gauge, levels(Control))]\nstruct BigGauge { base: Gauge }\n";
            var context = OverrideAssistant.Analyze(text, text.LastIndexOf("base", System.StringComparison.Ordinal), Catalog)!;
            CollectionAssert.AreEqual(new[] { "BigGauge", "Gauge", "Control", "Component" }, (context.Chain).ToList());
            var inImpl = OverrideAssistant.Analyze(text, text.IndexOf("impl Control for Gauge {", System.StringComparison.Ordinal) + 23, Catalog)!;
            Assert.AreEqual("Gauge", inImpl.TypeName);
        }

        [TestMethod]
        public void Snippets_are_defined()
        {
            CollectionAssert.AreEqual(new[] { "onpaint", "handler", "event", "prop" }, (RustSnippets.All.Select(s => s.Shortcut)).ToList());
            StringAssert.Contains(RustSnippets.All[0].Text("    ", "\n"), "self.base_mut().on_paint(e);");
        }
    }
}
