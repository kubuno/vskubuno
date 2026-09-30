using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Runtime.CompilerServices;
using Kubuno.Desktop.Logic.Sql;
using Kubuno.Desktop.Designer;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;
using Microsoft.VisualStudio.Workspace.VSIntegration.Contracts;

namespace Kubuno.Desktop.LanguageService.Sql
{
    /// <summary>
    /// Warning squiggles on SQL names the connection's schema snapshot does not know (docs/DATA.md DATA-8): an unknown
    /// table after FROM/JOIN/UPDATE/INTO, an unknown column after <c>alias.</c>, before <c>cargo sqlx prepare</c> would
    /// fail. Only when a snapshot is loaded, and conservative (<see cref="SqlDiagnostics"/>).
    /// </summary>
    [Export(typeof(ITaggerProvider))]
    [ContentType(Kubuno.Rust.Constants.RustContentType)]
    [TagType(typeof(IErrorTag))]
    [Name("Kubuno SQL unknown names")]
    internal sealed class SqlErrorTaggerProvider : ITaggerProvider
    {
        [Import]
        internal ITextDocumentFactoryService TextDocumentFactory { get; set; } = null!;

        [Import(AllowDefault = true)]
        internal IVsFolderWorkspaceService? WorkspaceService { get; set; }

        public ITagger<T>? CreateTagger<T>(ITextBuffer buffer)
            where T : ITag =>
            buffer.Properties.GetOrCreateSingletonProperty(typeof(SqlErrorTagger), () => new SqlErrorTagger(buffer, this)) as ITagger<T>;
    }

    internal sealed class SqlErrorTagger : ITagger<IErrorTag>
    {
        private static readonly ConditionalWeakTable<SqlText, Cached> Cache = new ConditionalWeakTable<SqlText, Cached>();

        private readonly ITextBuffer _buffer;
        private readonly SqlErrorTaggerProvider _provider;
        private readonly SqlBufferState _state;
        private SqlSchemaSource? _subscribed;

        public SqlErrorTagger(ITextBuffer buffer, SqlErrorTaggerProvider provider)
        {
            _buffer = buffer;
            _provider = provider;
            _state = SqlBufferState.Get(buffer);
            _state.LiteralsChanged += (_, e) => TagsChanged?.Invoke(this, e);
        }

        public event EventHandler<SnapshotSpanEventArgs>? TagsChanged;

        public IEnumerable<ITagSpan<IErrorTag>> GetTags(NormalizedSnapshotSpanCollection spans)
        {
            if (spans.Count == 0)
            {
                yield break;
            }

            var source = Source();
            if (source is null)
            {
                yield break;
            }

            var index = source.Index;
            if (index.IsEmpty)
            {
                yield break;
            }

            var snapshot = spans[0].Snapshot;
            var literals = _state.GetLiterals(snapshot);
            if (literals.Count == 0)
            {
                yield break;
            }

            bool french = DesignerText.IsFrench;
            var seen = new HashSet<SqlStringLiteral>();
            foreach (var span in spans)
            {
                foreach (var literal in SqlBufferState.Intersecting(literals, span.Span))
                {
                    if (!seen.Add(literal))
                    {
                        continue;
                    }

                    foreach (var diagnostic in Diagnostics(literal.Sql, index, source.Version, french))
                    {
                        int start = literal.ToDocument(diagnostic.Start);
                        int end = literal.ToDocument(diagnostic.Start + diagnostic.Length);
                        if (end > start && end <= snapshot.Length)
                        {
                            yield return new TagSpan<IErrorTag>(new SnapshotSpan(snapshot, start, end - start), new ErrorTag(PredefinedErrorTypeNames.Warning, diagnostic.Message));
                        }
                    }
                }
            }
        }

        private static IReadOnlyList<SqlDiagnostic> Diagnostics(SqlText sql, SqlSchemaIndex index, int version, bool french)
        {
            if (Cache.TryGetValue(sql, out var cached) && cached.Version == version && cached.French == french)
            {
                return cached.Diagnostics;
            }

            var diagnostics = SqlDiagnostics.Analyze(sql.Text, sql.Tokens, index, french);
            Cache.Remove(sql);
            Cache.Add(sql, new Cached(version, french, diagnostics));
            return diagnostics;
        }

        private SqlSchemaSource? Source()
        {
            var source = SqlSchemaLocator.For(_buffer, _provider.TextDocumentFactory, _provider.WorkspaceService);
            if (source != null && !ReferenceEquals(source, _subscribed))
            {
                if (_subscribed != null)
                {
                    _subscribed.Changed -= OnSchemaChanged;
                }

                _subscribed = source;
                source.Changed += OnSchemaChanged;
            }

            return source;
        }

        private void OnSchemaChanged(object? sender, EventArgs e)
        {
#pragma warning disable VSSDK007 // fire-and-forget from a file watcher; FileAndForget reports any fault.
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                var snapshot = _buffer.CurrentSnapshot;
                TagsChanged?.Invoke(this, new SnapshotSpanEventArgs(new SnapshotSpan(snapshot, 0, snapshot.Length)));
            }).FileAndForget("Kubuno/SqlSchemaChanged");
#pragma warning restore VSSDK007
        }

        private sealed class Cached
        {
            public Cached(int version, bool french, IReadOnlyList<SqlDiagnostic> diagnostics)
            {
                Version = version;
                French = french;
                Diagnostics = diagnostics;
            }

            public int Version { get; }

            public bool French { get; }

            public IReadOnlyList<SqlDiagnostic> Diagnostics { get; }
        }
    }
}
