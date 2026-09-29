using System;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.VisualStudio.Core;
using Kubuno.VisualStudio.Logging;
using Microsoft.VisualStudio.LanguageServer.Client;
using Newtonsoft.Json.Linq;

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
    /// </summary>
    internal sealed class RustAnalyzerMiddleLayer : ILanguageClientMiddleLayer2<JToken>
    {
        private const string DocumentDiagnosticMethod = "textDocument/diagnostic";
        private const string HoverMethod = "textDocument/hover";

        private readonly PullDiagnosticsResultIds _resultIds = new();
        private int _loggedOnce;

        public bool CanHandle(string methodName) => methodName == DocumentDiagnosticMethod || methodName == HoverMethod;

        public Task HandleNotificationAsync(string methodName, JToken methodParam, Func<JToken, Task> sendNotification) =>
            sendNotification(methodParam);

        public async Task<JToken?> HandleRequestAsync(string methodName, JToken methodParam, Func<JToken, Task<JToken?>> sendRequest)
        {
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
