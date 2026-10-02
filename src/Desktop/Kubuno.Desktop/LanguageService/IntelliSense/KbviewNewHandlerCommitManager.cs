using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Threading;
using Kubuno.Rust.LanguageService.IntelliSense;
using Kubuno.Shared.Logging;
using Kubuno.Views;
using Kubuno.Views.Designer;
using Kubuno.Views.Designer.Handlers;
using Kubuno.Views.Designer.Handlers.Infrastructure;
using Kubuno.Views.LanguageService;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Utilities;
using Newtonsoft.Json.Linq;

namespace Kubuno.Desktop.LanguageService.IntelliSense
{
    /// <summary>
    /// XAML's « &lt;Nouveau gestionnaire d'événements&gt; » in a <c>.kbview</c>: the first item of the handler list of an
    /// <c>On…="|"</c> attribute (<see cref="KbviewCrossLanguageCompletionSource"/>). Committing it inserts nothing itself:
    /// it asks kubuno-views-ls for the handler (<c>kubuno/createHandler</c>, the designer's double-click path, DSG-10),
    /// which names it (<c>ok_click</c>), writes the attribute and adds the stub to the code-behind, then shows the stub.
    /// Every other item is left to the other commit managers.
    /// </summary>
    [Export(typeof(IAsyncCompletionCommitManagerProvider))]
    [Name("Kubuno Views New Event Handler Commit")]
    [ContentType(KbviewConstants.ContentType)]
    internal sealed class KbviewNewHandlerCommitManagerProvider : IAsyncCompletionCommitManagerProvider
    {
        public IAsyncCompletionCommitManager GetOrCreate(ITextView textView) =>
            textView.Properties.GetOrCreateSingletonProperty(() => new KbviewNewHandlerCommitManager());
    }

    internal sealed class KbviewNewHandlerCommitManager : IAsyncCompletionCommitManager
    {
        private const string Key = "Kubuno.KbviewNewHandler";

        /// <summary>The event, the view's URI and where the list was opened.</summary>
        private sealed class Data
        {
            public Data(string eventName, string uri, int position)
            {
                EventName = eventName;
                Uri = uri;
                Position = position;
            }

            public string EventName { get; }

            public string Uri { get; }

            public int Position { get; }
        }

        public IEnumerable<char> PotentialCommitCharacters => Array.Empty<char>();

        /// <summary>The « &lt;New Event Handler&gt; » item for <paramref name="eventName"/>.</summary>
        public static CompletionItem CreateItem(IAsyncCompletionSource source, ImageElement icon, string eventName, string uri, int position)
        {
            var text = DesignerText.IsFrench ? "<Nouveau gestionnaire d'événements>" : "<New Event Handler>";
            var item = new CompletionItem(text, source, icon, ImmutableArray<CompletionFilter>.Empty, string.Empty, string.Empty, string.Empty, text, ImmutableArray<ImageElement>.Empty);
            item.Properties.AddProperty(Key, new Data(eventName, uri, position));
            return item;
        }

        public static bool IsNewHandlerItem(CompletionItem item) => item.Properties.ContainsProperty(Key);

        public bool ShouldCommitCompletion(IAsyncCompletionSession session, SnapshotPoint location, char typedChar, CancellationToken token) => false;

        public CommitResult TryCommit(IAsyncCompletionSession session, ITextBuffer buffer, CompletionItem item, char typedChar, CancellationToken token)
        {
            if (!item.Properties.TryGetProperty(Key, out Data data))
            {
                return CommitResult.Unhandled;
            }

            var snapshot = buffer.CurrentSnapshot;
#pragma warning disable VSSDK007 // Runs after the session closed; nothing to join it to (like the designer's own handler creation).
            ThreadHelper.JoinableTaskFactory.RunAsync(() => CreateAsync(data, snapshot)).Task.Forget();
#pragma warning restore VSSDK007
            return new CommitResult(isHandled: true, CommitBehavior.CancelCommit);
        }

        private static async System.Threading.Tasks.Task CreateAsync(Data data, ITextSnapshot snapshot)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (KubunoViewsLanguageClient.Current?.ReadyRpc is not { } rpc)
            {
                KubunoLog.WriteLine("Views completion: <New Event Handler> skipped, kubuno-views-ls is not running.");
                return;
            }

            try
            {
                var path = new Uri(data.Uri).LocalPath;
                var openFiles = new VsWorkspaceFileHost(ServiceProvider.GlobalProvider).OpenTexts(Path.GetDirectoryName(path) ?? string.Empty, ".rs");
                var point = new SnapshotPoint(snapshot, Math.Min(data.Position, snapshot.Length));
                var element = await rpc.InvokeWithParameterObjectAsync<JToken?>("kubuno/elementAtOffset", new JObject { ["uri"] = data.Uri, ["position"] = RustLsp.Position(point) });
                var elementId = (string?)element?["elementId"] ?? string.Empty;
                var service = new HandlerCreationService(new JsonRpcKubunoViewsLanguageServerClient(rpc), new VsHandlerDocumentHost(ServiceProvider.GlobalProvider));
                var result = await service.CreateAsync(new CreateHandlerRequest(data.Uri, elementId, data.EventName, null, openFiles), CancellationToken.None);
                KubunoLog.WriteLine($"Views completion: <New Event Handler> {data.EventName} on '{elementId}': {result.Outcome} ({result.HandlerName}).");
            }
            catch (Exception exception)
            {
                KubunoLog.WriteLine("Views completion: <New Event Handler> failed: " + exception.Message);
            }
        }
    }
}
