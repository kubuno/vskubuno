using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.VisualStudio.Core;
using Kubuno.VisualStudio.Designer.Handlers;
using Kubuno.VisualStudio.Designer.Handlers.Infrastructure;
using Kubuno.VisualStudio.Logging;
using Kubuno.VisualStudio.Views.LanguageService;
using Microsoft.VisualStudio.LanguageServer.Client;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using Newtonsoft.Json.Linq;
using StreamJsonRpc;

namespace Kubuno.VisualStudio.LanguageService
{
    /// <summary>
    /// <see cref="RustLanguageClient.MiddleLayer"/>: fixes stale rust-analyzer errors that stayed in the
    /// editor and the Error List after the code was fixed. See <see cref="PullDiagnosticsResultIds"/> for
    /// the cause (rust-analyzer's constant pull-diagnostics <c>resultId</c> + Visual Studio dropping an empty
    /// report whose id did not change). Every full <c>textDocument/diagnostic</c> report gets a fresh
    /// <c>resultId</c>, and the synthetic id Visual Studio sends back as <c>previousResultId</c> is removed
    /// from the next request, so rust-analyzer always computes (and returns) a full report.
    /// Also answers Visual Studio's own <c>textDocument/hover</c> with nothing: the tooltip comes from
    /// <see cref="QuickInfo.RustQuickInfoSourceProvider"/>.
    /// And completes rust-analyzer's <c>textDocument/rename</c> of a view handler (docs/EVENTS.md §5.5, EVT-5): the
    /// <c>.kbview</c> attributes naming it and its <c>handlers!</c> string are renamed with it (<c>kubuno/renameHandler</c>).
    /// </summary>
    internal sealed class RustAnalyzerMiddleLayer : ILanguageClientMiddleLayer2<JToken>
    {
        private const string DocumentDiagnosticMethod = "textDocument/diagnostic";
        private const string HoverMethod = "textDocument/hover";
        private const string RenameMethod = "textDocument/rename";

        private readonly PullDiagnosticsResultIds _resultIds = new();
        private int _loggedOnce;

        public bool CanHandle(string methodName) => methodName == DocumentDiagnosticMethod || methodName == HoverMethod || methodName == RenameMethod;

        public Task HandleNotificationAsync(string methodName, JToken methodParam, Func<JToken, Task> sendNotification) =>
            sendNotification(methodParam);

        public async Task<JToken?> HandleRequestAsync(string methodName, JToken methodParam, Func<JToken, Task<JToken?>> sendRequest)
        {
            if (methodName == RenameMethod)
            {
                return await HandleRenameAsync(methodParam, sendRequest).ConfigureAwait(false);
            }

            if (methodName == HoverMethod)
            {
                // Visual Studio's own hover would draw rust-analyzer's raw markdown; QuickInfo.RustQuickInfoSourceProvider
                // asks rust-analyzer itself (over the client's JsonRpc, which does not go through this layer) and
                // shows a C#-style tooltip instead.
                return null;
            }

            if (methodParam is JObject request)
            {
                request.Remove("previousResultId");
            }

            var response = await sendRequest(methodParam).ConfigureAwait(false);
            if (response is JObject report)
            {
                MakeResultIdUnique(report);
                if (report["relatedDocuments"] is JObject related)
                {
                    foreach (var property in related.Properties())
                    {
                        if (property.Value is JObject relatedReport)
                        {
                            MakeResultIdUnique(relatedReport);
                        }
                    }
                }
            }

            return response;
        }

        /// <summary>
        /// rust-analyzer renames the Rust side of a handler (the method, its calls); <c>kubuno-views-ls</c> adds the views'
        /// attributes and the <c>handlers!</c> string (<c>rustRenamed</c>: it leaves the Rust code alone), computed on the open
        /// editors' texts. Anything unexpected returns rust-analyzer's answer untouched.
        /// </summary>
        private static async Task<JToken?> HandleRenameAsync(JToken methodParam, Func<JToken, Task<JToken?>> sendRequest)
        {
            var response = await sendRequest(methodParam).ConfigureAwait(false);
            if (response is not JObject || KubunoViewsLanguageClient.Current?.ReadyRpc is not { } rpc)
            {
                return response;
            }

            try
            {
                var uri = (string?)methodParam["textDocument"]?["uri"];
                var position = methodParam["position"];
                var newName = (string?)methodParam["newName"];
                if (uri is null || position is null || newName is null || !uri.EndsWith(".rs", StringComparison.OrdinalIgnoreCase))
                {
                    return response;
                }

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                var folder = Path.GetDirectoryName(new Uri(Uri.UnescapeDataString(uri)).LocalPath) ?? string.Empty;
                var openFiles = new VsWorkspaceFileHost(ServiceProvider.GlobalProvider).OpenTexts(folder, ".rs");
                await TaskScheduler.Default;
                var result = await rpc.InvokeWithParameterObjectAsync<JToken?>("kubuno/renameHandler", new { uri, position, @new = newName, rustRenamed = true, openFiles }).ConfigureAwait(false);
                if (result?["edit"] is not JObject edit)
                {
                    return response;
                }

                KubunoLog.WriteLine($"Rename: handler '{(string?)result["oldName"]}' renamed in the views too.");
                return JToken.Parse(RenameEditMerger.Merge(response.ToString(Newtonsoft.Json.Formatting.None), edit.ToString(Newtonsoft.Json.Formatting.None)));
            }
            catch (Exception ex) when (ex is RemoteInvocationException or ConnectionLostException or OperationCanceledException or UriFormatException or Newtonsoft.Json.JsonException)
            {
                KubunoLog.WriteLine("Rename: the views could not be updated: " + ex.Message);
                return response;
            }
        }

        private void MakeResultIdUnique(JObject report)
        {
            if (!string.Equals((string?)report["kind"], "full", StringComparison.Ordinal) || report["resultId"]?.Type != JTokenType.String)
            {
                return;
            }

            report["resultId"] = _resultIds.MakeUnique((string?)report["resultId"]);
            if (Interlocked.Exchange(ref _loggedOnce, 1) == 0)
            {
                KubunoLog.WriteLine("Pull diagnostics: rust-analyzer reports now get a unique resultId (Visual Studio would otherwise keep fixed errors on screen).");
            }
        }
    }
}
