using System;
using System.Collections.Generic;
using Kubuno.Desktop.Logic.Sql;
using Microsoft.VisualStudio.Text;

namespace Kubuno.Desktop.LanguageService.Sql
{
    /// <summary>
    /// The SQL literals of one Rust buffer (docs/DATA.md DATA-8), shared by the SQL classifier, the SQL completion and the
    /// unknown-name squiggles. Computed lazily per snapshot: an edit inside a literal's text only re-decodes that literal
    /// (<see cref="RustSqlLiteralScanner.TryApplyEdit"/>), and every other literal keeps its tokens; anything else re-lexes
    /// the file (a linear pass, well under a millisecond for usual files). <see cref="LiteralsChanged"/> reports the
    /// literals whose SQL changed, so that multi-line literals are re-coloured beyond the edited line.
    /// </summary>
    internal sealed class SqlBufferState
    {
        private static readonly IReadOnlyList<SqlStringLiteral> None = new SqlStringLiteral[0];

        private readonly object _gate = new object();
        private ITextSnapshot? _snapshot;
        private IReadOnlyList<SqlStringLiteral> _literals = None;

        private SqlBufferState(ITextBuffer buffer)
        {
            buffer.Changed += OnChanged;
        }

        /// <summary>Raised (on the thread that edited the buffer) with the span of the literals whose SQL changed.</summary>
        public event EventHandler<SnapshotSpanEventArgs>? LiteralsChanged;

        public static SqlBufferState Get(ITextBuffer buffer) =>
            buffer.Properties.GetOrCreateSingletonProperty(typeof(SqlBufferState), () => new SqlBufferState(buffer));

        /// <summary>The SQL literal whose text contains <paramref name="point"/> (both ends included), or null.</summary>
        public static SqlStringLiteral? LiteralAt(SnapshotPoint point)
        {
            var literals = Get(point.Snapshot.TextBuffer).GetLiterals(point.Snapshot);
            int position = point.Position;
            int low = 0;
            int high = literals.Count - 1;
            while (low <= high)
            {
                int mid = (low + high) / 2;
                var literal = literals[mid];
                if (position < literal.ContentStart)
                {
                    high = mid - 1;
                }
                else if (position > literal.ContentEnd)
                {
                    low = mid + 1;
                }
                else
                {
                    return literal;
                }
            }

            return null;
        }

        /// <summary>The literals that intersect <paramref name="span"/>, in order.</summary>
        public static IEnumerable<SqlStringLiteral> Intersecting(IReadOnlyList<SqlStringLiteral> literals, Span span)
        {
            int low = 0;
            int high = literals.Count;
            while (low < high)
            {
                int mid = (low + high) / 2;
                if (literals[mid].End <= span.Start)
                {
                    low = mid + 1;
                }
                else
                {
                    high = mid;
                }
            }

            for (int i = low; i < literals.Count && literals[i].Start < span.End; i++)
            {
                yield return literals[i];
            }
        }

        public IReadOnlyList<SqlStringLiteral> GetLiterals(ITextSnapshot snapshot)
        {
            lock (_gate)
            {
                if (ReferenceEquals(_snapshot, snapshot))
                {
                    return _literals;
                }

                IReadOnlyList<SqlStringLiteral>? result = null;
                if (_snapshot != null && _snapshot.Version.Next is { } next && next.VersionNumber == snapshot.Version.VersionNumber && next.Changes is { Count: 1 } changes)
                {
                    var change = changes[0];
                    result = RustSqlLiteralScanner.TryApplyEdit(_literals, change.OldPosition, change.OldText, change.NewText, snapshot.GetText);
                }

                result ??= RustSqlLiteralScanner.Scan(snapshot.GetText());
                if (_snapshot is null || snapshot.Version.VersionNumber >= _snapshot.Version.VersionNumber)
                {
                    _snapshot = snapshot;
                    _literals = result;
                }

                return result;
            }
        }

        private void OnChanged(object sender, TextContentChangedEventArgs e)
        {
            IReadOnlyList<SqlStringLiteral> before;
            lock (_gate)
            {
                if (!ReferenceEquals(_snapshot, e.Before) || LiteralsChanged is null)
                {
                    // Nobody has looked at the previous version: nothing is coloured from it, nothing to refresh.
                    return;
                }

                before = _literals;
            }

            var after = GetLiterals(e.After);
            int start = int.MaxValue;
            int end = -1;
            var matched = new HashSet<SqlStringLiteral>();
            var byStart = new Dictionary<int, SqlStringLiteral>();
            foreach (var literal in after)
            {
                byStart[literal.Start] = literal;
            }

            foreach (var old in before)
            {
                var moved = new SnapshotSpan(e.Before, old.Start, old.Length).TranslateTo(e.After, SpanTrackingMode.EdgeInclusive);
                if (byStart.TryGetValue(moved.Start.Position, out var current) && current.Length == moved.Length
                    && (ReferenceEquals(current.Sql, old.Sql) || current.Sql.Text == old.Sql.Text))
                {
                    matched.Add(current);
                    continue;
                }

                start = Math.Min(start, moved.Start.Position);
                end = Math.Max(end, moved.End.Position);
            }

            foreach (var literal in after)
            {
                if (!matched.Contains(literal))
                {
                    start = Math.Min(start, literal.Start);
                    end = Math.Max(end, literal.End);
                }
            }

            if (end >= start && end > 0)
            {
                end = Math.Min(end, e.After.Length);
                start = Math.Min(start, end);
                LiteralsChanged?.Invoke(this, new SnapshotSpanEventArgs(new SnapshotSpan(e.After, start, end - start)));
            }
        }
    }
}
