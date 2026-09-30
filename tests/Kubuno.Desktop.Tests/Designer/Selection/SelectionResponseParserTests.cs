using Kubuno.Desktop.Designer.Selection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Desktop.Tests.Designer.Selection
{
    [TestClass]
    public class SelectionResponseParserTests
    {
        [TestMethod]
        public void ParseElementAtOffset_ValidPayload_ReadsIdAndRange()
        {
            var json = "{\"elementId\":\"1\",\"range\":{\"start\":{\"line\":0,\"character\":25},\"end\":{\"line\":0,\"character\":40}}}";

            var result = SelectionResponseParser.ParseElementAtOffset(json);

            Assert.IsNotNull(result);
            Assert.AreEqual("1", result!.ElementId);
            Assert.AreEqual(0, result.Range.Start.Line);
            Assert.AreEqual(25, result.Range.Start.Character);
            Assert.AreEqual(0, result.Range.End.Line);
            Assert.AreEqual(40, result.Range.End.Character);
        }

        [TestMethod]
        public void ParseElementAtOffset_JsonNullLiteral_IsNull()
        {
            Assert.IsNull(SelectionResponseParser.ParseElementAtOffset("null"));
        }

        [TestMethod]
        public void ParseElementAtOffset_EmptyOrMalformedPayload_IsNull()
        {
            Assert.IsNull(SelectionResponseParser.ParseElementAtOffset(null));
            Assert.IsNull(SelectionResponseParser.ParseElementAtOffset(string.Empty));
            Assert.IsNull(SelectionResponseParser.ParseElementAtOffset("{not json"));
            Assert.IsNull(SelectionResponseParser.ParseElementAtOffset("{}"));
        }

        [TestMethod]
        public void ParseRangeOfElement_ValidPayload_ReadsRange()
        {
            var json = "{\"range\":{\"start\":{\"line\":3,\"character\":0},\"end\":{\"line\":3,\"character\":10}}}";

            var result = SelectionResponseParser.ParseRangeOfElement(json);

            Assert.IsTrue(result.HasValue);
            Assert.AreEqual(3, result!.Value.Start.Line);
            Assert.AreEqual(10, result.Value.End.Character);
        }

        [TestMethod]
        public void ParseRangeOfElement_JsonNullLiteral_IsNull()
        {
            Assert.IsNull(SelectionResponseParser.ParseRangeOfElement("null"));
        }

        [TestMethod]
        public void ParseDocumentSymbols_NestedTree_PreservesOrderAndChildren()
        {
            var json = "[{\"name\":\"Stack\",\"detail\":null," +
                       "\"range\":{\"start\":{\"line\":0,\"character\":0},\"end\":{\"line\":2,\"character\":8}}," +
                       "\"selectionRange\":{\"start\":{\"line\":0,\"character\":1},\"end\":{\"line\":0,\"character\":6}}," +
                       "\"children\":[" +
                       "{\"name\":\"proxy\",\"detail\":\"TextField\"," +
                       "\"range\":{\"start\":{\"line\":1,\"character\":0},\"end\":{\"line\":1,\"character\":20}}," +
                       "\"selectionRange\":{\"start\":{\"line\":1,\"character\":1},\"end\":{\"line\":1,\"character\":10}}}" +
                       "]}]";

            var symbols = SelectionResponseParser.ParseDocumentSymbols(json);

            Assert.IsNotNull(symbols);
            Assert.AreEqual(1, symbols!.Count);
            Assert.AreEqual("Stack", symbols[0].Name);
            Assert.IsNull(symbols[0].Detail);
            Assert.AreEqual(1, symbols[0].Children.Count);
            Assert.AreEqual("proxy", symbols[0].Children[0].Name);
            Assert.AreEqual("TextField", symbols[0].Children[0].Detail);
        }

        [TestMethod]
        public void ParseDocumentSymbols_EmptyArray_IsAnEmptyListNotNull()
        {
            var symbols = SelectionResponseParser.ParseDocumentSymbols("[]");

            Assert.IsNotNull(symbols);
            Assert.AreEqual(0, symbols!.Count);
        }

        [TestMethod]
        public void ParseDocumentSymbols_NotAnArray_IsNull()
        {
            Assert.IsNull(SelectionResponseParser.ParseDocumentSymbols("{\"name\":\"oops\"}"));
        }

        [TestMethod]
        public void ParseDocumentSymbols_MalformedPayload_IsNull()
        {
            Assert.IsNull(SelectionResponseParser.ParseDocumentSymbols(null));
            Assert.IsNull(SelectionResponseParser.ParseDocumentSymbols("not json"));
        }
    }
}
