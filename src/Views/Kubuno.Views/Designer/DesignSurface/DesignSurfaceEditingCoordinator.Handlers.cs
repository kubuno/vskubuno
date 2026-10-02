using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Kubuno.Views.Designer.Handlers;
using Kubuno.Views.Designer.Handlers.Infrastructure;
using Kubuno.Views.Designer.Selection;
using Kubuno.Views.Logging;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json.Linq;
using StreamJsonRpc;

namespace Kubuno.Views.Designer.DesignSurface
{
    /// <summary>
    /// The handler commands beyond creation (docs/EVENTS.md §5.3/§5.5, EVT-5): the Events tab row's dropdown of
    /// compatible handlers, renaming a handler from its row, clearing a row, and the context menu's
    /// "Convert to Typed Handlers". Each asks <c>kubuno-views-ls</c> with the texts of the open code-behind
    /// editors (<c>openFiles</c>) and applies the answer's <c>WorkspaceEdit</c> through those editors - one undo unit
    /// per file - or on disk for a file no editor has open (<see cref="WorkspaceEditApplier"/>).
    /// </summary>
    internal sealed partial class DesignSurfaceEditingCoordinator
    {
        private const string CompatibleHandlersMethod = "kubuno/compatibleHandlers";
        private const string RenameHandlerMethod = "kubuno/renameHandler";
        private const string RemoveHandlerMethod = "kubuno/removeHandler";
        private const string ConvertHandlersMethod = "kubuno/convertHandlers";

        /// <summary>How long the dropdown waits for the server (the grid asks synchronously, on the UI thread).</summary>
        private static readonly TimeSpan CompatibleHandlersTimeout = TimeSpan.FromSeconds(2);

        /// <summary>The last dropdown answer, reused while the buffer and the request are the same (the grid asks more than once per opening).</summary>
        private (string Key, DateTime At, IReadOnlyList<string> Names)? _compatibleCache;

        // ---- IKbviewElementHost (EVT-5) ----

        public IReadOnlyList<string> GetCompatibleHandlers(string elementId, string eventName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var rpc = _resolveLanguageClient()?.ReadyRpc;
            var uri = TryGetDocumentUri();
            if (rpc is null || uri is null)
            {
                return Array.Empty<string>();
            }

            var key = $"{CurrentVersion}|{elementId}|{eventName}";
            if (_compatibleCache is { } cached && cached.Key == key && DateTime.UtcNow - cached.At < TimeSpan.FromSeconds(3))
            {
                return cached.Names;
            }

            FlushPendingPush();
            var openFiles = OpenCodeBehindTexts(uri);
#pragma warning disable VSTHRD104 // the Properties window asks synchronously; bounded by CompatibleHandlersTimeout.
            var names = ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                var call = rpc.InvokeWithParameterObjectAsync<JToken?>(CompatibleHandlersMethod, new { uri, elementId, @event = eventName, openFiles });
                if (await Task.WhenAny(call, Task.Delay(CompatibleHandlersTimeout)) != call)
                {
                    KubunoViewsLogHost.Current.WriteLine($"[designer] {CompatibleHandlersMethod} did not answer in time.");
                    return (IReadOnlyList<string>)Array.Empty<string>();
                }

                try
                {
                    return HandlerCommandResponseParser.ParseHandlerNames((await call)?.ToString(Newtonsoft.Json.Formatting.None));
                }
                catch (Exception ex) when (ex is RemoteInvocationException or ConnectionLostException or OperationCanceledException)
                {
                    KubunoViewsLogHost.Current.WriteException($"[designer] {CompatibleHandlersMethod} failed", ex);
                    return Array.Empty<string>();
                }
            });
#pragma warning restore VSTHRD104
            _compatibleCache = (key, DateTime.UtcNow, names);
            return names;
        }

        public void RenameHandler(string elementId, string eventName, string oldName, string newName) =>
            RunHandlerCommand(RenameHandlerMethod, (uri, openFiles) => new { uri, old = oldName, @new = newName, openFiles }, $"Rename handler {oldName} to {newName}", fallback: null);

        public void RemoveHandler(string elementId, string eventName) =>
            RunHandlerCommand(
                RemoveHandlerMethod,
                (uri, openFiles) => new { uri, elementId, @event = eventName, openFiles },
                "Remove handler of " + eventName,
                // Without the server, clearing the row still removes the attribute (as before EVT-5).
                fallback: () =>
                {
                    ThreadHelper.ThrowIfNotOnUIThread();
                    RemoveAttributeAsWritten(elementId, eventName);
                });

        // ---- IDesignerMenuActions (EVT-5) ----

        public void ConvertHandlers() =>
            RunHandlerCommand(ConvertHandlersMethod, (uri, openFiles) => new { uri, openFiles }, "Convert to typed handlers", fallback: null);

        // ---- plumbing ----

        /// <summary>
        /// Sends <paramref name="method"/> with the params <paramref name="buildParams"/> makes from this view's URI,
        /// then applies the answer's edit (or shows why there is none).
        /// </summary>
        private void RunHandlerCommand(string method, Func<string, IDictionary<string, string>, object> buildParams, string description, Action? fallback) =>
            Run(
                async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    await RunHandlerCommandAsync(method, buildParams, description, fallback);
                },
                method);

        private async Task RunHandlerCommandAsync(string method, Func<string, IDictionary<string, string>, object> buildParams, string description, Action? fallback)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var rpc = _resolveLanguageClient()?.ReadyRpc;
            var uri = TryGetDocumentUri();
            if (rpc is null || uri is null || _oleServiceProvider is null)
            {
                KubunoViewsLogHost.Current.WriteLine($"[designer] {method} skipped: the Kubuno Views language client is not attached yet.");
                fallback?.Invoke();
                return;
            }

            FlushPendingPush();
            _compatibleCache = null;
            JToken? token;
            try
            {
                token = await rpc.InvokeWithParameterObjectAsync<JToken?>(method, buildParams(uri, OpenCodeBehindTexts(uri))).ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is RemoteInvocationException or ConnectionLostException or OperationCanceledException)
            {
                KubunoViewsLogHost.Current.WriteException($"[designer] {method} failed", ex);
                fallback?.Invoke();
                return;
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var response = HandlerCommandResponseParser.ParseEditResponse(token?.ToString(Newtonsoft.Json.Formatting.None));
            if (response?.Edit is null)
            {
                ShowStatus(response?.Reason is { } reason ? DesignerText.HandlerCommandRefused(description, reason) : DesignerText.HandlerCommandNothingToDo(description));
                return;
            }

            var result = WorkspaceEditApplier.Apply(response.Edit, new VsWorkspaceFileHost(new ServiceProvider(_oleServiceProvider)));
            if (!result.Succeeded)
            {
                ShowStatus(DesignerText.HandlerCommandFailed(description, Path.GetFileName(new Uri(result.FailedFile!).LocalPath), result.Failure ?? string.Empty));
                return;
            }

            KubunoViewsLogHost.Current.WriteLine($"[designer] {description}: {result.AppliedFiles.Count} file(s) edited{(response.RemovedStub ? ", handler stub removed" : string.Empty)}.");
            if (method == ConvertHandlersMethod)
            {
                ShowStatus(DesignerText.HandlersConverted(response.Converted.Count));
            }
        }

        /// <summary>Removes the event's attribute under the name the file uses (canonical or older alias).</summary>
        private void RemoveAttributeAsWritten(string elementId, string eventName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var attributes = ElementAttributeReader.Read(GetCurrentText(), elementId)?.Attributes;
            var @event = Registry.Find(ElementAttributeReader.Read(GetCurrentText(), elementId)?.TagName ?? string.Empty)?.FindEvent(eventName);
            foreach (var name in @event?.AttributeNames ?? new[] { eventName })
            {
                if (attributes is not null && attributes.ContainsKey(name))
                {
                    RemoveAttribute(elementId, name);
                    return;
                }
            }
        }

        /// <summary>The texts of the open <c>.rs</c> editors of this view's folder (<c>openFiles</c>), empty when unknown.</summary>
        private IDictionary<string, string> OpenCodeBehindTexts(string kbviewUri)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_oleServiceProvider is null)
            {
                return new Dictionary<string, string>();
            }

            var folder = Path.GetDirectoryName(new Uri(kbviewUri).LocalPath) ?? string.Empty;
            return new VsWorkspaceFileHost(new ServiceProvider(_oleServiceProvider)).OpenTexts(folder, ".rs");
        }
    }
}
