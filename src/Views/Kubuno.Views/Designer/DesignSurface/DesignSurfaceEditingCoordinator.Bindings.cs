using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Kubuno.Desktop.Designer.Bindings;
using Kubuno.Desktop.Designer.Editing;
using Kubuno.Desktop.Designer.Handlers.Infrastructure;
using Kubuno.Desktop.Views.Logging;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json.Linq;
using StreamJsonRpc;

namespace Kubuno.Desktop.Designer.DesignSurface
{
    /// <summary>
    /// The data binding services of the Properties window (docs/DESIGNER.md, "Data bindings"): the source schema of an
    /// element's bindings and their problems (<c>kubuno/bindingSources</c>, cached per buffer version and element), the
    /// live sample of the « Liaison de données » dialog (<c>kubuno/bindingPreview</c>), and « Aller à la définition »
    /// (<c>kubuno/bindingDefinition</c>). Every call is synchronous - the grid asks while it paints or opens a drop-down -
    /// and bounded by a short timeout.
    /// </summary>
    internal sealed partial class DesignSurfaceEditingCoordinator
    {
        private static readonly TimeSpan BindingRequestTimeout = TimeSpan.FromSeconds(3);

        private readonly Dictionary<string, (int Version, BindingSourceSchema Schema)> _bindingSchemas = new Dictionary<string, (int, BindingSourceSchema)>(StringComparer.Ordinal);

        public BindingSourceSchema GetBindingSources(string elementId)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var version = CurrentVersion;
            if (_bindingSchemas.TryGetValue(elementId, out var cached) && cached.Version == version)
            {
                return cached.Schema;
            }

            var uri = TryGetDocumentUri();
            if (uri is null)
            {
                return BindingSourceSchema.Empty;
            }

            var openFiles = OpenCodeBehindTexts(uri);
            var token = Request("kubuno/bindingSources", new { uri, elementId, openFiles });
            var schema = BindingSourceSchema.Parse(token);
            if (token is not null)
            {
                if (_bindingSchemas.Count > 64)
                {
                    _bindingSchemas.Clear();
                }

                _bindingSchemas[elementId] = (version, schema);
            }

            return schema;
        }

        public BindingPreview PreviewBinding(string expression, string? sample, BindingShape sampleShape, BindingShape? want)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var token = Request("kubuno/bindingPreview", new { expression, value = sample, shape = sampleShape.ToString(), want = want?.ToString() });
            return token is JObject o ? new BindingPreview((string?)o["text"], (string?)o["note"]) : new BindingPreview(null, null);
        }

        public bool GoToBindingDefinition(string elementId, string attribute)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var uri = TryGetDocumentUri();
            if (uri is null || _oleServiceProvider is null)
            {
                return false;
            }

            var token = Request("kubuno/bindingDefinition", new { uri, elementId, attribute, openFiles = OpenCodeBehindTexts(uri) });
            if (token is not JObject location || (string?)location["uri"] is not { } target || location["range"]?["start"] is not JObject start)
            {
                return false;
            }

            new VsHandlerDocumentHost(new ServiceProvider(_oleServiceProvider)).NavigateTo(target, new LspPosition((int?)start["line"] ?? 0, (int?)start["character"] ?? 0));
            return true;
        }

        /// <summary>A custom request to the language server, null when it is not running, fails or does not answer in time.</summary>
        private JToken? Request(string method, object parameters)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var rpc = _resolveLanguageClient()?.ReadyRpc;
            if (rpc is null)
            {
                return null;
            }

#pragma warning disable VSTHRD104 // the Properties window asks synchronously; bounded by BindingRequestTimeout.
            return ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                var call = rpc.InvokeWithParameterObjectAsync<JToken?>(method, parameters);
                if (await Task.WhenAny(call, Task.Delay(BindingRequestTimeout)) != call)
                {
                    KubunoViewsLogHost.Current.WriteLine($"[designer] {method} did not answer in time.");
                    return null;
                }

                try
                {
                    return await call;
                }
                catch (Exception ex) when (ex is RemoteInvocationException or ConnectionLostException or OperationCanceledException)
                {
                    KubunoViewsLogHost.Current.WriteException($"[designer] {method} failed", ex);
                    return null;
                }
            });
#pragma warning restore VSTHRD104
        }
    }
}
