using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Mcp.Bridge;
using Kubuno.Mcp.Bridge.Contracts;
using Kubuno.Mcp.Bridge.PipeProtocol;
using Kubuno.Mcp.Tests.Fakes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Mcp.Tests
{
    /// <summary>
    /// <see cref="BridgeDispatcher"/> against a <see cref="FakeVsContextProvider"/>, with no pipe
    /// involved: verifies every bridge method name routes to the right provider call and that its
    /// result round-trips through JSON correctly.
    /// </summary>
    [TestClass]
    public sealed class BridgeDispatcherTests
    {
        private readonly BridgeDispatcher _dispatcher = new BridgeDispatcher(new FakeVsContextProvider());

        [TestMethod]
        public async Task Dispatch_ActiveDocument_ReturnsProviderData()
        {
            BridgeResponse response = await _dispatcher.DispatchAsync(
                new BridgeRequest { Id = "1", Method = BridgeMethods.ActiveDocument }, CancellationToken.None);

            Assert.IsTrue(response.Success);
            ActiveDocumentInfo? info = response.Result!.Value.Deserialize<ActiveDocumentInfo>();
            Assert.IsNotNull(info);
            Assert.AreEqual(FakeVsContextProvider.DocumentPath, info!.Path);
            Assert.AreEqual(FakeVsContextProvider.DocumentText, info.Text);
        }

        [TestMethod]
        public async Task Dispatch_ActiveDocument_ForwardsLineRangeParams()
        {
            var parameters = new ActiveDocumentParams { StartLine = 2, EndLine = 3 };
            var request = new BridgeRequest
            {
                Id = "1",
                Method = BridgeMethods.ActiveDocument,
                Params = JsonSerializer.SerializeToElement(parameters),
            };

            BridgeResponse response = await _dispatcher.DispatchAsync(request, CancellationToken.None);

            ActiveDocumentInfo? info = response.Result!.Value.Deserialize<ActiveDocumentInfo>();
            Assert.AreEqual(2, info!.StartLine);
            Assert.AreEqual(3, info.EndLine);
            Assert.AreEqual("lines 2-3", info.Text);
        }

        [TestMethod]
        public async Task Dispatch_Selection_ReturnsProviderData()
        {
            BridgeResponse response = await _dispatcher.DispatchAsync(
                new BridgeRequest { Id = "1", Method = BridgeMethods.Selection }, CancellationToken.None);

            SelectionInfo? info = response.Result!.Value.Deserialize<SelectionInfo>();
            Assert.IsTrue(response.Success);
            Assert.AreEqual("println!(\"hi\");", info!.Text);
        }

        [TestMethod]
        public async Task Dispatch_ErrorList_HonorsMaxItems()
        {
            var request = new BridgeRequest
            {
                Id = "1",
                Method = BridgeMethods.ErrorList,
                Params = JsonSerializer.SerializeToElement(new ErrorListParams { MaxItems = 1 }),
            };

            BridgeResponse response = await _dispatcher.DispatchAsync(request, CancellationToken.None);

            ErrorListInfo? info = response.Result!.Value.Deserialize<ErrorListInfo>();
            Assert.AreEqual(2, info!.TotalCount);
            Assert.AreEqual(1, info.Items.Count);
            Assert.AreEqual("Error", info.Items[0].Severity);
        }

        [TestMethod]
        public async Task Dispatch_OpenDocuments_ReturnsProviderData()
        {
            BridgeResponse response = await _dispatcher.DispatchAsync(
                new BridgeRequest { Id = "1", Method = BridgeMethods.OpenDocuments }, CancellationToken.None);

            OpenDocumentsInfo? info = response.Result!.Value.Deserialize<OpenDocumentsInfo>();
            Assert.AreEqual(2, info!.Documents.Count);
        }

        [TestMethod]
        public async Task Dispatch_SolutionOrFolder_ReturnsProviderData()
        {
            BridgeResponse response = await _dispatcher.DispatchAsync(
                new BridgeRequest { Id = "1", Method = BridgeMethods.SolutionOrFolder }, CancellationToken.None);

            SolutionOrFolderInfo? info = response.Result!.Value.Deserialize<SolutionOrFolderInfo>();
            Assert.AreEqual("Folder", info!.Kind);
            Assert.AreEqual(2, info.CargoWorkspaceRoots.Count);
        }

        [TestMethod]
        public async Task Dispatch_DebuggerState_ReturnsProviderData()
        {
            BridgeResponse response = await _dispatcher.DispatchAsync(
                new BridgeRequest { Id = "1", Method = BridgeMethods.DebuggerState }, CancellationToken.None);

            DebuggerStateInfo? info = response.Result!.Value.Deserialize<DebuggerStateInfo>();
            Assert.AreEqual("Break", info!.Mode);
            Assert.AreEqual(2, info.StackFrames.Count);
            Assert.IsNotNull(info.CurrentFrameLocals);
        }

        [TestMethod]
        public async Task Dispatch_OutputPane_ForwardsPaneNameParam()
        {
            var request = new BridgeRequest
            {
                Id = "1",
                Method = BridgeMethods.OutputPane,
                Params = JsonSerializer.SerializeToElement(new OutputPaneParams { PaneName = "Kubuno" }),
            };

            BridgeResponse response = await _dispatcher.DispatchAsync(request, CancellationToken.None);

            OutputPaneInfo? info = response.Result!.Value.Deserialize<OutputPaneInfo>();
            Assert.AreEqual("Kubuno", info!.PaneName);
            Assert.IsTrue(info.Found);
        }

        [TestMethod]
        public async Task Dispatch_UnknownMethod_FailsWithClearError()
        {
            BridgeResponse response = await _dispatcher.DispatchAsync(
                new BridgeRequest { Id = "1", Method = "vs_does_not_exist" }, CancellationToken.None);

            Assert.IsFalse(response.Success);
            Assert.AreEqual("unknown_method", response.Error!.Code);
            StringAssert.Contains(response.Error.Message, "vs_does_not_exist");
        }

        [TestMethod]
        public async Task Dispatch_ProviderException_BecomesErrorResponse_NotAnUnhandledThrow()
        {
            var throwingProvider = new ThrowingVsContextProvider();
            var dispatcher = new BridgeDispatcher(throwingProvider);

            BridgeResponse response = await dispatcher.DispatchAsync(
                new BridgeRequest { Id = "1", Method = BridgeMethods.Selection }, CancellationToken.None);

            Assert.IsFalse(response.Success);
            Assert.AreEqual("provider_error", response.Error!.Code);
            Assert.AreEqual("boom", response.Error.Message);
        }

        private sealed class ThrowingVsContextProvider : IVsContextProvider
        {
            public Task<ActiveDocumentInfo> GetActiveDocumentAsync(ActiveDocumentParams parameters, CancellationToken cancellationToken) =>
                throw new System.InvalidOperationException("boom");

            public Task<SelectionInfo> GetSelectionAsync(CancellationToken cancellationToken) =>
                throw new System.InvalidOperationException("boom");

            public Task<ErrorListInfo> GetErrorListAsync(ErrorListParams parameters, CancellationToken cancellationToken) =>
                throw new System.InvalidOperationException("boom");

            public Task<OpenDocumentsInfo> GetOpenDocumentsAsync(CancellationToken cancellationToken) =>
                throw new System.InvalidOperationException("boom");

            public Task<SolutionOrFolderInfo> GetSolutionOrFolderAsync(CancellationToken cancellationToken) =>
                throw new System.InvalidOperationException("boom");

            public Task<DebuggerStateInfo> GetDebuggerStateAsync(CancellationToken cancellationToken) =>
                throw new System.InvalidOperationException("boom");

            public Task<OutputPaneInfo> GetOutputPaneAsync(OutputPaneParams parameters, CancellationToken cancellationToken) =>
                throw new System.InvalidOperationException("boom");
        }
    }
}
