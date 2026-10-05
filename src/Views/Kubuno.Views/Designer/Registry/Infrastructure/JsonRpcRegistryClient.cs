using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Views.Logging;
using Newtonsoft.Json.Linq;
using StreamJsonRpc;

namespace Kubuno.Views.Designer.Registry.Infrastructure
{
    /// <summary>
    /// The real <c>kubuno/registry</c> caller (docs/DESIGNER.md §5), over the same
    /// <c>StreamJsonRpc.JsonRpc</c> object <c>KubunoViewsLanguageClient.Rpc</c> exposes once
    /// <c>AttachForCustomMessageAsync</c> has run - the same shape as
    /// <c>Handlers.Infrastructure.JsonRpcKubunoViewsLanguageServerClient</c> (see that class's own doc
    /// comment for why the result is read as a weakly-typed <see cref="JToken"/> rather than a
    /// strongly-typed System.Text.Json DTO). The wire result is <c>{ "version": "...", "components":
    /// [...] }</c> (`kubuno_desktop_views::registry::export::RegistryExport`) - NOT the bare array
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

        /// <summary><c>kubuno/registryVersion</c> (EVT-7b): the registry's version only, polled to notice the project's controls changing; null on failure.</summary>
        /// <remarks><paramref name="documentUri"/>: the view the registry is for - the language server answers with its target's registry (desktop, or the web registry of a web project, WV-7); null: the desktop registry.</remarks>
        public static async Task<string?> FetchVersionAsync(JsonRpc? rpc, CancellationToken cancellationToken, string? documentUri = null)
        {
            if (rpc is null)
            {
                return null;
            }

            try
            {
                var result = await rpc.InvokeWithParameterObjectAsync<JToken?>("kubuno/registryVersion", Params(documentUri), cancellationToken).ConfigureAwait(false);
                return result?["version"]?.ToString();
            }
            catch (Exception ex) when (ex is RemoteInvocationException or ConnectionLostException or OperationCanceledException or ObjectDisposedException)
            {
                return null;
            }
        }

        private static object Params(string? documentUri) => documentUri is null ? new { } : new { uri = documentUri };

        /// <remarks><paramref name="documentUri"/>: see <see cref="FetchVersionAsync"/>; the result's <see cref="ComponentRegistry.Target"/> says which registry came back.</remarks>
        public static async Task<ComponentRegistry> FetchAsync(JsonRpc? rpc, CancellationToken cancellationToken, string? documentUri = null)
        {
            if (rpc is null)
            {
                return ComponentRegistry.Empty;
            }

            try
            {
                var result = await rpc.InvokeWithParameterObjectAsync<JToken?>(MethodName, Params(documentUri), cancellationToken).ConfigureAwait(false);
                var componentsToken = result?["components"];
                if (componentsToken is null)
                {
                    return ComponentRegistry.Empty;
                }

                var registry = ComponentRegistry.FromJson(componentsToken.ToString(Newtonsoft.Json.Formatting.None));
                registry.Version = result?["version"]?.ToString();
                registry.Target = result?["target"]?.Type == JTokenType.String ? (string?)result["target"] : null;
                // EVT-7b: the project's own controls, as exported - what the design surface registers as placeholders.
                registry.ProjectComponentsJson = new JArray(componentsToken.Where(c => (string?)c["origin"] == "project")).ToString(Newtonsoft.Json.Formatting.None);
                return registry;
            }
            catch (Exception ex) when (ex is RemoteInvocationException or ConnectionLostException or OperationCanceledException)
            {
                KubunoViewsLogHost.Current.WriteException("kubuno/registry RPC call failed", ex);
                return ComponentRegistry.Empty;
            }
        }
    }
}
