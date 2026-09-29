using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Kubuno.VisualStudio.Designer.Editing;
using Kubuno.VisualStudio.Designer.Handlers;
using Kubuno.VisualStudio.Designer.Tests.Editing.Fakes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.VisualStudio.Designer.Tests.Handlers
{
    /// <summary>docs/EVENTS.md §5.3/§5.5 (EVT-5): the handler commands' answers, applying their edits, and a Rust-editor rename.</summary>
    [TestClass]
    public class HandlerCommandsTests
    {
        private const string ViewUri = "file:///C:/app/src/main_view.kbview";
        private const string CodeUri = "file:///C:/app/src/main_view.rs";

        [TestMethod]
        public void CompatibleHandlers_AreParsed()
        {
            CollectionAssert.AreEqual(new[] { "on_ok_click", "any" }, HandlerCommandResponseParser.ParseHandlerNames(@"{""handlers"":[""on_ok_click"",""any"",""""]}").ToArray());
            Assert.AreEqual(0, HandlerCommandResponseParser.ParseHandlerNames("null").Count);
            Assert.AreEqual(0, HandlerCommandResponseParser.ParseHandlerNames("not json").Count);
        }

        [TestMethod]
        public void RenameRemoveAndConvertAnswers_AreParsed()
        {
            var rename = HandlerCommandResponseParser.ParseEditResponse(
                @"{""edit"":{""changes"":{""" + ViewUri + @""":[{""range"":{""start"":{""line"":1,""character"":33},""end"":{""line"":1,""character"":44}},""newText"":""save""}]}},""oldName"":""on_ok_click"",""reason"":null}")!;
            Assert.AreEqual("on_ok_click", rename.HandlerName);
            Assert.AreEqual("save", rename.Edit!.Changes![ViewUri][0].NewText);
            Assert.AreEqual(new LspPosition(1, 33), rename.Edit.Changes[ViewUri][0].Range.Start);

            var refused = HandlerCommandResponseParser.ParseEditResponse(@"{""edit"":null,""oldName"":null,""reason"":""the code-behind already has a `any`""}")!;
            Assert.IsNull(refused.Edit);
            StringAssert.Contains(refused.Reason, "already has");

            var removed = HandlerCommandResponseParser.ParseEditResponse(@"{""edit"":{""changes"":{}},""handlerName"":""go"",""removedStub"":true}")!;
            Assert.IsTrue(removed.RemovedStub);
            Assert.AreEqual("go", removed.HandlerName);

            var converted = HandlerCommandResponseParser.ParseEditResponse(@"{""edit"":null,""converted"":[""a"",""b""],""reason"":null}")!;
            CollectionAssert.AreEqual(new[] { "a", "b" }, converted.Converted.ToArray());
            Assert.IsNull(HandlerCommandResponseParser.ParseEditResponse("[]"));
        }

        [TestMethod]
        public void WorkspaceEdit_GoesToTheOpenBuffer_OneEditPerFile_OrToTheFileOnDisk()
        {
            var host = new FakeWorkspaceFileHost();
            var view = new FakeEditableTextBuffer("<Button OnClick=\"go\"/>");
            host.Open[ViewUri] = view;
            host.Disk[CodeUri] = "fn go() {}\n";
            var edit = new HandlerWorkspaceEdit(new Dictionary<string, IReadOnlyList<TextEditDto>>
            {
                [ViewUri] = new[] { Edit(0, 17, 0, 19, "run") },
                [CodeUri] = new[] { Edit(0, 3, 0, 5, "run") },
            });

            var result = WorkspaceEditApplier.Apply(edit, host);

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual("<Button OnClick=\"run\"/>", view.GetCurrentText());
            Assert.AreEqual(1, view.ApplyEditsCallCount, "one undo unit for the file");
            Assert.AreEqual("fn run() {}\n", host.Disk[CodeUri]);
        }

        [TestMethod]
        public void WorkspaceEdit_ThatDoesNotFit_AppliesNothing()
        {
            var host = new FakeWorkspaceFileHost();
            var view = new FakeEditableTextBuffer("<Button/>");
            host.Open[ViewUri] = view;
            host.Disk[CodeUri] = "fn go() {}\n";
            var edit = new HandlerWorkspaceEdit(new Dictionary<string, IReadOnlyList<TextEditDto>>
            {
                [CodeUri] = new[] { Edit(0, 3, 0, 5, "run"), Edit(0, 4, 0, 6, "x") },
                [ViewUri] = new[] { Edit(0, 7, 0, 7, " OnClick=\"run\"") },
            });

            var result = WorkspaceEditApplier.Apply(edit, host);

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(CodeUri, result.FailedFile);
            Assert.AreEqual("<Button/>", view.GetCurrentText(), "planned before anything is applied");
            Assert.AreEqual("fn go() {}\n", host.Disk[CodeUri]);
        }

        [TestMethod]
        public void RustAnalyzerRename_GetsTheViewEdits_InDocumentChangesOrChanges()
        {
            const string Kubuno = @"{""changes"":{""file:///C:/app/src/main_view.kbview"":[{""range"":{""start"":{""line"":1,""character"":33},""end"":{""line"":1,""character"":44}},""newText"":""save""}],""file:///c:/app/src/main_view.rs"":[{""range"":{""start"":{""line"":9,""character"":9},""end"":{""line"":9,""character"":11}},""newText"":""save""}]}}";
            const string DocumentChanges = @"{""documentChanges"":[{""textDocument"":{""uri"":""file:///c%3A/app/src/main_view.rs"",""version"":3},""edits"":[{""range"":{""start"":{""line"":6,""character"":7},""end"":{""line"":6,""character"":18}},""newText"":""save""}]}]}";

            var merged = JsonNode.Parse(RenameEditMerger.Merge(DocumentChanges, Kubuno))!;
            var documents = merged["documentChanges"]!.AsArray();
            Assert.AreEqual(2, documents.Count);
            Assert.AreEqual(2, documents[0]!["edits"]!.AsArray().Count, "the handlers! string joins rust-analyzer's edits of the same file");
            Assert.AreEqual("file:///C:/app/src/main_view.kbview", (string)documents[1]!["textDocument"]!["uri"]!);
            Assert.IsNull(documents[1]!["textDocument"]!["version"]);

            const string Changes = @"{""changes"":{""file:///c%3A/app/src/main_view.rs"":[{""range"":{""start"":{""line"":6,""character"":7},""end"":{""line"":6,""character"":18}},""newText"":""save""}]}}";
            var merged2 = JsonNode.Parse(RenameEditMerger.Merge(Changes, Kubuno))!["changes"]!.AsObject();
            Assert.AreEqual(2, merged2.Count);
            Assert.AreEqual(2, merged2["file:///c%3A/app/src/main_view.rs"]!.AsArray().Count);

            Assert.AreEqual(Changes, RenameEditMerger.Merge(Changes, null));
            Assert.AreEqual(Changes, RenameEditMerger.Merge(Changes, "garbage"));
        }

        private static TextEditDto Edit(int startLine, int startChar, int endLine, int endChar, string text) =>
            new TextEditDto(new LspRange(new LspPosition(startLine, startChar), new LspPosition(endLine, endChar)), text);

        private sealed class FakeWorkspaceFileHost : IWorkspaceFileHost
        {
            public Dictionary<string, FakeEditableTextBuffer> Open { get; } = new Dictionary<string, FakeEditableTextBuffer>();

            public Dictionary<string, string> Disk { get; } = new Dictionary<string, string>();

            public IEditableTextBuffer? TryGetOpenBuffer(string fileUri) => Open.TryGetValue(fileUri, out var buffer) ? buffer : null;

            public string? ReadFile(string fileUri) => Disk.TryGetValue(fileUri, out var text) ? text : null;

            public void WriteFile(string fileUri, string text) => Disk[fileUri] = text;
        }
    }
}
