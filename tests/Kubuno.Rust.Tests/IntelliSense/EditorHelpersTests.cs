using Kubuno.VisualStudio.Core.IntelliSense;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.VisualStudio.Tests.IntelliSense
{
    [TestClass]
    public sealed class EditorHelpersTests
    {
        [TestMethod]
        public void CodeLensCountsReadLikeCSharpInBothLanguages()
        {
            var three = CodeLensText.Parse("3 references", 3)!;
            Assert.AreEqual("3 references", CodeLensText.Format(three, french: false));
            Assert.AreEqual("3 références", CodeLensText.Format(three, french: true));
            Assert.AreEqual("1 reference", CodeLensText.Format(CodeLensText.Parse("1 reference", null)!, french: false));
            Assert.AreEqual("0 référence", CodeLensText.Format(CodeLensText.Parse("0 references", 0)!, french: true));
            Assert.AreEqual("2 implémentations", CodeLensText.Format(CodeLensText.Parse("2 implementations", null)!, french: true));
            Assert.IsNull(CodeLensText.Parse("▶︎ Run", null));
        }

        [TestMethod]
        public void TheLocationCountWinsOverTheTitle()
        {
            Assert.AreEqual(5, CodeLensText.Parse("3 references", 5)!.Count);
        }

        [TestMethod]
        public void DocCommentsAreContinuedAndLineCommentsOnlyWhenSplit()
        {
            Assert.AreEqual("    /// ", CommentContinuation.PrefixFor("    /// Adds two numbers.", string.Empty));
            Assert.AreEqual("//! ", CommentContinuation.PrefixFor("//! Crate docs", string.Empty));
            Assert.AreEqual("  // ", CommentContinuation.PrefixFor("  // split this", " comment"));
            Assert.IsNull(CommentContinuation.PrefixFor("  // at the end", string.Empty));
            Assert.IsNull(CommentContinuation.PrefixFor("let x = 1; // trailing", " more"));
            Assert.IsNull(CommentContinuation.PrefixFor("    ", "/// before the marker"));
            Assert.IsNull(CommentContinuation.PrefixFor("//// not a doc comment", string.Empty));
        }

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
