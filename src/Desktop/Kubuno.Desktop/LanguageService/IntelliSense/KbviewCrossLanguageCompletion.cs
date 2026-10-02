using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Desktop.Logic.IntelliSense;
using Kubuno.Views.Designer;
using Kubuno.Views.Designer.Handlers.Infrastructure;
using Kubuno.Shared.Logging;
using Kubuno.Rust.LanguageService;
using Kubuno.Rust.LanguageService.IntelliSense;
using Kubuno.Rust.SolutionExplorer;
using Kubuno.Views;
using Kubuno.Views.LanguageService;
using Microsoft.VisualStudio.Core.Imaging;
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

namespace Kubuno.Desktop.LanguageService.IntelliSense
{
    /// <summary>
    /// Completions in <c>.kbview</c> attribute values that come from the Rust code-behind, like XAML's: the handlers
    /// an event can be bound to in <c>OnClick="|"</c> (<c>kubuno/compatibleHandlers</c>: only handlers whose
    /// signature fits the event), and the view model's properties in <c>{Binding |}</c> (<c>kubuno/bindingPaths</c>).
    /// kubuno-views-ls's own completion (elements, attributes, enum values) is unchanged beside it.
    /// </summary>
    [Export(typeof(IAsyncCompletionSourceProvider))]
    [Name("Kubuno Views Code-behind Completion")]
    [ContentType(KbviewConstants.ContentType)]
    internal sealed class KbviewCrossLanguageCompletionProvider : IAsyncCompletionSourceProvider
    {
        [Import]
        internal ITextDocumentFactoryService TextDocumentFactory { get; set; } = null!;

        public IAsyncCompletionSource GetOrCreate(ITextView textView) =>
            textView.Properties.GetOrCreateSingletonProperty(() => new KbviewCrossLanguageCompletionSource(this));
    }

    internal sealed class KbviewCrossLanguageCompletionSource : IAsyncCompletionSource
    {
        private const string KindKey = "Kubuno.KbviewCrossLanguage";
        private static readonly ImageElement HandlerIcon = Icon("MethodPublic");
        private static readonly ImageElement PropertyIcon = Icon("PropertyPublic");

        private readonly KbviewCrossLanguageCompletionProvider _provider;

        public KbviewCrossLanguageCompletionSource(KbviewCrossLanguageCompletionProvider provider)
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

            var context = ContextAt(triggerLocation);
            RustCompletionTrace.WriteLine($"Views init reason={trigger.Reason} context={context?.Kind.ToString() ?? "none"} ready={KubunoViewsLanguageClient.Current?.ReadyRpc != null}");
            if (context is null || KubunoViewsLanguageClient.Current?.ReadyRpc is null)
            {
                return CompletionStartData.DoesNotParticipateInCompletion;
            }

            return new CompletionStartData(CompletionParticipation.ProvidesItems, new SnapshotSpan(triggerLocation.Snapshot, context.ReplaceStart, context.ReplaceLength));
        }

        public async Task<CompletionContext> GetCompletionContextAsync(IAsyncCompletionSession session, CompletionTrigger trigger, SnapshotPoint triggerLocation, SnapshotSpan applicableToSpan, CancellationToken token)
        {
            var context = ContextAt(triggerLocation);
            if (context is null || KubunoViewsLanguageClient.Current?.ReadyRpc is not { } rpc
                || !_provider.TextDocumentFactory.TryGetTextDocument(triggerLocation.Snapshot.TextBuffer, out var document))
            {
                return CompletionContext.Empty;
            }

            var uri = new Uri(document.FilePath).AbsoluteUri;
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(token);
                var openFiles = new VsWorkspaceFileHost(ServiceProvider.GlobalProvider).OpenTexts(Path.GetDirectoryName(document.FilePath) ?? string.Empty, ".rs");
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

                RustCompletionTrace.WriteLine($"Views {context.Kind} '{context.Attribute}': {string.Join(", ", names)}");
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
                    // XAML's « <Nouveau gestionnaire d'événements> »: first in the list; committing it creates the handler
                    // in the code-behind (KbviewNewHandlerCommitManager, the designer's kubuno/createHandler path).
                    items = items.Insert(0, KbviewNewHandlerCommitManager.CreateItem(this, HandlerIcon, context.Attribute, uri, triggerLocation.Position));
                }

                return new CompletionContext(items);
            }
            catch (Exception exception) when (exception is RemoteInvocationException or ConnectionLostException or ObjectDisposedException or UriFormatException or ArgumentException)
            {
                KubunoLog.WriteLine("Views completion: the code-behind could not be read: " + exception.Message);
                return CompletionContext.Empty;
            }
        }

        public Task<object> GetDescriptionAsync(IAsyncCompletionSession session, CompletionItem item, CancellationToken token)
        {
            bool french = DesignerText.IsFrench;
            if (KbviewNewHandlerCommitManager.IsNewHandlerItem(item))
            {
                return Task.FromResult<object>(french
                    ? "Crée un gestionnaire d'événements dans le code-behind et le lie à cet événement"
                    : "Creates an event handler in the code-behind and binds it to this event");
            }

            if (item.Properties.TryGetProperty(KindKey, out KbviewValueKind kind) && kind == KbviewValueKind.BindingPath)
            {
                return Task.FromResult<object>(french
                    ? item.DisplayText + " (propriété du modèle de vue, fn get du code-behind)"
                    : item.DisplayText + " (view-model property, the code-behind's fn get)");
            }

            return Task.FromResult<object>(french
                ? "fn " + item.DisplayText + " (gestionnaire d'événements du code-behind, signature compatible)"
                : "fn " + item.DisplayText + " (code-behind event handler with a compatible signature)");
        }

        /// <summary>The handlers of the event the attribute names, for the element around the caret (or the view itself).</summary>
        private async Task<IReadOnlyList<string>> HandlersAsync(JsonRpc rpc, string uri, SnapshotPoint caret, string eventName, IDictionary<string, string> openFiles, CancellationToken token)
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
            // The caret's line and a few before it are enough to find the attribute (values do not span lines here).
            var snapshot = point.Snapshot;
            var line = point.GetContainingLine();
            var text = snapshot.GetText(line.Start.Position, point.Position - line.Start.Position) + snapshot.GetText(point.Position, line.End.Position - point.Position);
            var context = KbviewValueContext.At(text, point.Position - line.Start.Position);
            return context?.Shift(line.Start.Position);
        }

        private static IReadOnlyList<string> Strings(JToken? array) =>
            array is JArray values ? values.Select(v => (string?)v).Where(v => !string.IsNullOrEmpty(v)).Select(v => v!).ToList() : (IReadOnlyList<string>)Array.Empty<string>();

        private static ImageElement Icon(string moniker)
        {
            var id = KubunoTreeItem.Moniker(moniker);
            return new ImageElement(new ImageId(id.Guid, id.Id), moniker);
        }
    }
}
