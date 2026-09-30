using System;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Desktop.Designer.Handlers;
using Kubuno.Desktop.Designer.Properties;
using Kubuno.Desktop.Designer.Registry;
using Kubuno.Desktop.Tests.Designer.Handlers.Fakes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Desktop.Tests.Designer.Handlers
{
    [TestClass]
    public class EventsTabHandlerCreationBridgeTests
    {
        private static PropertiesPanelViewModel SelectedButton()
        {
            var viewModel = new PropertiesPanelViewModel();
            var component = new ComponentMeta
            {
                Name = "Button",
                Family = "core",
                Children = ChildrenModel.None,
                Events = new System.Collections.Generic.List<EventMeta> { new EventMeta { Name = "OnClick" } },
            };
            viewModel.SetSelection(
                component,
                new System.Collections.Generic.Dictionary<string, string?>(),
                new System.Collections.Generic.Dictionary<string, string?>(),
                Array.Empty<string>(),
                "file:///view.kbview",
                "0");
            return viewModel;
        }

        [TestMethod]
        public void Constructor_RejectsNullCollaborators()
        {
            var service = new HandlerCreationService(new FakeKubunoViewsLanguageServerClient(), new FakeHandlerDocumentHost());
            Assert.ThrowsExactly<ArgumentNullException>(() => new EventsTabHandlerCreationBridge(null!, SelectedButton()));
            Assert.ThrowsExactly<ArgumentNullException>(() => new EventsTabHandlerCreationBridge(service, null!));
        }

        [TestMethod]
        public async Task RequestCreateHandler_ForwardsRowIdentityToTheService()
        {
            var client = new FakeKubunoViewsLanguageServerClient
            {
                Response = new CreateHandlerResponse("on_button_click", location: null, edit: null),
            };
            var host = new FakeHandlerDocumentHost();
            var service = new HandlerCreationService(client, host);
            var viewModel = SelectedButton();
            _ = new EventsTabHandlerCreationBridge(service, viewModel);

            viewModel.Events[0].RequestCreateHandler();

            // The bridge dispatches fire-and-forget; give the scheduled continuation a chance to run
            // (an in-process, already-completed Task.FromResult resolves synchronously enough in
            // practice, but polling briefly keeps this robust against scheduler timing).
            for (var i = 0; i < 50 && client.CallCount == 0; i++)
            {
                await Task.Delay(10);
            }

            Assert.AreEqual(1, client.CallCount);
            Assert.IsNotNull(client.LastRequest);
            Assert.AreEqual("file:///view.kbview", client.LastRequest!.KbviewUri);
            Assert.AreEqual("0", client.LastRequest.ElementId);
            Assert.AreEqual("OnClick", client.LastRequest.EventName);
        }

        [TestMethod]
        public async Task RequestCreateHandler_WhenServiceThrows_DoesNotPropagateToTheCaller()
        {
            var client = new FakeKubunoViewsLanguageServerClient { ThrowOnCall = new InvalidOperationException("boom") };
            var host = new FakeHandlerDocumentHost();
            var service = new HandlerCreationService(client, host);
            var viewModel = SelectedButton();
            _ = new EventsTabHandlerCreationBridge(service, viewModel);

            // Must not throw synchronously out of the event-raising call.
            viewModel.Events[0].RequestCreateHandler();

            for (var i = 0; i < 50 && client.CallCount == 0; i++)
            {
                await Task.Delay(10);
            }

            Assert.AreEqual(1, client.CallCount);
        }
    }
}
