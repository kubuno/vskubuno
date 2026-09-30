using Kubuno.Desktop.Logic.IntelliSense;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Desktop.Tests.IntelliSense
{
    /// <summary>The .kbview editor helpers (event values, binding paths, navigation to Rust) - split from the Rust editor helper tests.</summary>
    [TestClass]
    public sealed class KbviewEditorHelpersTests
    {
        [TestMethod]
        public void EventValuesAndBindingPathsAreRecognizedInViews()
        {
            var text = "  <Button OnClick=\"on_ok\" Text=\"{Binding Status}\" Width=\"10\"/>";

            var handler = KbviewValueContext.At(text, text.IndexOf("on_ok") + 2)!;
            Assert.AreEqual(KbviewValueKind.Handler, handler.Kind);
            Assert.AreEqual("OnClick", handler.Attribute);
            Assert.AreEqual("on_ok", text.Substring(handler.ReplaceStart, handler.ReplaceLength));

            var binding = KbviewValueContext.At(text, text.IndexOf("Status") + 3)!;
            Assert.AreEqual(KbviewValueKind.BindingPath, binding.Kind);
            Assert.AreEqual("Status", text.Substring(binding.ReplaceStart, binding.ReplaceLength));

            Assert.IsNull(KbviewValueContext.At(text, text.IndexOf("10")));
            Assert.IsNull(KbviewValueContext.At(text, text.IndexOf("Button")));
            Assert.AreEqual(KbviewValueKind.Handler, KbviewValueContext.At("<Button OnClick=\"\"", 17)!.Kind);
            Assert.AreEqual(KbviewValueKind.BindingPath, KbviewValueContext.At("<Label Text=\"{Binding Path=Na", 29)!.Kind);
            Assert.IsNull(KbviewValueContext.At("<Label Text=\"{Binding Name, Mode=", 33));
            Assert.AreEqual(5, KbviewValueContext.At(text, text.IndexOf("on_ok"))!.Shift(5).ReplaceStart - text.IndexOf("on_ok"));
        }

        [TestMethod]
        public void TagsAndBindingPathsLeadToRust()
        {
            var view = "<Panel>\n  <Button Text=\"{Binding Path=Title}\"/>\n  <ui:Card/>\n</Panel>";

            Assert.AreEqual("Button", KbviewNavigation.ElementNameAt(view, view.IndexOf("Button") + 2));
            Assert.AreEqual("Panel", KbviewNavigation.ElementNameAt(view, view.LastIndexOf("Panel") + 1));
            Assert.AreEqual("Card", KbviewNavigation.ElementNameAt(view, view.IndexOf("Card") + 1));
            Assert.IsNull(KbviewNavigation.ElementNameAt(view, view.IndexOf("Text") + 1));
            Assert.AreEqual("Title", KbviewNavigation.BindingPathAt(view, view.IndexOf("Title") + 2));
            Assert.IsNull(KbviewNavigation.BindingPathAt(view, view.IndexOf("Binding") + 1));

            var rust = "fn get(&self, path: &str) -> Option<Value> {\n    match path {\n        \"Title\" => Some(..),\n        \"Other\" | \"Alias\" => None,\n";
            Assert.AreEqual(rust.IndexOf("Title"), KbviewNavigation.FindBindingArm(rust, "Title"));
            Assert.AreEqual(rust.IndexOf("Other"), KbviewNavigation.FindBindingArm(rust, "Other"));
            Assert.AreEqual(-1, KbviewNavigation.FindBindingArm(rust, "Missing"));
        }
    }
}
