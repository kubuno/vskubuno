using Kubuno.Desktop.Designer.Handlers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Desktop.Tests.Designer.Handlers
{
    /// <summary>
    /// Exercises <see cref="CreateHandlerResponseParser"/> against realistic wire JSON - the exact
    /// camelCase shape `kubuno-views-ls`'s `handler_insert::CreateHandlerResult` serializes (verified
    /// against `lsp_types` 0.97's own `#[serde(rename_all = "camelCase")]` on `TextEdit`/`WorkspaceEdit`,
    /// e.g. `newText` not `new_text`), not a shape invented on the C# side.
    /// </summary>
    [TestClass]
    public class CreateHandlerResponseParserTests
    {
        [TestMethod]
        public void Parse_NullOrWhitespace_ReturnsNull()
        {
            Assert.IsNull(CreateHandlerResponseParser.Parse(null));
            Assert.IsNull(CreateHandlerResponseParser.Parse(string.Empty));
            Assert.IsNull(CreateHandlerResponseParser.Parse("   "));
        }

        [TestMethod]
        public void Parse_MalformedJson_ReturnsNullInsteadOfThrowing()
        {
            Assert.IsNull(CreateHandlerResponseParser.Parse("{ not json"));
        }

        [TestMethod]
        public void Parse_LocationOnlyResponse_ReadsUriAndRange()
        {
            const string json = """
                {
                  "handlerName": "offline_toggled",
                  "location": {
                    "uri": "file:///view.rs",
                    "range": { "start": { "line": 3, "character": 0 }, "end": { "line": 3, "character": 10 } }
                  },
                  "edit": null
                }
                """;

            var response = CreateHandlerResponseParser.Parse(json);

            Assert.IsNotNull(response);
            Assert.AreEqual("offline_toggled", response!.HandlerName);
            Assert.IsNull(response.Edit);
            Assert.IsNotNull(response.Location);
            Assert.AreEqual("file:///view.rs", response.Location!.Uri);
            Assert.AreEqual(3, response.Location.Range.Start.Line);
            Assert.AreEqual(0, response.Location.Range.Start.Character);
            Assert.AreEqual(10, response.Location.Range.End.Character);
        }

        [TestMethod]
        public void Parse_EditResponse_ReadsBothFilesAndEveryEdit()
        {
            const string json = """
                {
                  "handlerName": "on_button_click",
                  "location": null,
                  "edit": {
                    "changes": {
                      "file:///view.kbview": [
                        {
                          "range": { "start": { "line": 0, "character": 7 }, "end": { "line": 0, "character": 7 } },
                          "newText": " OnClick=\"on_button_click\""
                        }
                      ],
                      "file:///view.rs": [
                        {
                          "range": { "start": { "line": 2, "character": 0 }, "end": { "line": 2, "character": 0 } },
                          "newText": "fn on_button_click(vm: &mut dyn kubuno_views::binding::ViewModel, value: kubuno_views::binding::Value) {\n}\n\n"
                        },
                        {
                          "range": { "start": { "line": 4, "character": 4 }, "end": { "line": 4, "character": 4 } },
                          "newText": "\"on_button_click\" => |vm, value| on_button_click(vm, value),\n"
                        }
                      ]
                    }
                  }
                }
                """;

            var response = CreateHandlerResponseParser.Parse(json);

            Assert.IsNotNull(response);
            Assert.AreEqual("on_button_click", response!.HandlerName);
            Assert.IsNull(response.Location);
            Assert.IsNotNull(response.Edit);
            var changes = response.Edit!.Changes;
            Assert.IsNotNull(changes);
            Assert.AreEqual(2, changes!.Count);

            var kbviewEdits = changes["file:///view.kbview"];
            Assert.AreEqual(1, kbviewEdits.Count);
            Assert.AreEqual(" OnClick=\"on_button_click\"", kbviewEdits[0].NewText);
            Assert.AreEqual(0, kbviewEdits[0].Range.Start.Line);
            Assert.AreEqual(7, kbviewEdits[0].Range.Start.Character);

            var rsEdits = changes["file:///view.rs"];
            Assert.AreEqual(2, rsEdits.Count);
            StringAssert.Contains(rsEdits[0].NewText, "fn on_button_click(");
            StringAssert.Contains(rsEdits[1].NewText, "\"on_button_click\" => |vm, value| on_button_click(vm, value),");
        }

        [TestMethod]
        public void Parse_EmptyChangesMap_IsStillAValidResponse()
        {
            const string json = """{"handlerName":"x","location":null,"edit":{"changes":{}}}""";

            var response = CreateHandlerResponseParser.Parse(json);

            Assert.IsNotNull(response);
            Assert.IsNotNull(response!.Edit!.Changes);
            Assert.AreEqual(0, response.Edit.Changes!.Count);
        }
    }
}
