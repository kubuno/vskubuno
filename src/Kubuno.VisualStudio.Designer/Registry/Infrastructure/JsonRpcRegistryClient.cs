using System;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.VisualStudio.Views.Logging;
using Newtonsoft.Json.Linq;
using StreamJsonRpc;

namespace Kubuno.VisualStudio.Designer.Registry.Infrastructure
{
    /// <summary>
    /// The real <c>kubuno/registry</c> caller (docs/DESIGNER.md §5), over the same
    /// <c>StreamJsonRpc.JsonRpc</c> object <c>KubunoViewsLanguageClient.Rpc</c> exposes once
    /// <c>AttachForCustomMessageAsync</c> has run - the same shape as
    /// <c>Handlers.Infrastructure.JsonRpcKubunoViewsLanguageServerClient</c> (see that class's own doc
    /// comment for why the result is read as a weakly-typed <see cref="JToken"/> rather than a
    /// strongly-typed System.Text.Json DTO). The wire result is <c>{ "version": "...", "components":
    /// [...] }</c> (`kubuno_views::registry::export::RegistryExport`) - NOT the bare array
    /// <see cref="ComponentRegistry.FromJson"/> itself deserializes (that method's own doc: "the bare
    /// JSON array"), so this class is the one place that unwraps the <c>components</c> field before
    /// handing the array text to it. <see cref="ComponentRegistry.Empty"/> on any failure (server
    /// unreachable, no <c>kubuno/registry</c> method yet, malformed response) - a caller feeding the
    /// Toolbox/Properties tool windows must treat "no components yet" as a normal, transient state
    /// (INTEGRATION.md §7 point 2: "ComponentRegistry.Empty is a safe placeholder").
    /// </summary>
    public static class JsonRpcRegistryClient
    {
        private const string MethodName = "kubuno/registry";

        public static async Task<ComponentRegistry> FetchAsync(JsonRpc? rpc, CancellationToken cancellationToken)
        {
            if (rpc is null)
            {
                return ComponentRegistry.Empty;
            }

            try
            {
                var result = await rpc.InvokeWithParameterObjectAsync<JToken?>(MethodName, new { }, cancellationToken).ConfigureAwait(false);
                var componentsToken = result?["components"];
                if (componentsToken is null)
                {
                    return ComponentRegistry.Empty;
                }

                return ComponentRegistry.FromJson(componentsToken.ToString(Newtonsoft.Json.Formatting.None));
            }
            catch (Exception ex) when (ex is RemoteInvocationException or ConnectionLostException or OperationCanceledException)
            {
                KubunoViewsLogHost.Current.WriteException("kubuno/registry RPC call failed", ex);
                return ComponentRegistry.Empty;
            }
        }
    }
}
