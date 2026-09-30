using System;
using System.Collections.Generic;

namespace Kubuno.VisualStudio.Core.Sql
{
    public enum SqlTokenKind
    {
        Keyword,
        Identifier,
        QuotedIdentifier,
        Function,
        Number,
        String,
        Comment,
        Parameter,
        Operator,
        Punctuation,
    }

    /// <summary>A SQL token: offsets in the decoded SQL text.</summary>
    public readonly struct SqlToken
    {
        public SqlToken(SqlTokenKind kind, int start, int length)
        {
            Kind = kind;
            Start = start;
            Length = length;
        }

        public SqlTokenKind Kind { get; }

        public int Start { get; }

        public int Length { get; }

        public int End => Start + Length;

        public string GetText(string sql) => sql.Substring(Start, Length);

        /// <summary>The name an identifier or quoted identifier denotes (quotes removed, <c>""</c> unescaped).</summary>
        public string GetName(string sql)
        {
            if (Kind != SqlTokenKind.QuotedIdentifier || Length < 2)
            {
                return GetText(sql);
            }

            char close = sql[Start] == '[' ? ']' : sql[Start];
            int length = sql[End - 1] == close ? Length - 2 : Length - 1;
            var inner = sql.Substring(Start + 1, Math.Max(0, length));
            return inner.Replace(new string(close, 2), close.ToString());
        }

        public bool IsPunctuation(string sql, char c) => Kind == SqlTokenKind.Punctuation && sql[Start] == c;

        public bool IsKeyword(string sql, string keyword) =>
            Kind == SqlTokenKind.Keyword && Length == keyword.Length && string.Compare(sql, Start, keyword, 0, Length, StringComparison.OrdinalIgnoreCase) == 0;

        public override string ToString() => Kind + "@" + Start + "+" + Length;
    }

    /// <summary>
    /// A dialect-tolerant SQL tokenizer (PostgreSQL, SQLite, MySQL, SQL Server): keywords, identifiers (quoted with
    /// <c>"…"</c> or backticks), built-in and called functions, numbers, <c>'…'</c> and dollar-quoted strings, <c>--</c>
    /// and nested <c>/* */</c> comments, and every parameter style (<c>$1</c>, <c>?</c>, <c>?1</c>, <c>@name</c>,
    /// <c>:name</c>). Blanks are not tokens.
    /// </summary>
    public static class SqlTokenizer
    {
        public static IReadOnlyList<SqlToken> Tokenize(string sql)
        {
            if (sql is null)
            {
                throw new ArgumentNullException(nameof(sql));
            }

            var tokens = new List<SqlToken>(Math.Max(8, sql.Length / 4));
            int n = sql.Length;
            int i = 0;
            while (i < n)
            {
                char c = sql[i];
                if (char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }

                int start = i;
                if (c == '-' && i + 1 < n && sql[i + 1] == '-')
                {
                    while (i < n && sql[i] != '\n' && sql[i] != '\r')
                    {
                        i++;
                    }

                    tokens.Add(new SqlToken(SqlTokenKind.Comment, start, i - start));
                    continue;
                }

                if (c == '/' && i + 1 < n && sql[i + 1] == '*')
                {
                    int depth = 1;
                    i += 2;
                    while (i < n && depth > 0)
                    {
                        if (sql[i] == '/' && i + 1 < n && sql[i + 1] == '*')
                        {
                            depth++;
                            i += 2;
                        }
                        else if (sql[i] == '*' && i + 1 < n && sql[i + 1] == '/')
                        {
                            depth--;
                            i += 2;
                        }
                        else
                        {
                            i++;
                        }
                    }

                    tokens.Add(new SqlToken(SqlTokenKind.Comment, start, i - start));
                    continue;
                }

                if (c == '\'')
                {
                    i = Quoted(sql, i, '\'');
                    tokens.Add(new SqlToken(SqlTokenKind.String, start, i - start));
                    continue;
                }

                if (c == '"' || c == '`')
                {
                    i = Quoted(sql, i, c);
                    tokens.Add(new SqlToken(SqlTokenKind.QuotedIdentifier, start, i - start));
                    continue;
                }

                if (c == '$')
                {
                    if (i + 1 < n && char.IsDigit(sql[i + 1]))
                    {
                        i++;
                        while (i < n && char.IsDigit(sql[i]))
                        {
                            i++;
                        }

                        tokens.Add(new SqlToken(SqlTokenKind.Parameter, start, i - start));
                        continue;
                    }

                    int tagEnd = DollarTagEnd(sql, i);
                    if (tagEnd > 0)
                    {
                        var tag = sql.Substring(i, tagEnd - i);
                        int close = sql.IndexOf(tag, tagEnd, StringComparison.Ordinal);
                        i = close < 0 ? n : close + tag.Length;
                        tokens.Add(new SqlToken(SqlTokenKind.String, start, i - start));
                        continue;
                    }
                }

                if (c == '?')
                {
                    i++;
                    while (i < n && char.IsDigit(sql[i]))
                    {
                        i++;
                    }

                    tokens.Add(new SqlToken(SqlTokenKind.Parameter, start, i - start));
                    continue;
                }

                if ((c == '@' || c == ':') && i + 1 < n && IsIdentifierStart(sql[i + 1]) && !(i > 0 && (sql[i - 1] == ':' || sql[i - 1] == '@')))
                {
                    i++;
                    while (i < n && IsIdentifierPart(sql[i]))
                    {
                        i++;
                    }

                    tokens.Add(new SqlToken(SqlTokenKind.Parameter, start, i - start));
                    continue;
                }

                if (char.IsDigit(c) || (c == '.' && i + 1 < n && char.IsDigit(sql[i + 1]) && !(i > 0 && IsIdentifierPart(sql[i - 1]))))
                {
                    i = Number(sql, i);
                    tokens.Add(new SqlToken(SqlTokenKind.Number, start, i - start));
                    continue;
                }

                if (IsIdentifierStart(c))
                {
                    i++;
                    while (i < n && (IsIdentifierPart(sql[i]) || sql[i] == '$'))
                    {
                        i++;
                    }

                    tokens.Add(new SqlToken(SqlTokenKind.Identifier, start, i - start));
                    continue;
                }

                if (c == '(' || c == ')' || c == ',' || c == ';' || c == '.')
                {
                    tokens.Add(new SqlToken(SqlTokenKind.Punctuation, start, 1));
                    i++;
                    continue;
                }

                i++;
                while (i < n && IsOperatorChar(sql[i]) && !(sql[i] == '-' && i + 1 < n && sql[i + 1] == '-') && !(sql[i] == '/' && i + 1 < n && sql[i + 1] == '*'))
                {
                    i++;
                }

                tokens.Add(new SqlToken(SqlTokenKind.Operator, start, i - start));
            }

            Classify(sql, tokens);
            return tokens;
        }

        /// <summary>Identifiers become keywords or functions from their text and neighbours.</summary>
        private static void Classify(string sql, List<SqlToken> tokens)
        {
            for (int k = 0; k < tokens.Count; k++)
            {
                var token = tokens[k];
                if (token.Kind != SqlTokenKind.Identifier)
                {
                    continue;
                }

                int previous = PreviousSignificant(tokens, k);
                int next = NextSignificant(tokens, k);
                // Only an adjacent dot qualifies: in `c. FROM t` (being typed) FROM is still a keyword.
                bool afterDot = previous >= 0 && tokens[previous].IsPunctuation(sql, '.') && tokens[previous].End == token.Start;
                bool beforeDot = next >= 0 && tokens[next].IsPunctuation(sql, '.') && token.End == tokens[next].Start;
                bool beforeParen = next >= 0 && tokens[next].IsPunctuation(sql, '(');
                var text = token.GetText(sql);

                if (afterDot || beforeDot)
                {
                    // `c.name`, `main.customers`, `schema.f(…)`: names, never keywords.
                    if (afterDot && beforeParen)
                    {
                        tokens[k] = new SqlToken(SqlTokenKind.Function, token.Start, token.Length);
                    }

                    continue;
                }

                if (SqlLanguage.IsFunction(text) && (beforeParen || SqlLanguage.IsNiladicFunction(text)))
                {
                    tokens[k] = new SqlToken(SqlTokenKind.Function, token.Start, token.Length);
                }
                else if (SqlLanguage.IsKeyword(text))
                {
                    tokens[k] = new SqlToken(SqlTokenKind.Keyword, token.Start, token.Length);
                }
                else if (beforeParen && !(previous >= 0 && IsTableKeyword(sql, tokens[previous])))
                {
                    // `my_function(…)` - but not `INSERT INTO t(a, b)`, `CREATE TABLE t (`, `REFERENCES t(id)`.
                    tokens[k] = new SqlToken(SqlTokenKind.Function, token.Start, token.Length);
                }
            }
        }

        private static bool IsTableKeyword(string sql, SqlToken token)
        {
            if (token.Kind != SqlTokenKind.Identifier && token.Kind != SqlTokenKind.Keyword)
            {
                return false;
            }

            var text = token.GetText(sql);
            return string.Equals(text, "INTO", StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, "TABLE", StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, "REFERENCES", StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, "ON", StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, "INDEX", StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, "VIEW", StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, "EXISTS", StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, "JOIN", StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, "FROM", StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, "UPDATE", StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, "AS", StringComparison.OrdinalIgnoreCase);
        }

        internal static int PreviousSignificant(IReadOnlyList<SqlToken> tokens, int k)
        {
            for (int i = k - 1; i >= 0; i--)
            {
                if (tokens[i].Kind != SqlTokenKind.Comment)
                {
                    return i;
                }
            }

            return -1;
        }

        internal static int NextSignificant(IReadOnlyList<SqlToken> tokens, int k)
        {
            for (int i = k + 1; i < tokens.Count; i++)
            {
                if (tokens[i].Kind != SqlTokenKind.Comment)
                {
                    return i;
                }
            }

            return -1;
        }

        private static int Quoted(string sql, int i, char quote)
        {
            int n = sql.Length;
            i++;
            while (i < n)
            {
                if (sql[i] == quote)
                {
                    if (i + 1 < n && sql[i + 1] == quote)
                    {
                        i += 2;
                        continue;
                    }

                    return i + 1;
                }

                i++;
            }

            return n;
        }

        /// <summary>The end of a <c>$tag$</c> / <c>$$</c> opener at <paramref name="i"/>, or -1.</summary>
        private static int DollarTagEnd(string sql, int i)
        {
            int j = i + 1;
            while (j < sql.Length && (char.IsLetterOrDigit(sql[j]) || sql[j] == '_'))
            {
                if (j == i + 1 && char.IsDigit(sql[j]))
                {
                    return -1;
                }

                j++;
            }

            return j < sql.Length && sql[j] == '$' ? j + 1 : -1;
        }

        private static int Number(string sql, int i)
        {
            int n = sql.Length;
            if (sql[i] == '0' && i + 1 < n && (sql[i + 1] == 'x' || sql[i + 1] == 'X'))
            {
                i += 2;
                while (i < n && Uri.IsHexDigit(sql[i]))
                {
                    i++;
                }

                return i;
            }

            while (i < n && (char.IsDigit(sql[i]) || sql[i] == '_'))
            {
                i++;
            }

            if (i < n && sql[i] == '.' && !(i + 1 < n && sql[i + 1] == '.'))
            {
                i++;
                while (i < n && char.IsDigit(sql[i]))
                {
                    i++;
                }
            }

            if (i < n && (sql[i] == 'e' || sql[i] == 'E'))
            {
                int j = i + 1;
                if (j < n && (sql[j] == '+' || sql[j] == '-'))
                {
                    j++;
                }

                if (j < n && char.IsDigit(sql[j]))
                {
                    i = j;
                    while (i < n && char.IsDigit(sql[i]))
                    {
                        i++;
                    }
                }
            }

            return i;
        }

        private static bool IsOperatorChar(char c) => "+-*/<>=~!@#%^&|?:[]{}".IndexOf(c) >= 0;

        internal static bool IsIdentifierStart(char c) => c == '_' || char.IsLetter(c);

        internal static bool IsIdentifierPart(char c) => c == '_' || char.IsLetterOrDigit(c);
    }
}
