using System.Collections.Generic;
using Kubuno.Desktop.Designer.Editing;
using Kubuno.Desktop.Designer.Outline;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Desktop.Tests.Designer.Outline
{
    [TestClass]
    public class DocumentSymbolTreeBuilderTests
    {
        private static LspRange Range() => new LspRange(new LspPosition(0, 0), new LspPosition(0, 1));

        private static DocumentSymbolDto Symbol(string name, string? detail, params DocumentSymbolDto[] children) =>
            new DocumentSymbolDto(name, detail, Range(), children);

        [TestMethod]
        public void Build_NullOrEmpty_ReturnsAnEmptyList()
        {
            Assert.AreEqual(0, DocumentSymbolTreeBuilder.Build(null).Count);
            Assert.AreEqual(0, DocumentSymbolTreeBuilder.Build(new List<DocumentSymbolDto>()).Count);
        }

        [TestMethod]
        public void Build_SingleTopLevelEntry_IsTheDocumentRootWithTheEmptyId()
        {
            var nodes = DocumentSymbolTreeBuilder.Build(new[] { Symbol("Stack", null) });

            Assert.AreEqual(1, nodes.Count);
            Assert.AreEqual("", nodes[0].ElementId);
            Assert.AreEqual("Stack", nodes[0].Name);
        }

        [TestMethod]
        public void Build_ChildrenGetOrdinalIdsUnderTheRoot()
        {
            var nodes = DocumentSymbolTreeBuilder.Build(new[]
            {
                Symbol("Stack", null, Symbol("Button", null), Symbol("proxy", "TextField")),
            });

            var stack = nodes[0];
            Assert.AreEqual(2, stack.Children.Count);
            Assert.AreEqual("0", stack.Children[0].ElementId);
            Assert.AreEqual("Button", stack.Children[0].Name);
            Assert.AreEqual("1", stack.Children[1].ElementId);
            Assert.AreEqual("proxy", stack.Children[1].Name);
            Assert.AreEqual("TextField", stack.Children[1].Detail);
        }

        [TestMethod]
        public void Build_GrandchildrenGetDotSeparatedIds()
        {
            var nodes = DocumentSymbolTreeBuilder.Build(new[]
            {
                Symbol("Card", null, Symbol("Stack", null, Symbol("Button", null))),
            });

            var button = nodes[0].Children[0].Children[0];
            Assert.AreEqual("0.0", button.ElementId);
            Assert.AreEqual("Button", button.Name);
        }
    }
}
