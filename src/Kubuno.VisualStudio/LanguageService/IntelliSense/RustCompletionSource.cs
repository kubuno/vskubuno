using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.VisualStudio.Core.IntelliSense;
using Kubuno.VisualStudio.Designer;
using Kubuno.VisualStudio.LanguageService.QuickInfo;
using Kubuno.VisualStudio.Logging;
using Kubuno.VisualStudio.SolutionExplorer;
using Microsoft.VisualStudio.Core.Imaging;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.Utilities;
using Newtonsoft.Json.Linq;
using StreamJsonRpc;

namespace Kubuno.VisualStudio.LanguageService.IntelliSense
{
    /// <summary>
    /// rust-analyzer's completion, shown like C#'s IntelliSense (it replaces Visual Studio's generic LSP completion
    /// for Rust, which <see cref="RustAnalyzerMiddleLayer"/> silences): Kubuno icons and filter buttons, IntelliCode-like
    /// starred items, a C#-style description tooltip, snippets with Tab stops (argument placeholders, postfix
    /// templates), auto-import completions that add the <c>use</c>, and Parameter Info reopened after a call.
    /// </summary>
    [Export(typeof(IAsyncCompletionSourceProvider))]
    [Name("Kubuno Rust Completion")]
    [ContentType(Constants.RustContentType)]
    internal sealed class RustCompletionSourceProvider : IAsyncCompletionSourceProvider
    {
        [Import]
        internal IClassificationTypeRegistryService ClassificationRegistry { get; set; } = null!;

        [Import]
        internal ITextDocumentFactoryService TextDocumentFactory { get; set; } = null!;

        public IAsyncCompletionSource GetOrCreate(ITextView textView) =>
            textView.Properties.GetOrCreateSingletonProperty(() => new RustCompletionSource(this));
    }

    [Export(typeof(IAsyncCompletionCommitManagerProvider))]
    [Name("Kubuno Rust Completion Commit")]
    [ContentType(Constants.RustContentType)]
    internal sealed class RustCompletionCommitManagerProvider : IAsyncCompletionCommitManagerProvider
    {
        [Import]
        internal ITextUndoHistoryRegistry UndoHistoryRegistry { get; set; } = null!;

        [Import]
        internal IEditorOperationsFactoryService EditorOperations { get; set; } = null!;

        public IAsyncCompletionCommitManager GetOrCreate(ITextView textView) =>
            textView.Properties.GetOrCreateSingletonProperty(() => new RustCompletionCommitManager(textView, this));
    }

    /// <summary>What a Kubuno completion item carries (in its property bag).</summary>
    internal sealed class RustCompletionData
    {
        public RustCompletionData(JObject item, ITextSnapshot serverSnapshot, RustCompletionCategory category, string displayText, string filePath)
        {
            Item = item;
            FilePath = filePath;
            ServerSnapshot = serverSnapshot;
            Category = category;
            DisplayText = displayText;
        }

        public const string Key = "Kubuno.RustCompletion";

        /// <summary>rust-analyzer's item (replaced by the resolved one once resolved).</summary>
        public JObject Item { get; set; }

        public bool Resolved { get; set; }

        /// <summary>The text the server had when it answered: the item's ranges are in this snapshot.</summary>
        public ITextSnapshot ServerSnapshot { get; }

        public RustCompletionCategory Category { get; }

        public string DisplayText { get; }

        /// <summary>The .rs file the item was asked for.</summary>
        public string FilePath { get; }

        /// <summary>The path of the item rust-analyzer will import (<c>use</c>) on commit, or null.</summary>
        public string? ImportPath
        {
            get
            {
                var imports = Item["data"]?["imports"] as JArray;
                return imports is { Count: > 0 } ? (string?)imports[0]?["full_import_path"] : null;
            }
        }

        public bool NeedsResolve => !Resolved && Item["data"]?["imports"] is JArray { Count: > 0 } && Item["additionalTextEdits"] is null;
    }

    internal sealed class RustCompletionSource : IAsyncCompletionSource, IAsyncExpandingCompletionSource
    {
        private static readonly TimeSpan ResolveTimeout = TimeSpan.FromSeconds(3);

        // IconCache first: the initializers below use it.
        private static readonly Dictionary<string, ImageElement> IconCache = new Dictionary<string, ImageElement>(StringComparer.Ordinal);
        private static readonly Dictionary<string, (CompletionFilter En, CompletionFilter Fr)> FilterCache = CreateFilters();
        private static readonly CompletionExpander UnimportedExpanderEn = new CompletionExpander("Show items from unimported crates and modules", "a", Icon("ExpandScope"));
        private static readonly CompletionExpander UnimportedExpanderFr = new CompletionExpander("Afficher les éléments des crates et modules non importés", "a", Icon("ExpandScope"));

        private readonly RustCompletionSourceProvider _provider;
        private readonly QuickInfoElementFactory _elements;
        private static int _logged;

        public RustCompletionSource(RustCompletionSourceProvider provider)
        {
            _provider = provider;
            _elements = new QuickInfoElementFactory(provider.ClassificationRegistry);
        }

        public CompletionStartData InitializeCompletion(CompletionTrigger trigger, SnapshotPoint triggerLocation, CancellationToken token)
        {
            Dbg($"Init reason={trigger.Reason} char='{trigger.Character}' pos={triggerLocation.Position} prev='{(triggerLocation.Position >= 2 ? triggerLocation.Snapshot[triggerLocation.Position - 2] : ' ')}'");
            var snapshot = triggerLocation.Snapshot;
            int position = triggerLocation.Position;
            switch (trigger.Reason)
            {
                case CompletionTriggerReason.Insertion:
                    char c = trigger.Character;
                    bool identifier = char.IsLetter(c) || c == '_';
                    bool member = c == '.' && !(position >= 2 && snapshot[position - 2] == '.');
                    bool path = c == ':' && position >= 2 && snapshot[position - 2] == ':';
                    bool lifetime = c == '\'' && position >= 2 && (snapshot[position - 2] == '&' || snapshot[position - 2] == '<');
                    if (!(identifier || member || path || lifetime))
                    {
                        return CompletionStartData.DoesNotParticipateInCompletion;
                    }

                    if (identifier && position >= 2 && char.IsDigit(snapshot[position - 2]))
                    {
                        // 1u32, 0x1f...: a literal suffix, not a name.
                        return CompletionStartData.DoesNotParticipateInCompletion;
                    }

                    if (IsInLineComment(snapshot, position))
                    {
                        return CompletionStartData.DoesNotParticipateInCompletion;
                    }

                    break;
                case CompletionTriggerReason.Invoke:
                case CompletionTriggerReason.InvokeAndCommitIfUnique:
                    break;
                default:
                    return CompletionStartData.DoesNotParticipateInCompletion;
            }

            if (RustLanguageClient.Instance?.Rpc is null)
            {
                return CompletionStartData.DoesNotParticipateInCompletion;
            }

            int start = position;
            while (start > 0 && IsIdentifierChar(snapshot[start - 1]))
            {
                start--;
            }

            int end = position;
            while (end < snapshot.Length && IsIdentifierChar(snapshot[end]))
            {
                end++;
            }

            return new CompletionStartData(CompletionParticipation.ProvidesItems, new SnapshotSpan(snapshot, start, end - start));
        }

        public Task<CompletionContext> GetCompletionContextAsync(IAsyncCompletionSession session, CompletionTrigger trigger, SnapshotPoint triggerLocation, SnapshotSpan applicableToSpan, CancellationToken token)
        {
            bool byCharacter = trigger.Reason == CompletionTriggerReason.Insertion && !char.IsLetterOrDigit(trigger.Character) && trigger.Character != '_';
            var context = byCharacter
                ? new JObject { ["triggerKind"] = 2, ["triggerCharacter"] = trigger.Character.ToString() }
                : new JObject { ["triggerKind"] = 1 };
            return RequestItemsAsync(triggerLocation, context, applicableToSpan, token);
        }

        /// <summary>The unimported-items button toggled: those items are already in the list, the button only shows or hides them.</summary>
        public Task<CompletionContext> GetExpandedCompletionContextAsync(IAsyncCompletionSession session, CompletionExpander expander, CompletionTrigger initialTrigger, SnapshotSpan applicableToSpan, CancellationToken token) =>
            Task.FromResult(CompletionContext.Empty);

        private async Task<CompletionContext> RequestItemsAsync(SnapshotPoint triggerLocation, JObject lspContext, SnapshotSpan applicableToSpan, CancellationToken token)
        {
            if (!_provider.TextDocumentFactory.TryGetTextDocument(triggerLocation.Snapshot.TextBuffer, out var document))
            {
                return CompletionContext.Empty;
            }

            var tracking = triggerLocation.Snapshot.CreateTrackingPoint(triggerLocation.Position, PointTrackingMode.Positive);
            JToken? result;
            ITextSnapshot serverSnapshot;
            try
            {
                (result, serverSnapshot) = await RustLsp.RequestAsync(
                    triggerLocation.Snapshot,
                    document.FilePath,
                    "textDocument/completion",
                    snapshot => new JObject
                    {
                        ["textDocument"] = RustLsp.TextDocument(document.FilePath),
                        ["position"] = RustLsp.Position(tracking.GetPoint(snapshot)),
                        ["context"] = lspContext,
                    },
                    token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return CompletionContext.Empty;
            }
            catch (Exception exception) when (exception is RemoteInvocationException or ConnectionLostException or ObjectDisposedException or InvalidOperationException)
            {
                KubunoLog.WriteLine("Completion: textDocument/completion failed: " + exception.Message);
                return CompletionContext.Empty;
            }

            var itemsToken = result is JArray array ? array : result?["items"] as JArray;
            if (itemsToken is null || itemsToken.Count == 0)
            {
                return CompletionContext.Empty;
            }

            var raw = itemsToken.OfType<JObject>().ToList();
            if (Interlocked.Increment(ref _logged) <= 3)
            {
                KubunoLog.WriteLine($"Completion: {raw.Count} rust-analyzer items ({raw.Count(i => (int?)i["insertTextFormat"] == 2)} snippets, {raw.Count(i => i["data"]?["imports"] is JArray { Count: > 0 })} with an import).");
            }

            var starred = RustCompletionPresentation.StarredIndexes(raw.Select(i => (string?)i["sortText"]).ToList());
            var starRank = new Dictionary<int, int>();
            for (int i = 0; i < starred.Count; i++)
            {
                starRank[starred[i]] = i;
            }

            bool french = DesignerText.IsFrench;
            var items = ImmutableArray.CreateBuilder<CompletionItem>(raw.Count);
            var usedFilters = new Dictionary<string, CompletionFilter>(StringComparer.Ordinal);
            bool hasUnimported = false;
            for (int i = 0; i < raw.Count; i++)
            {
                var item = raw[i];
                var label = (string?)item["label"] ?? string.Empty;
                var category = RustCompletionPresentation.Categorize((int?)item["kind"], label, (string?)item["detail"]);
                var display = RustCompletionPresentation.DisplayText(label, category);
                var data = new RustCompletionData(item, serverSnapshot, category, display, document.FilePath);

                var filters = ImmutableArray.CreateBuilder<CompletionFilter>(2);
                var filterKey = RustCompletionPresentation.FilterKey(category);
                if (filterKey != null && FilterCache.TryGetValue(filterKey, out var pair))
                {
                    var filter = french ? pair.Fr : pair.En;
                    usedFilters[filterKey] = filter;
                    filters.Add(filter);
                }

                if (data.ImportPath != null)
                {
                    // C#'s "Show items from unimported namespaces" button: these items add a `use` when committed.
                    filters.Add(french ? UnimportedExpanderFr : UnimportedExpanderEn);
                    hasUnimported = true;
                }

                bool isStarred = starRank.TryGetValue(i, out var rank);
                var shown = isStarred ? RustCompletionPresentation.StarPrefix + display : display;
                var sortText = isStarred ? "0" + rank.ToString("D2") : "1" + display.ToLowerInvariant();
                var filterText = (string?)item["filterText"] ?? display;
                var suffix = data.ImportPath is { } importPath ? "(use " + importPath + ")" : string.Empty;
                var completionItem = new CompletionItem(
                    shown,
                    this,
                    Icon(RustCompletionPresentation.IconMonikerName(category)),
                    filters.ToImmutable(),
                    suffix,
                    display,
                    sortText,
                    filterText,
                    display,
                    ImmutableArray<ImageElement>.Empty,
                    ImmutableArray<char>.Empty,
                    applicableToSpan,
                    false,
                    // Not rust-analyzer's `preselect`: Visual Studio treats a preselected item as the unique match of an
                    // empty prefix, and Ctrl+Space (Complete Word) would insert it at once. The starred items lead instead.
                    false);
                completionItem.Properties.AddProperty(RustCompletionData.Key, data);
                items.Add(completionItem);
            }

            var filterStates = RustCompletionPresentation.Filters
                .Where(f => usedFilters.ContainsKey(f.Key))
                .Select(f => new CompletionFilterWithState(usedFilters[f.Key], false))
                .ToImmutableArray();
            if (hasUnimported)
            {
                filterStates = filterStates.Add(new CompletionFilterWithState(french ? UnimportedExpanderFr : UnimportedExpanderEn, isAvailable: true, isSelected: true));
            }

            // rust-analyzer answers with every item matching the prefix typed so far (a superset of what a longer prefix
            // gives), so the list is filtered locally from here; an auto-import item is asked again when committed.
            return new CompletionContext(items.ToImmutable(), null, InitialSelectionHint.RegularSelection, filterStates, false, null);
        }

        public async Task<object> GetDescriptionAsync(IAsyncCompletionSession session, CompletionItem item, CancellationToken token)
        {
            if (!item.Properties.TryGetProperty(RustCompletionData.Key, out RustCompletionData data))
            {
                return string.Empty;
            }

            await EnsureResolvedAsync(data, token).ConfigureAwait(false);
            var documentation = data.Item["documentation"] is JObject markup ? (string?)markup["value"] : (string?)data.Item["documentation"];
            var description = RustCompletionPresentation.Description(
                data.Category,
                data.DisplayText,
                (string?)data.Item["detail"] ?? (string?)data.Item["labelDetails"]?["description"],
                documentation,
                data.ImportPath,
                DesignerText.IsFrench);
            return _elements.Create(description);
        }

        /// <summary>
        /// The same item asked again for the text as it is now, then resolved: rust-analyzer only computes the <c>use</c> an
        /// auto-import item adds for the document version the item was listed for, and the list is filtered locally while
        /// the user types. Null when the item is no longer offered.
        /// </summary>
        internal static async Task<RustCompletionData?> RefreshAsync(RustCompletionData data, SnapshotPoint caret, CancellationToken token)
        {
            var label = (string?)data.Item["label"];
            var importPath = data.ImportPath;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(ResolveTimeout);
            try
            {
                var (result, snapshot) = await RustLsp.RequestAsync(
                    caret.Snapshot,
                    data.FilePath,
                    "textDocument/completion",
                    s => new JObject
                    {
                        ["textDocument"] = RustLsp.TextDocument(data.FilePath),
                        ["position"] = RustLsp.Position(caret.TranslateTo(s, PointTrackingMode.Positive)),
                        ["context"] = new JObject { ["triggerKind"] = 1 },
                    },
                    timeout.Token).ConfigureAwait(false);
                var items = result is JArray array ? array : result?["items"] as JArray;
                var match = items?.OfType<JObject>().FirstOrDefault(i =>
                    (string?)i["label"] == label
                    && (string?)(i["data"]?["imports"] as JArray)?.FirstOrDefault()?["full_import_path"] == importPath);
                if (match is null)
                {
                    return null;
                }

                var fresh = new RustCompletionData(match, snapshot, data.Category, data.DisplayText, data.FilePath);
                await EnsureResolvedAsync(fresh, timeout.Token).ConfigureAwait(false);
                return fresh;
            }
            catch (Exception exception) when (exception is OperationCanceledException or RemoteInvocationException or ConnectionLostException or ObjectDisposedException)
            {
                KubunoLog.WriteLine("Completion: the import could not be computed: " + exception.Message);
                return null;
            }
        }

        /// <summary>Asks rust-analyzer to resolve an item that will add an import (its <c>additionalTextEdits</c>).</summary>
        internal static async Task EnsureResolvedAsync(RustCompletionData data, CancellationToken token)
        {
            if (!data.NeedsResolve || RustLanguageClient.Instance?.Rpc is not { } rpc)
            {
                return;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(ResolveTimeout);
            try
            {
                var resolved = await rpc.InvokeWithParameterObjectAsync<JToken?>("completionItem/resolve", data.Item, timeout.Token).ConfigureAwait(false);
                if (resolved is JObject resolvedItem)
                {
                    data.Item = resolvedItem;
                }
            }
            catch (Exception exception) when (exception is OperationCanceledException or RemoteInvocationException or ConnectionLostException or ObjectDisposedException)
            {
                KubunoLog.WriteLine("Completion: completionItem/resolve failed: " + exception.Message);
            }
            finally
            {
                data.Resolved = true;
            }
        }

        internal static void Dbg(string message)
        {
            if (Environment.GetEnvironmentVariable("KUBUNO_COMPLETION_TRACE") == "1")
            {
                KubunoLog.WriteLine("[completion] " + message);
            }
        }

        private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';

        /// <summary>True when <paramref name="position"/> follows a <c>//</c> on its line that is not inside a string.</summary>
        private static bool IsInLineComment(ITextSnapshot snapshot, int position)
        {
            var line = snapshot.GetLineFromPosition(position);
            var text = snapshot.GetText(line.Start.Position, position - line.Start.Position);
            bool inString = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\\' && inString)
                {
                    i++;
                }
                else if (c == '"')
                {
                    inString = !inString;
                }
                else if (!inString && c == '/' && i + 1 < text.Length && text[i + 1] == '/')
                {
                    return true;
                }
            }

            return false;
        }

        private static ImageElement Icon(string monikerName)
        {
            lock (IconCache)
            {
                if (!IconCache.TryGetValue(monikerName, out var image))
                {
                    var moniker = KubunoTreeItem.Moniker(monikerName);
                    image = new ImageElement(new ImageId(moniker.Guid, moniker.Id), monikerName);
                    IconCache[monikerName] = image;
                }

                return image;
            }
        }

        private static Dictionary<string, (CompletionFilter En, CompletionFilter Fr)> CreateFilters()
        {
            var filters = new Dictionary<string, (CompletionFilter, CompletionFilter)>(StringComparer.Ordinal);
            foreach (var info in RustCompletionPresentation.Filters)
            {
                var icon = Icon(info.MonikerName);
                filters[info.Key] = (new CompletionFilter(info.English, info.AccessKey, icon), new CompletionFilter(info.French, info.AccessKey, icon));
            }

            return filters;
        }
    }
}
