using Kubuno.Rust.Logic.IntelliSense;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Rust.Tests.IntelliSense
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
    }
}
