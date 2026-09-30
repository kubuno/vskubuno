using System;
using System.Collections.Generic;
using System.Threading;

namespace Kubuno.Desktop.Logic.Sql
{
    /// <summary>
    /// A Rust string literal recognised as SQL (the query of <c>sqlx::query!</c>, <c>DbCommand::with_text</c>...).
    /// Offsets are absolute positions in the Rust document; the SQL itself (<see cref="Sql"/>) is kept relative to
    /// <see cref="ContentStart"/> so that a literal shifted by an edit before it keeps its decoded text and tokens.
    /// </summary>
    public sealed class SqlStringLiteral
    {
        public SqlStringLiteral(int start, int length, int contentStart, int contentLength, int hashes, bool isRaw, string origin, SqlText sql)
        {
            Start = start;
            Length = length;
            ContentStart = contentStart;
            ContentLength = contentLength;
            Hashes = hashes;
            IsRaw = isRaw;
            Origin = origin;
            Sql = sql;
        }

        /// <summary>The literal's first character (the <c>r</c> of <c>r#"</c> or the opening quote).</summary>
        public int Start { get; }

        /// <summary>The literal's whole length, quotes and hashes included (up to the end of the text when unterminated).</summary>
        public int Length { get; }

        public int End => Start + Length;

        /// <summary>The first character after the opening quote.</summary>
        public int ContentStart { get; }

        public int ContentLength { get; }

        public int ContentEnd => ContentStart + ContentLength;

        /// <summary>The number of <c>#</c> of a raw string (<c>r#"…"#</c> = 1).</summary>
        public int Hashes { get; }

        public bool IsRaw { get; }

        /// <summary>What made the literal SQL, e.g. <c>sqlx::query_as!</c>, <c>DbCommand::with_text</c>, <c>select_command</c>.</summary>
        public string Origin { get; }

        /// <summary>The decoded SQL (escapes resolved), with its tokens.</summary>
        public SqlText Sql { get; }

        /// <summary>True when <paramref name="position"/> is inside the content (both ends included).</summary>
        public bool ContainsInContent(int position) => position >= ContentStart && position <= ContentEnd;

        /// <summary>The same literal moved by <paramref name="delta"/> characters (text and tokens shared).</summary>
        public SqlStringLiteral Shift(int delta) =>
            delta == 0 ? this : new SqlStringLiteral(Start + delta, Length, ContentStart + delta, ContentLength, Hashes, IsRaw, Origin, Sql);

        /// <summary>The absolute document position of a decoded SQL offset.</summary>
        public int ToDocument(int sqlOffset) => ContentStart + Sql.ToContentOffset(sqlOffset);

        /// <summary>The decoded SQL offset of an absolute document position inside the content.</summary>
        public int ToSql(int documentPosition) => Sql.FromContentOffset(documentPosition - ContentStart);

        public override string ToString() => Origin + " @" + Start + ": " + Sql.Text;
    }

    /// <summary>
    /// The SQL of a literal: the decoded text and, for each decoded character, its offset in the literal's content
    /// (they differ only where a plain string has escapes: <c>\"</c>, <c>\n</c>, a line continuation...).
    /// </summary>
    public sealed class SqlText
    {
        private readonly int[]? _map;
        private IReadOnlyList<SqlToken>? _tokens;

        /// <param name="text">The decoded SQL.</param>
        /// <param name="map">Content offset of each decoded character plus one final entry (the content length); null when identical.</param>
        public SqlText(string text, int[]? map)
        {
            Text = text ?? throw new ArgumentNullException(nameof(text));
            if (map != null && map.Length != text.Length + 1)
            {
                throw new ArgumentException("The map needs one entry per character plus one.", nameof(map));
            }

            _map = map;
        }

        public string Text { get; }

        /// <summary>The SQL tokens (computed once, on first use, thread-safe).</summary>
        public IReadOnlyList<SqlToken> Tokens
        {
            get
            {
                var tokens = Volatile.Read(ref _tokens);
                if (tokens is null)
                {
                    tokens = SqlTokenizer.Tokenize(Text);
                    Interlocked.CompareExchange(ref _tokens, tokens, null);
                }

                return tokens;
            }
        }

        public int ToContentOffset(int sqlOffset)
        {
            if (sqlOffset < 0)
            {
                return 0;
            }

            if (_map is null)
            {
                return Math.Min(sqlOffset, Text.Length);
            }

            return _map[Math.Min(sqlOffset, Text.Length)];
        }

        /// <summary>The decoded offset of a content offset (inside an escape: the escape's decoded character).</summary>
        public int FromContentOffset(int contentOffset)
        {
            if (_map is null)
            {
                return Math.Max(0, Math.Min(contentOffset, Text.Length));
            }

            int low = 0;
            int high = _map.Length - 1;
            if (contentOffset <= _map[0])
            {
                return 0;
            }

            while (low < high)
            {
                int mid = (low + high + 1) / 2;
                if (_map[mid] <= contentOffset)
                {
                    low = mid;
                }
                else
                {
                    high = mid - 1;
                }
            }

            return low;
        }
    }
}
