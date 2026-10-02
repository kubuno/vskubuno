using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Views.Designer.Editing;
using Kubuno.Views.Designer.Handlers;
using Kubuno.Views.Tests.Designer.Handlers.Fakes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Views.Tests.Designer.Handlers
{
    [TestClass]
    public class HandlerCreationServiceTests
    {
        private const string KbviewUri = "file:///view.kbview";
        private const string RsUri = "file:///view.rs";

        private static CreateHandlerRequest Request() => new CreateHandlerRequest(KbviewUri, "0", "OnClick");

        private static TextEditDto Edit(int line, int character, string newText) =>
            new TextEditDto(new LspRange(new LspPosition(line, character), new LspPosition(line, character)), newText);

        [TestMethod]
        public async Task RequestFailed_WhenClientReturnsNull()
        {
            var client = new FakeKubunoViewsLanguageServerClient { Response = null };
            var host = new FakeHandlerDocumentHost();
            var service = new HandlerCreationService(client, host);

            var result = await service.CreateAsync(Request(), CancellationToken.None);

            Assert.AreEqual(HandlerCreationOutcome.RequestFailed, result.Outcome);
            Assert.AreEqual(0, host.OpenedOrder.Count);
            Assert.AreEqual(0, host.Navigations.Count);
        }

        [TestMethod]
        public async Task OpenedExisting_NavigatesToLocation_NeverTouchesABuffer()
        {
            var location = new HandlerLocation(RsUri, new LspRange(new LspPosition(3, 0), new LspPosition(3, 10)));
            var client = new FakeKubunoViewsLanguageServerClient
            {
                Response = new CreateHandlerResponse("offline_toggled", location, edit: null),
            };
            var host = new FakeHandlerDocumentHost();
            var service = new HandlerCreationService(client, host);

            var result = await service.CreateAsync(Request(), CancellationToken.None);

            Assert.AreEqual(HandlerCreationOutcome.OpenedExisting, result.Outcome);
            Assert.AreEqual("offline_toggled", result.HandlerName);
            Assert.AreEqual(0, host.OpenedOrder.Count, "an already-wired handler must never apply an edit");
            CollectionAssert.AreEqual(new[] { (RsUri, new LspPosition(3, 0)) }, host.Navigations);
        }

        [TestMethod]
        public async Task NothingToDo_WhenResponseHasNeitherLocationNorEdit()
        {
            var client = new FakeKubunoViewsLanguageServerClient
            {
                Response = new CreateHandlerResponse(string.Empty, location: null, edit: null),
            };
            var service = new HandlerCreationService(client, new FakeHandlerDocumentHost());

            var result = await service.CreateAsync(Request(), CancellationToken.None);

            Assert.AreEqual(HandlerCreationOutcome.NothingToDo, result.Outcome);
        }

        [TestMethod]
        public async Task Created_AppliesEachFilesEditsAsOneBufferEditAndNavigatesToTheNewFnStub()
        {
            var changes = new Dictionary<string, IReadOnlyList<TextEditDto>>
            {
                [KbviewUri] = new List<TextEditDto> { Edit(0, 7, " OnClick=\"on_button_click\"") },
                [RsUri] = new List<TextEditDto>
                {
                    Edit(2, 0, "fn on_button_click(vm: &mut dyn kubuno_views::binding::ViewModel, value: kubuno_views::binding::Value) {\n    // TODO\n}\n\n"),
                    Edit(4, 4, "\"on_button_click\" => |vm, value| on_button_click(vm, value),\n"),
                },
            };
            var client = new FakeKubunoViewsLanguageServerClient
            {
                Response = new CreateHandlerResponse("on_button_click", location: null, new HandlerWorkspaceEdit(changes)),
            };
            var host = new FakeHandlerDocumentHost();
            var service = new HandlerCreationService(client, host);

            var result = await service.CreateAsync(Request(), CancellationToken.None);

            Assert.AreEqual(HandlerCreationOutcome.Created, result.Outcome);
            Assert.AreEqual("on_button_click", result.HandlerName);

            Assert.AreEqual(1, host.Buffers[KbviewUri].ApplyEditsCallCount, "one undo unit for the .kbview file");
            Assert.AreEqual(1, host.Buffers[RsUri].ApplyEditsCallCount, "ONE undo unit for the code-behind file, covering both its edits");
            StringAssert.Contains(host.Buffers[KbviewUri].GetCurrentText(), "OnClick=\"on_button_click\"");
            StringAssert.Contains(host.Buffers[RsUri].GetCurrentText(), "fn on_button_click(");
            StringAssert.Contains(host.Buffers[RsUri].GetCurrentText(), "\"on_button_click\" => |vm, value| on_button_click(vm, value),");

            CollectionAssert.AreEqual(new[] { (RsUri, new LspPosition(2, 0)) }, host.Navigations, "opens the code-behind at the new fn stub, not the .kbview file");
        }

        /// <summary>
        /// EVT-4: a typed stub is an indented method after a blank line, inserted together with a
        /// <c>use kubuno_views::prelude::*;</c> line higher in the file - the caret still lands on its <c>fn</c>.
        /// </summary>
        [TestMethod]
        public async Task Created_TypedStub_NavigatesToTheMethodPastTheAddedImport()
        {
            var changes = new Dictionary<string, IReadOnlyList<TextEditDto>>
            {
                [KbviewUri] = new List<TextEditDto> { Edit(0, 7, " OnClick=\"on_ok_click\"") },
                [RsUri] = new List<TextEditDto>
                {
                    Edit(12, 0, "\n    fn on_ok_click(&mut self, sender: &Sender<Button>, e: &MouseEventArgs) {\n        // TODO: implement on_ok_click\n    }\n"),
                    Edit(2, 0, "use kubuno_views::prelude::*;\n"),
                },
            };
            var client = new FakeKubunoViewsLanguageServerClient
            {
                Response = new CreateHandlerResponse("on_ok_click", location: null, new HandlerWorkspaceEdit(changes)),
            };
            var host = new FakeHandlerDocumentHost();

            var result = await new HandlerCreationService(client, host).CreateAsync(Request(), CancellationToken.None);

            Assert.AreEqual(HandlerCreationOutcome.Created, result.Outcome);
            CollectionAssert.AreEqual(new[] { (RsUri, new LspPosition(14, 4)) }, host.Navigations);
        }

        [TestMethod]
        public async Task Created_AppliesFilesInDeterministicOrdinalUriOrder()
        {
            var changes = new Dictionary<string, IReadOnlyList<TextEditDto>>
            {
                ["file:///z_view.kbview"] = new List<TextEditDto> { Edit(0, 0, "z") },
                ["file:///a_view.rs"] = new List<TextEditDto> { Edit(0, 0, "fn h(") },
            };
            var client = new FakeKubunoViewsLanguageServerClient
            {
                Response = new CreateHandlerResponse("h", location: null, new HandlerWorkspaceEdit(changes)),
            };
            var host = new FakeHandlerDocumentHost();
            var service = new HandlerCreationService(client, host);

            await service.CreateAsync(Request(), CancellationToken.None);

            CollectionAssert.AreEqual(new[] { "file:///a_view.rs", "file:///z_view.kbview" }, host.OpenedOrder);
        }

        [TestMethod]
        public async Task ApplyFailed_WhenABufferVersionDriftsBetweenReadAndApply()
        {
            var changes = new Dictionary<string, IReadOnlyList<TextEditDto>>
            {
                [RsUri] = new List<TextEditDto> { Edit(0, 0, "fn h(") },
            };
            var client = new FakeKubunoViewsLanguageServerClient
            {
                Response = new CreateHandlerResponse("h", location: null, new HandlerWorkspaceEdit(changes)),
            };
            var drifting = new VersionDriftingBuffer();
            var driftingHost = new DelegatingDocumentHost(_ => drifting, new List<(string, LspPosition)>());
            var service = new HandlerCreationService(client, driftingHost);

            var result = await service.CreateAsync(Request(), CancellationToken.None);

            Assert.AreEqual(HandlerCreationOutcome.ApplyFailed, result.Outcome);
            Assert.AreEqual(RsUri, result.FailedFile);
            Assert.AreEqual(BufferEditOutcome.VersionMismatch, result.FailedOutcome);
        }

        [TestMethod]
        public void Constructor_RejectsNullCollaborators()
        {
            Assert.ThrowsExactly<ArgumentNullException>(() => new HandlerCreationService(null!, new FakeHandlerDocumentHost()));
            Assert.ThrowsExactly<ArgumentNullException>(() => new HandlerCreationService(new FakeKubunoViewsLanguageServerClient(), null!));
        }

        /// <summary>A buffer whose <see cref="CurrentVersion"/> increments on every read - simulates a concurrent edit landing between <see cref="HandlerCreationService"/> capturing the version and <see cref="BufferEditCore"/> re-checking it, without needing a second thread.</summary>
        private sealed class VersionDriftingBuffer : IEditableTextBuffer
        {
            private int _version;

            public int CurrentVersion => _version++;

            public string GetCurrentText() => string.Empty;

            public void ApplyEdits(IReadOnlyList<PlannedTextEdit> edits)
            {
            }
        }

        private sealed class DelegatingDocumentHost : IHandlerDocumentHost
        {
            private readonly Func<string, IEditableTextBuffer> _openBuffer;
            private readonly List<(string Uri, LspPosition Position)> _navigations;

            public DelegatingDocumentHost(Func<string, IEditableTextBuffer> openBuffer, List<(string, LspPosition)> navigations)
            {
                _openBuffer = openBuffer;
                _navigations = navigations;
            }

            public IEditableTextBuffer OpenBuffer(string fileUri) => _openBuffer(fileUri);

            public void NavigateTo(string fileUri, LspPosition position) => _navigations.Add((fileUri, position));
        }
    }
}
