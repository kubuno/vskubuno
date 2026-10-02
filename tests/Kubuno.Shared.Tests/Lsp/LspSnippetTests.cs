using System.Linq;
using Kubuno.Core.Logic.Lsp;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Core.Tests.Lsp
{
    [TestClass]
    public sealed class LspSnippetTests
    {
        [TestMethod]
        public void ArgumentPlaceholdersAreVisitedInOrderThenTheEnd()
        {
            // rust-analyzer's "fill_arguments" completion of a method call.
            var snippet = LspSnippet.Parse("push(${1:value}, ${2:other})$0");

            Assert.AreEqual("push(value, other)", snippet.Text);
            CollectionAssert.AreEqual(new[] { 1, 2, 0 }, snippet.Stops.Select(s => s.Index).ToArray());
            Assert.AreEqual("value", snippet.Text.Substring(snippet.Stops[0].Start, snippet.Stops[0].Length));
            Assert.AreEqual("other", snippet.Text.Substring(snippet.Stops[1].Start, snippet.Stops[1].Length));
            Assert.AreEqual(snippet.Text.Length, snippet.FinalCaret);
            Assert.IsTrue(snippet.HasPlaceholders);
        }

        [TestMethod]
        public void AFinalCaretOnlySnippetHasNoPlaceholders()
        {
            var snippet = LspSnippet.Parse("len()$0");

            Assert.AreEqual("len()", snippet.Text);
            Assert.IsFalse(snippet.HasPlaceholders);
            Assert.AreEqual(5, snippet.FinalCaret);
        }

        [TestMethod]
        public void PostfixTemplatesKeepTheirLayoutAndTheCaretInTheBody()
        {
            var snippet = LspSnippet.Parse("if x {\n    $0\n}");

            Assert.AreEqual("if x {\n    \n}", snippet.Text);
            Assert.AreEqual(11, snippet.FinalCaret);
        }

        [TestMethod]
        public void EscapesChoicesAndVariablesExpand()
        {
            Assert.AreEqual("a$b}", LspSnippet.Parse("a\\$b\\}").Text);
            var choice = LspSnippet.Parse("${1|first,second|} ${TM_SELECTED_TEXT:dflt}$SOMETHING end");
            Assert.AreEqual("first dflt end", choice.Text);
            Assert.AreEqual(5, choice.Stops[0].Length);
        }

        [TestMethod]
        public void NestedPlaceholdersAreBothStops()
        {
            var snippet = LspSnippet.Parse("${1:Vec<${2:T}>}");

            Assert.AreEqual("Vec<T>", snippet.Text);
            Assert.AreEqual(6, snippet.Stops.First(s => s.Index == 1).Length);
            Assert.AreEqual(4, snippet.Stops.First(s => s.Index == 2).Start);
        }
    }
}
