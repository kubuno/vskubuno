using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using StreamJsonRpc;

namespace Kubuno.Desktop.Designer.Handlers.Infrastructure
{
    /// <summary>
    /// The real <c>kubuno/createHandler</c> caller, over the same <c>StreamJsonRpc.JsonRpc</c> object
    /// <c>Kubuno.Desktop.Views.LanguageService.KubunoViewsLanguageClient.Rpc</c> already exposes
    /// once <c>AttachForCustomMessageAsync</c> runs (docs/DESIGNER.md §3: "the exact hook custom,
    /// non-textDocument/* methods need"; §8 point 2's own note that a later integration step hands this
    /// class that live object - this library has no dependency of its own on <c>Kubuno.Desktop.Views</c>
    /// beyond what <see cref="Kubuno.Desktop.Designer.Editing"/>/this project's csproj comment
    /// already explains, so the caller passes the <see cref="JsonRpc"/> in rather than this class
    /// resolving it itself).
    ///
    /// Not unit-tested here - it needs a live <c>JsonRpc</c> connected to a running <c>kubuno-views-ls</c>
    /// process, the same reasoning <see cref="Editing.Infrastructure.BufferEditApplier"/>'s own doc
    /// comment gives for staying out of tests/Kubuno.Desktop.Tests/Designer; a manual round trip in
    /// the experimental instance is this class's test strategy, per docs/DESIGNER.md's DSG-10 row
    /// ("manual check for the VS-side open/insert/caret-jump"). See
    /// <see cref="CreateHandlerResponseParser"/> for the pure, unit-tested half of this bridge (JSON -&gt;
    /// DTO), which this class delegates to rather than deserializing the RPC result itself.
    ///
    /// Result deserialization goes through a <c>Newtonsoft.Json.Linq.JToken</c> on purpose: the
    /// <c>StreamJsonRpc.JsonRpc</c> a <c>Microsoft.VisualStudio.LanguageServer.Client.Connection</c>
    /// builds internally uses Newtonsoft.Json's own message formatter (the VS SDK's own convention for
    /// its LSP client infrastructure - .NET Framework 4.8 ships Newtonsoft.Json but not
    /// System.Text.Json, see `Kubuno.VisualStudio.csproj`'s own comment on exactly that point), so
    /// requesting a strongly-typed result here would bind against THAT formatter's rules, not this
    /// project's own <c>System.Text.Json</c>-based DTOs. Asking for the weakly-typed <see cref="JToken"/>
    /// instead and re-serializing it to a plain JSON string sidesteps that entirely: whichever formatter
    /// produced the wire bytes, the string this class hands to <see cref="CreateHandlerResponseParser"/>
    /// is the same `camelCase` JSON `kubuno-views-ls` actually sent, parsed exactly once, by this
    /// library's own (fully tested) rules. **Unverified without a live VS instance** - if the real
    /// formatter ever turns out to be System.Text.Json-based instead, <see cref="InvokeWithParameterObjectAsync"/>'s
    /// type argument here is the only line that would need to change.
    /// </summary>
    public sealed class JsonRpcKubunoViewsLanguageServerClient : IKubunoViewsLanguageServerClient
    {
        private const string MethodName = "kubuno/createHandler";

        private readonly JsonRpc _rpc;

        public JsonRpcKubunoViewsLanguageServerClient(JsonRpc rpc)
        {
            _rpc = rpc ?? throw new ArgumentNullException(nameof(rpc));
        }

        public async Task<CreateHandlerResponse?> CreateHandlerAsync(CreateHandlerRequest request, CancellationToken cancellationToken)
        {
            if (request is null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            // A plain anonymous object with explicit camelCase property names: this sends exactly
            // `{uri, elementId, event, suggestedName}` on the wire regardless of whichever naming
            // policy the underlying formatter applies to other, differently-cased argument shapes -
            // `kubuno-views-ls`'s `handler_insert::CreateHandlerParams` (`#[serde(rename_all =
            // "camelCase")]`) expects exactly these keys.
            var argument = new
            {
                uri = request.KbviewUri,
                elementId = request.ElementId,
                @event = request.EventName,
                suggestedName = request.SuggestedName,
                openFiles = request.OpenFiles,
            };

            JToken? token;
            try
            {
                token = await _rpc.InvokeWithParameterObjectAsync<JToken?>(MethodName, argument, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is RemoteInvocationException or ConnectionLostException or OperationCanceledException)
            {
                return null;
            }

            if (token is null)
            {
                return null;
            }

            return CreateHandlerResponseParser.Parse(token.ToString(Newtonsoft.Json.Formatting.None));
        }
    }
}
