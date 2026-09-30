using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.VisualStudio.Core.Sql;
using Kubuno.VisualStudio.Designer;
using Kubuno.VisualStudio.SolutionExplorer;
using Microsoft.VisualStudio.Core.Imaging;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using Microsoft.VisualStudio.Workspace.VSIntegration.Contracts;

namespace Kubuno.VisualStudio.LanguageService.Sql
{
    /// <summary>
    /// SQL completion inside the query strings of Rust code (docs/DATA.md DATA-8): keywords, and from the connection's
    /// schema snapshot the tables after FROM/JOIN/INTO/UPDATE, the columns of the statement's tables in the select list,
    /// conditions, SET, ORDER BY..., the columns of <c>alias.</c>, the tables of <c>schema.</c>, the functions. Beside
    /// <see cref="IntelliSense.RustCompletionSource"/>, which stays out of SQL strings (rust-analyzer has nothing there).
    /// </summary>
    [Export(typeof(IAsyncCompletionSourceProvider))]
    [Name("Kubuno SQL Completion")]
    [ContentType(Constants.RustContentType)]
    [Order(Before = "Kubuno Rust Completion")]
    internal sealed class SqlCompletionSourceProvider : IAsyncCompletionSourceProvider
    {
        [Import]
        internal ITextDocumentFactoryService TextDocumentFactory { get; set; } = null!;

        [Import(AllowDefault = true)]
        internal IVsFolderWorkspaceService? WorkspaceService { get; set; }

        public IAsyncCompletionSource GetOrCreate(ITextView textView) =>
            textView.Properties.GetOrCreateSingletonProperty(() => new SqlCompletionSource(this));
    }

    internal sealed class SqlCompletionSource : IAsyncCompletionSource
    {
        /// <summary>The characters that commit a SQL item (never a space, never <c>.</c>: <c>c.</c> must stay an alias).</summary>
        public const string CommitCharacters = "(),;";

        private const string SessionKey = "Kubuno.SqlCompletion";

        private static readonly Dictionary<string, ImageElement> Icons = new Dictionary<string, ImageElement>(StringComparer.Ordinal);

        private readonly SqlCompletionSourceProvider _provider;

        public SqlCompletionSource(SqlCompletionSourceProvider provider)
        {
            _provider = provider;
        }

        /// <summary>True when the session lists SQL items (the Rust commit manager then uses <see cref="CommitCharacters"/>).</summary>
        public static bool IsSqlSession(IAsyncCompletionSession session) => session.Properties.ContainsProperty(SessionKey);

        /// <summary>True when <paramref name="point"/> is inside the text of a SQL string (the Rust completion then stays out).</summary>
        public static bool IsInSql(SnapshotPoint point) => SqlBufferState.LiteralAt(point) != null;

        public CompletionStartData InitializeCompletion(CompletionTrigger trigger, SnapshotPoint triggerLocation, CancellationToken token)
        {
            var literal = SqlBufferState.LiteralAt(triggerLocation);
            if (literal is null)
            {
                return CompletionStartData.DoesNotParticipateInCompletion;
            }

            var sql = literal.Sql;
            int offset = literal.ToSql(triggerLocation.Position);
            switch (trigger.Reason)
            {
                case CompletionTriggerReason.Insertion:
                    char c = trigger.Character;
                    if (c == ' ')
                    {
                        var source = SqlSchemaLocator.For(triggerLocation.Snapshot.TextBuffer, _provider.TextDocumentFactory, _provider.WorkspaceService);
                        bool loaded = source != null && !source.Index.IsEmpty;
                        if (!SqlCompletion.IsSpaceTrigger(sql.Text, sql.Tokens, offset, loaded))
                        {
                            return CompletionStartData.DoesNotParticipateInCompletion;
                        }
                    }
                    else if (!(char.IsLetter(c) || c == '_' || c == '.'))
                    {
                        return CompletionStartData.DoesNotParticipateInCompletion;
                    }
                    else if (c != '.' && offset >= 2 && char.IsDigit(sql.Text[offset - 2]) && !IsIdentifierPartBefore(sql.Text, offset - 2))
                    {
                        // `1e`, `10d`: a number, not a name.
                        return CompletionStartData.DoesNotParticipateInCompletion;
                    }

                    break;
                case CompletionTriggerReason.Invoke:
                case CompletionTriggerReason.InvokeAndCommitIfUnique:
                    break;
                default:
                    return CompletionStartData.DoesNotParticipateInCompletion;
            }

            var context = SqlStatementAnalyzer.GetCompletionContext(sql.Text, sql.Tokens, offset);
            if (context.Kind == SqlCompletionContextKind.None)
            {
                return CompletionStartData.DoesNotParticipateInCompletion;
            }

            var snapshot = triggerLocation.Snapshot;
            int start = triggerLocation.Position;
            while (start > literal.ContentStart && IsIdentifierChar(snapshot[start - 1]))
            {
                start--;
            }

            int end = triggerLocation.Position;
            while (end < literal.ContentEnd && IsIdentifierChar(snapshot[end]))
            {
                end++;
            }

            return new CompletionStartData(CompletionParticipation.ProvidesItems, new SnapshotSpan(snapshot, start, end - start));
        }

        public Task<CompletionContext> GetCompletionContextAsync(IAsyncCompletionSession session, CompletionTrigger trigger, SnapshotPoint triggerLocation, SnapshotSpan applicableToSpan, CancellationToken token)
        {
            var literal = SqlBufferState.LiteralAt(triggerLocation);
            if (literal is null)
            {
                return Task.FromResult(CompletionContext.Empty);
            }

            session.Properties[SessionKey] = true;
            var source = SqlSchemaLocator.For(triggerLocation.Snapshot.TextBuffer, _provider.TextDocumentFactory, _provider.WorkspaceService);
            var index = source?.Index ?? SqlSchemaIndex.Empty;
            bool french = DesignerText.IsFrench;
            var sql = literal.Sql;
            var entries = SqlCompletion.GetEntries(sql.Text, sql.Tokens, literal.ToSql(triggerLocation.Position), index, french, !literal.IsRaw, out _);
            if (entries.Count == 0)
            {
                return Task.FromResult(CompletionContext.Empty);
            }

            var items = ImmutableArray.CreateBuilder<CompletionItem>(entries.Count);
            foreach (var entry in entries)
            {
                var item = new CompletionItem(
                    entry.DisplayText,
                    this,
                    Icon(entry.IconMonikerName),
                    ImmutableArray<CompletionFilter>.Empty,
                    entry.Suffix,
                    entry.InsertText,
                    entry.SortText,
                    entry.DisplayText,
                    entry.DisplayText,
                    ImmutableArray<ImageElement>.Empty,
                    ImmutableArray<char>.Empty,
                    applicableToSpan,
                    false,
                    false);
                item.Properties.AddProperty(typeof(SqlCompletionEntry), entry);
                items.Add(item);
            }

            // After a space nothing is typed yet: suggest without taking Enter from the user.
            var selection = trigger.Reason == CompletionTriggerReason.Insertion && trigger.Character == ' '
                ? InitialSelectionHint.SoftSelection
                : InitialSelectionHint.RegularSelection;
            return Task.FromResult(new CompletionContext(items.ToImmutable(), null, selection));
        }

        public Task<object> GetDescriptionAsync(IAsyncCompletionSession session, CompletionItem item, CancellationToken token) =>
            Task.FromResult<object>(item.Properties.TryGetProperty(typeof(SqlCompletionEntry), out SqlCompletionEntry entry) ? entry.Description : string.Empty);

        private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';

        /// <summary>True when the digit at <paramref name="index"/> belongs to a name (<c>t1</c>), not a number.</summary>
        private static bool IsIdentifierPartBefore(string text, int index)
        {
            int i = index;
            while (i >= 0 && IsIdentifierChar(text[i]))
            {
                i--;
            }

            return i + 1 <= index && (char.IsLetter(text[i + 1]) || text[i + 1] == '_');
        }

        private static ImageElement Icon(string monikerName)
        {
            lock (Icons)
            {
                if (!Icons.TryGetValue(monikerName, out var image))
                {
                    var moniker = KubunoTreeItem.Moniker(monikerName);
                    image = new ImageElement(new ImageId(moniker.Guid, moniker.Id), monikerName);
                    Icons[monikerName] = image;
                }

                return image;
            }
        }
    }
}
