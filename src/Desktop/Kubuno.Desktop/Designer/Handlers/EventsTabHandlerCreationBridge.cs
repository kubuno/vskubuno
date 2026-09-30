using System;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.VisualStudio.Designer.Properties;
using Kubuno.VisualStudio.Views.Logging;

namespace Kubuno.VisualStudio.Designer.Handlers
{
    /// <summary>
    /// The one line of wiring docs/DESIGNER.md §1 asks for: "double-click on an empty row to generate a
    /// new handler". Subscribes to <see cref="PropertiesPanelViewModel.CreateHandlerRequested"/> (raised
    /// by <see cref="EventRowViewModel.RequestCreateHandler"/> - a double-click on the Events tab row, or
    /// its "Create handler" button) and turns it into one <see cref="HandlerCreationService.CreateAsync"/>
    /// call, using the <see cref="EventRowViewModel.DocumentUri"/>/<see cref="EventRowViewModel.ElementId"/>
    /// the "Properties/, minimal change" this package's brief calls for now carries on every row
    /// (see that view-model's own updated doc comment).
    ///
    /// A thin, fire-and-forget adapter - the two collaborators it wires together
    /// (<see cref="HandlerCreationService"/>, <see cref="PropertiesPanelViewModel"/>) are each already
    /// independently unit-tested; this class only forwards one event to one call, so its own coverage is
    /// the constructor's null-guards and the forwarding itself (see
    /// tests/Kubuno.VisualStudio.Designer.Tests/Handlers/EventsTabHandlerCreationBridgeTests.cs).
    /// Errors are logged (<see cref="KubunoViewsLogHost"/>, the same gateway
    /// <see cref="Kubuno.VisualStudio.Views.LanguageService.KubunoViewsLanguageClient"/> already logs
    /// through - this library may reference it, see its own csproj comment) rather than thrown back into
    /// the WPF event-raising call stack, which a `void`
    /// <see cref="PropertiesPanelViewModel.CreateHandlerRequested"/> handler could not propagate usefully
    /// anyway. Surfacing a user-visible failure (an info bar, a status-bar message) is the VSIX
    /// integration step's own concern, same as every other "later step" INTEGRATION.md already calls out
    /// for this library.
    /// </summary>
    public sealed class EventsTabHandlerCreationBridge
    {
        private readonly HandlerCreationService _service;

        public EventsTabHandlerCreationBridge(HandlerCreationService service, PropertiesPanelViewModel viewModel)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            if (viewModel is null)
            {
                throw new ArgumentNullException(nameof(viewModel));
            }

            viewModel.CreateHandlerRequested += OnCreateHandlerRequested;
        }

        private void OnCreateHandlerRequested(object? sender, CreateHandlerRequestedEventArgs e)
        {
            var row = e.Row;
            var request = new CreateHandlerRequest(row.DocumentUri, row.ElementId, row.Event.Name);
            _ = HandleAsync(request);
        }

        private async Task HandleAsync(CreateHandlerRequest request)
        {
            try
            {
                var result = await _service.CreateAsync(request, CancellationToken.None);
                if (result.Outcome is HandlerCreationOutcome.RequestFailed or HandlerCreationOutcome.ApplyFailed)
                {
                    KubunoViewsLogHost.Current.WriteLine(
                        $"kubuno/createHandler for '{request.EventName}' on element '{request.ElementId}' did not complete: {result.Outcome} ({result.FailedFile}).");
                }
            }
            catch (Exception exception)
            {
                KubunoViewsLogHost.Current.WriteException("kubuno/createHandler request failed", exception);
            }
        }
    }
}
