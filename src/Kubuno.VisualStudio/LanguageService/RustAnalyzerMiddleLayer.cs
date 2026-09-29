using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.VisualStudio.Core;
using Kubuno.VisualStudio.Core.IntelliSense;
using Kubuno.VisualStudio.Designer.Handlers;
using Kubuno.VisualStudio.Designer.Handlers.Infrastructure;
using Kubuno.VisualStudio.LanguageService.IntelliSense;
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
    /// <see cref="RustLanguageClient.MiddleLayer"/>, between Visual Studio's LSP client and rust-analyzer:
    /// <list type="bullet">
    /// <item>fixes stale rust-analyzer errors that stayed in the editor and the Error List after the code was fixed. See
    /// <see cref="PullDiagnosticsResultIds"/> for the cause (rust-analyzer's constant pull-diagnostics <c>resultId</c> +
    /// Visual Studio dropping an empty report whose id did not change). Every full <c>textDocument/diagnostic</c> report
    /// gets a fresh <c>resultId</c>, and the synthetic id Visual Studio sends back as <c>previousResultId</c> is removed
    /// from the next request, so rust-analyzer always computes (and returns) a full report;</item>
    /// <item>answers Visual Studio's own <c>textDocument/hover</c> with nothing: the tooltip comes from
    /// <see cref="QuickInfo.RustQuickInfoSourceProvider"/>;</item>
    /// <item>answers Visual Studio's own <c>textDocument/completion</c> with nothing: the list comes from
    /// <see cref="RustCompletionSource"/>, which asks rust-analyzer over the client's own connection (not through this layer);</item>
    /// <item>records the document versions rust-analyzer was sent (<see cref="DocumentVersions"/>), so Kubuno's own requests
    /// wait for the text they are about;</item>
    /// <item>remaps every semantic token response with <see cref="SemanticTokens"/>, the C#-like legend Kubuno announced
    /// in the handshake (see <see cref="RustSemanticTokenMap"/>);</item>
    /// <item>adds a view handler's <c>.kbview</c> usages to Find All References, and completes rust-analyzer's
    /// <c>textDocument/rename</c> of a view handler (docs/EVENTS.md §5.5, EVT-5): the <c>.kbview</c> attributes naming it
    /// and its <c>handlers!</c> string are renamed with it (<c>kubuno/renameHandler</c>).</item>
    /// </list>
    /// </summary>
    internal sealed class RustAnalyzerMiddleLayer : ILanguageClientMiddleLayer2<JToken>
    {
        private const string DocumentDiagnosticMethod = "textDocument/diagnostic";
        private const string HoverMethod = "textDocument/hover";
        private const string RenameMethod = "textDocument/rename";
        private const string CompletionMethod = "textDocument/completion";
        private const string ReferencesMethod = "textDocument/references";
        private const string SemanticTokensPrefix = "textDocument/semanticTokens/";
        private const string DidOpenMethod = "textDocument/didOpen";
        private const string DidChangeMethod = "textDocument/didChange";
        private const string DidCloseMethod = "textDocument/didClose";

        /// <summary>A name <c>kubuno/renameHandler</c> accepts, used to find a handler's view usages without renaming anything.</summary>
        private const string ProbeName = "kubuno_find_references_probe";

        private readonly PullDiagnosticsResultIds _resultIds = new();
        private int _loggedOnce;
        private int _tokensLogged;

        /// <summary>The legend rust-analyzer's semantic tokens are remapped with (set during the handshake; null before it).</summary>
        internal RustSemanticTokenMap? SemanticTokens { get; set; }

        /// <summary>
        /// Raised (with the document URI) when Visual Studio asks for semantic tokens - also what it does when rust-analyzer
        /// signals that its analysis changed (end of indexing, a finished cargo check), which the code lenses follow.
        /// </summary>
        internal static event Action<string>? SemanticTokensRequested;

        /// <summary>The document versions handed to rust-analyzer so far.</summary>
        internal RustDocumentVersions DocumentVersions { get; } = new RustDocumentVersions();

        public bool CanHandle(string methodName) =>
            methodName == DocumentDiagnosticMethod || methodName == HoverMethod || methodName == RenameMethod || methodName == CompletionMethod
            || methodName == ReferencesMethod || methodName.StartsWith(SemanticTokensPrefix, StringComparison.Ordinal)
            || methodName == DidOpenMethod || methodName == DidChangeMethod || methodName == DidCloseMethod;

        public async Task HandleNotificationAsync(string methodName, JToken methodParam, Func<JToken, Task> sendNotification)
        {
            await sendNotification(methodParam).ConfigureAwait(false);
            if (methodName == DidOpenMethod || methodName == DidChangeMethod || methodName == DidCloseMethod)
            {
                DocumentVersions.OnNotificationSent(methodName, methodParam);
            }
        }

        public async Task<JToken?> HandleRequestAsync(string methodName, JToken methodParam, Func<JToken, Task<JToken?>> sendRequest)
        {
            switch (methodName)
            {
                case RenameMethod:
                    return await HandleRenameAsync(methodParam, sendRequest).ConfigureAwait(false);
                case ReferencesMethod:
                    return await HandleReferencesAsync(methodParam, sendRequest).ConfigureAwait(false);
                case HoverMethod:
                    // Visual Studio's own hover would draw rust-analyzer's raw markdown; QuickInfo.RustQuickInfoSourceProvider
                    // asks rust-analyzer itself (over the client's JsonRpc, which does not go through this layer) and
                    // shows a C#-style tooltip instead.
                    return null;
                case CompletionMethod:
                    // Visual Studio's generic completion would list the same items again, without icons, snippets or imports.
                    return null;
                case DocumentDiagnosticMethod:
                    return await HandleDiagnosticsAsync(methodParam, sendRequest).ConfigureAwait(false);
            }

            var response = await sendRequest(methodParam).ConfigureAwait(false);
            if (methodName.StartsWith(SemanticTokensPrefix, StringComparison.Ordinal))
            {
                RemapSemanticTokens(response);
                if ((string?)methodParam["textDocument"]?["uri"] is { } tokensUri)
                {
                    SemanticTokensRequested?.Invoke(tokensUri);
                }
            }

            return response;
        }

        /// <summary>Remaps the tokens of a full, delta or range response (<c>data</c>, or each edit's <c>data</c>).</summary>
        private void RemapSemanticTokens(JToken? response)
        {
            var map = SemanticTokens;
            if (map is null || response is not JObject tokens)
            {
                return;
            }

            if (Interlocked.Exchange(ref _tokensLogged, 1) == 0)
            {
                KubunoLog.WriteLine("Semantic colorization: rust-analyzer's tokens are shown with C#'s classifications.");
            }

            if (tokens["data"] is JArray data)
            {
                tokens["data"] = Remap(map, data);
            }

            if (tokens["edits"] is JArray edits)
            {
                foreach (var edit in edits.OfType<JObject>())
                {
                    if (edit["data"] is JArray editData)
                    {
                        edit["data"] = Remap(map, editData);
                    }
                }
            }
        }

        private static JArray Remap(RustSemanticTokenMap map, JArray data)
        {
            var values = new List<int>(data.Count);
            foreach (var value in data)
            {
                values.Add(value.Type == JTokenType.Integer ? (int)value : 0);
            }

            map.Remap(values);
            return new JArray(values);
        }

        private async Task<JToken?> HandleDiagnosticsAsync(JToken methodParam, Func<JToken, Task<JToken?>> sendRequest)
        {
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
            if (response is not JObject)
            {
                return response;
            }

            var newName = (string?)methodParam["newName"];
            if (newName is null)
            {
                return response;
            }

            var views = await ViewHandlerEditAsync(methodParam, newName).ConfigureAwait(false);
            if (views?.Edit is not { } edit)
            {
                return response;
            }

            try
            {
                KubunoLog.WriteLine($"Rename: handler '{views.Value.OldName}' renamed in the views too.");
                return JToken.Parse(RenameEditMerger.Merge(response.ToString(Newtonsoft.Json.Formatting.None), edit.ToString(Newtonsoft.Json.Formatting.None)));
            }
            catch (Newtonsoft.Json.JsonException ex)
            {
                KubunoLog.WriteLine("Rename: the views could not be updated: " + ex.Message);
                return response;
            }
        }

        /// <summary>
        /// Find All References on a view handler (the method, or one of its calls) also lists the <c>.kbview</c> attributes
        /// naming it and its <c>handlers!</c> string: the places a rename of the handler would change (<c>kubuno/renameHandler</c>
        /// with a probe name), which is exactly where the views use it.
        /// </summary>
        private static async Task<JToken?> HandleReferencesAsync(JToken methodParam, Func<JToken, Task<JToken?>> sendRequest)
        {
            var response = await sendRequest(methodParam).ConfigureAwait(false);
            var views = await ViewHandlerEditAsync(methodParam, ProbeName).ConfigureAwait(false);
            if (views?.Edit is not { } edit)
            {
                return response;
            }

            var locations = response as JArray ?? new JArray();
            var known = new HashSet<string>(locations.OfType<JObject>().Select(LocationKey), StringComparer.OrdinalIgnoreCase);
            int added = 0;
            foreach (var location in EditLocations(edit))
            {
                if (known.Add(LocationKey(location)))
                {
                    locations.Add(location);
                    added++;
                }
            }

            if (added > 0)
            {
                KubunoLog.WriteLine($"Find All References: {added} view usage(s) of handler '{views.Value.OldName}' added.");
            }

            return locations;
        }

        /// <summary>
        /// Asks <c>kubuno-views-ls</c> what renaming the handler at the request's position to <paramref name="newName"/> changes
        /// in the views (null when the position is not on a view handler, or the views server is not running).
        /// </summary>
        private static async Task<(JObject Edit, string? OldName)?> ViewHandlerEditAsync(JToken methodParam, string newName)
        {
            if (KubunoViewsLanguageClient.Current?.ReadyRpc is not { } rpc)
            {
                return null;
            }

            try
            {
                var uri = (string?)methodParam["textDocument"]?["uri"];
                var position = methodParam["position"];
                if (uri is null || position is null || !uri.EndsWith(".rs", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                var folder = Path.GetDirectoryName(new Uri(Uri.UnescapeDataString(uri)).LocalPath) ?? string.Empty;
                var openFiles = new VsWorkspaceFileHost(ServiceProvider.GlobalProvider).OpenTexts(folder, ".rs");
                await TaskScheduler.Default;
                var result = await rpc.InvokeWithParameterObjectAsync<JToken?>("kubuno/renameHandler", new { uri, position, @new = newName, rustRenamed = true, openFiles }).ConfigureAwait(false);
                return result?["edit"] is JObject edit ? (edit, (string?)result["oldName"]) : null;
            }
            catch (Exception ex) when (ex is RemoteInvocationException or ConnectionLostException or OperationCanceledException or UriFormatException or ArgumentException)
            {
                KubunoLog.WriteLine("Views: kubuno/renameHandler failed: " + ex.Message);
                return null;
            }
        }

        /// <summary>The <c>Location</c>s of a <c>WorkspaceEdit</c>'s text edits (<c>changes</c> or <c>documentChanges</c>).</summary>
        internal static IEnumerable<JObject> EditLocations(JObject edit)
        {
            if (edit["changes"] is JObject changes)
            {
                foreach (var file in changes.Properties())
                {
                    foreach (var textEdit in file.Value.OfType<JObject>())
                    {
                        yield return new JObject { ["uri"] = file.Name, ["range"] = textEdit["range"]?.DeepClone() };
                    }
                }
            }

            if (edit["documentChanges"] is JArray documentChanges)
            {
                foreach (var change in documentChanges.OfType<JObject>())
                {
                    var uri = (string?)change["textDocument"]?["uri"];
                    if (uri is null || change["edits"] is not JArray edits)
                    {
                        continue;
                    }

                    foreach (var textEdit in edits.OfType<JObject>())
                    {
                        yield return new JObject { ["uri"] = uri, ["range"] = textEdit["range"]?.DeepClone() };
                    }
                }
            }
        }

        private static string LocationKey(JObject location) =>
            Uri.UnescapeDataString((string?)location["uri"] ?? string.Empty) + "|" + location["range"]?["start"]?["line"] + ":" + location["range"]?["start"]?["character"];

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
