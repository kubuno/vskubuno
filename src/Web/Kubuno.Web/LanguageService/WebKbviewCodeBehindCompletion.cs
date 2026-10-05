using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Rust.LanguageService.IntelliSense;
using Kubuno.Shared.Logging;
using Kubuno.Views;
using Kubuno.Views.Designer;
using Kubuno.Views.Designer.Handlers.Infrastructure;
using Kubuno.Views.LanguageService;
using Kubuno.Views.Logic.IntelliSense;
using Microsoft.VisualStudio.Core.Imaging;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Utilities;
using Newtonsoft.Json.Linq;
using StreamJsonRpc;

namespace Kubuno.Web.LanguageService
{
    /// <summary>
    /// Completions in the attribute values of a web view (<c>.kbview</c> / <c>.kbcontrol</c> of a web project,
    /// docs/WEB-VIEWS.md section 5, WV-7) that come from its TypeScript code-behind, like XAML's: the methods an event can
    /// be bound to in <c>OnClick="|"</c> (<c>kubuno/compatibleHandlers</c>: only those whose parameters fit the event),
    /// headed by « &lt;Nouveau gestionnaire d'événements&gt; », and the class's bindable members in <c>{Binding |}</c>
    /// (<c>kubuno/bindingPaths</c>: <c>@bind accessor</c> fields, fields, getters). The code-behind's open editors are sent
    /// as <c>openFiles</c>, so unsaved TypeScript counts. The desktop's Rust completion stays out of web views.
    /// </summary>
    [Export(typeof(IAsyncCompletionSourceProvider))]
    [Name("Kubuno Web Views Code-behind Completion")]
    [ContentType(KbviewConstants.ContentType)]
    internal sealed class WebKbviewCodeBehindCompletionProvider : IAsyncCompletionSourceProvider
    {
        [Import]
        internal ITextDocumentFactoryService TextDocumentFactory { get; set; } = null!;

        public IAsyncCompletionSource GetOrCreate(ITextView textView) =>
            textView.Properties.GetOrCreateSingletonProperty(() => new WebKbviewCodeBehindCompletionSource(this));
    }

    internal sealed class WebKbviewCodeBehindCompletionSource : IAsyncCompletionSource
    {
        /// <summary>The TypeScript code-behind files whose open editors are sent with each request.</summary>
        internal static readonly string[] CodeBehindExtensions = { ".ts", ".tsx" };

        private const string KindKey = "Kubuno.WebKbviewCodeBehind";
        private static readonly ImageElement HandlerIcon = new ImageElement(KnownMonikers.MethodPublic.ToImageId(), "Method");
        private static readonly ImageElement PropertyIcon = new ImageElement(KnownMonikers.PropertyPublic.ToImageId(), "Property");

        private readonly WebKbviewCodeBehindCompletionProvider _provider;

        public WebKbviewCodeBehindCompletionSource(WebKbviewCodeBehindCompletionProvider provider)
        {
            _provider = provider;
        }

        public CompletionStartData InitializeCompletion(CompletionTrigger trigger, SnapshotPoint triggerLocation, CancellationToken token)
        {
            if (trigger.Reason == CompletionTriggerReason.Insertion)
            {
                char c = trigger.Character;
                if (!(char.IsLetter(c) || c == '_' || c == '"' || c == '\'' || c == ' ' || c == '='))
                {
                    return CompletionStartData.DoesNotParticipateInCompletion;
                }
            }
            else if (trigger.Reason != CompletionTriggerReason.Invoke && trigger.Reason != CompletionTriggerReason.InvokeAndCommitIfUnique)
            {
                return CompletionStartData.DoesNotParticipateInCompletion;
            }

            if (!IsWebView(triggerLocation.Snapshot.TextBuffer, out _))
            {
                return CompletionStartData.DoesNotParticipateInCompletion;
            }

            var context = ContextAt(triggerLocation);
            if (context is null || KubunoViewsLanguageClient.Current?.ReadyRpc is null)
            {
                return CompletionStartData.DoesNotParticipateInCompletion;
            }

            return new CompletionStartData(CompletionParticipation.ProvidesItems, new SnapshotSpan(triggerLocation.Snapshot, context.ReplaceStart, context.ReplaceLength));
        }

        public async Task<CompletionContext> GetCompletionContextAsync(IAsyncCompletionSession session, CompletionTrigger trigger, SnapshotPoint triggerLocation, SnapshotSpan applicableToSpan, CancellationToken token)
        {
            var context = ContextAt(triggerLocation);
            if (context is null || KubunoViewsLanguageClient.Current?.ReadyRpc is not { } rpc || !IsWebView(triggerLocation.Snapshot.TextBuffer, out var path))
            {
                return CompletionContext.Empty;
            }

            var uri = new Uri(path).AbsoluteUri;
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(token);
                var openFiles = new VsWorkspaceFileHost(ServiceProvider.GlobalProvider).OpenTexts(Path.GetDirectoryName(path) ?? string.Empty, CodeBehindExtensions);
                await TaskScheduler.Default;

                IReadOnlyList<string> names;
                if (context.Kind == KbviewValueKind.BindingPath)
                {
                    var result = await rpc.InvokeWithParameterObjectAsync<JToken?>("kubuno/bindingPaths", new { uri, openFiles }, token).ConfigureAwait(false);
                    names = Strings(result?["paths"]);
                }
                else
                {
                    names = await HandlersAsync(rpc, uri, triggerLocation, context.Attribute, openFiles, token).ConfigureAwait(false);
                }

                bool isHandler = context.Kind != KbviewValueKind.BindingPath;
                if (names.Count == 0 && !isHandler)
                {
                    return CompletionContext.Empty;
                }

                var icon = isHandler ? HandlerIcon : PropertyIcon;
                var items = names.Distinct(StringComparer.Ordinal).Select((name, index) =>
                {
                    var item = new CompletionItem(name, this, icon, ImmutableArray<CompletionFilter>.Empty, string.Empty, name, index.ToString("D4"), name, name, ImmutableArray<ImageElement>.Empty);
                    item.Properties.AddProperty(KindKey, context.Kind);
                    return item;
                }).ToImmutableArray();
                if (isHandler)
                {
                    // « <Nouveau gestionnaire d'événements> » first: committing it creates the method in the TypeScript class.
                    items = items.Insert(0, WebKbviewNewHandlerCommitManager.CreateItem(this, HandlerIcon, context.Attribute, uri, triggerLocation.Position));
                }

                return new CompletionContext(items);
            }
            catch (Exception exception) when (exception is RemoteInvocationException or ConnectionLostException or ObjectDisposedException or UriFormatException or ArgumentException)
            {
                KubunoLog.WriteLine("Web views completion: the TypeScript code-behind could not be read: " + exception.Message);
                return CompletionContext.Empty;
            }
        }

        public Task<object> GetDescriptionAsync(IAsyncCompletionSession session, CompletionItem item, CancellationToken token)
        {
            bool french = DesignerText.IsFrench;
            if (WebKbviewNewHandlerCommitManager.IsNewHandlerItem(item))
            {
                return Task.FromResult<object>(french
                    ? "Crée une méthode dans la classe TypeScript du code-behind et la lie à cet événement"
                    : "Creates a method in the TypeScript code-behind class and binds it to this event");
            }

            if (item.Properties.TryGetProperty(KindKey, out KbviewValueKind kind) && kind == KbviewValueKind.BindingPath)
            {
                return Task.FromResult<object>(french
                    ? item.DisplayText + " (membre liable de la classe du code-behind : @bind accessor, champ ou getter)"
                    : item.DisplayText + " (bindable member of the code-behind class: @bind accessor, field or getter)");
            }

            return Task.FromResult<object>(french
                ? item.DisplayText + "() (méthode du code-behind TypeScript, signature compatible)"
                : item.DisplayText + "() (TypeScript code-behind method with a compatible signature)");
        }

        private bool IsWebView(ITextBuffer buffer, out string path)
        {
            path = string.Empty;
            if (!_provider.TextDocumentFactory.TryGetTextDocument(buffer, out var document) || !ViewTargetCache.IsWeb(document.FilePath))
            {
                return false;
            }

            path = document.FilePath;
            return true;
        }

        /// <summary>The handlers of the event the attribute names, for the element around the caret (or the view itself).</summary>
        private static async Task<IReadOnlyList<string>> HandlersAsync(JsonRpc rpc, string uri, SnapshotPoint caret, string eventName, IDictionary<string, string> openFiles, CancellationToken token)
        {
            var element = await rpc.InvokeWithParameterObjectAsync<JToken?>("kubuno/elementAtOffset", new JObject { ["uri"] = uri, ["position"] = RustLsp.Position(caret) }, token).ConfigureAwait(false);
            var elementId = (string?)element?["elementId"] ?? string.Empty;
            var handlers = Strings((await rpc.InvokeWithParameterObjectAsync<JToken?>("kubuno/compatibleHandlers", new { uri, elementId, @event = eventName, openFiles }, token).ConfigureAwait(false))?["handlers"]);
            if (handlers.Count == 0 && elementId.Length > 0)
            {
                // The view's own events (OnLoad...) are addressed with the empty id.
                handlers = Strings((await rpc.InvokeWithParameterObjectAsync<JToken?>("kubuno/compatibleHandlers", new { uri, elementId = string.Empty, @event = eventName, openFiles }, token).ConfigureAwait(false))?["handlers"]);
            }

            return handlers;
        }

        private static KbviewValueContext? ContextAt(SnapshotPoint point)
        {
            var snapshot = point.Snapshot;
            var line = point.GetContainingLine();
            var text = snapshot.GetText(line.Start.Position, point.Position - line.Start.Position) + snapshot.GetText(point.Position, line.End.Position - point.Position);
            var context = KbviewValueContext.At(text, point.Position - line.Start.Position);
            return context?.Shift(line.Start.Position);
        }

        private static IReadOnlyList<string> Strings(JToken? array) =>
            array is JArray values ? values.Select(v => (string?)v).Where(v => !string.IsNullOrEmpty(v)).Select(v => v!).ToList() : (IReadOnlyList<string>)Array.Empty<string>();
    }
}
