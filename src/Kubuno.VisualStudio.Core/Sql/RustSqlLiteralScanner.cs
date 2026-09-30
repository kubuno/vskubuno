using System;
using System.Collections.Generic;
using System.Text;

namespace Kubuno.VisualStudio.Core.Sql
{
    /// <summary>
    /// Finds the Rust string literals that hold SQL: the query of the sqlx macros (<c>query!</c>, <c>query_as!</c> after
    /// the row type, <c>query_scalar!</c> and their <c>_unchecked</c> forms, with or without <c>sqlx::</c>), of the sqlx
    /// functions (<c>sqlx::query(</c>, <c>sqlx::query_as::&lt;…&gt;(</c>, <c>sqlx::query_scalar(</c>, <c>raw_sql</c>...), and
    /// of kubuno-data (<c>DbCommand::with_text(</c>, <c>TableAdapter::new(conn, </c>, the <c>command_text</c> /
    /// <c>select_command</c> / <c>insert_command</c> / <c>update_command</c> / <c>delete_command</c> fields).
    /// A small Rust lexer (line and nested block comments, char literals versus lifetimes, byte/C strings, raw strings with
    /// any number of <c>#</c>, raw identifiers) keeps <c>query!(</c> inside a comment or a string from counting.
    /// </summary>
    public static class RustSqlLiteralScanner
    {
        private static readonly Dictionary<string, int> MacroArgument = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["query"] = 0,
            ["query_unchecked"] = 0,
            ["query_scalar"] = 0,
            ["query_scalar_unchecked"] = 0,
            ["query_as"] = 1,
            ["query_as_unchecked"] = 1,
        };

        private static readonly HashSet<string> SqlxFunctions = new HashSet<string>(StringComparer.Ordinal)
        {
            "query", "query_as", "query_scalar", "query_with", "query_as_with", "query_scalar_with", "raw_sql",
        };

        private static readonly HashSet<string> CommandFields = new HashSet<string>(StringComparer.Ordinal)
        {
            "command_text", "select_command", "insert_command", "update_command", "delete_command",
        };

        private static readonly HashSet<string> StatementKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SELECT", "INSERT", "UPDATE", "DELETE", "WITH", "CREATE", "ALTER", "DROP", "TRUNCATE", "REPLACE", "MERGE",
            "EXPLAIN", "PRAGMA", "VALUES", "CALL", "EXEC", "EXECUTE", "GRANT", "REVOKE", "BEGIN", "COMMIT", "ROLLBACK",
        };

        private enum TokenKind
        {
            Identifier,
            Punctuation,
            String,
            OtherLiteral,
        }

        private struct Token
        {
            public TokenKind Kind;
            public int Start;
            public int Length;
            public int ContentStart;
            public int ContentLength;
            public int Hashes;
            public bool Raw;

            public char Char(string text) => text[Start];
        }

        /// <summary>The SQL literals of a Rust source text, in document order.</summary>
        public static IReadOnlyList<SqlStringLiteral> Scan(string text)
        {
            if (text is null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            var tokens = Lex(text);
            var result = new List<SqlStringLiteral>();
            for (int k = 0; k < tokens.Count; k++)
            {
                if (tokens[k].Kind != TokenKind.Identifier)
                {
                    continue;
                }

                var name = Text(text, tokens[k]);
                int str = -1;
                string? origin = null;
                bool needsSqlLook = false;

                if (MacroArgument.TryGetValue(name, out int argument) && IsPunct(text, tokens, k + 1, '!') && IsOpenDelimiter(text, tokens, k + 2))
                {
                    str = argument == 0 ? k + 3 : NextArgument(text, tokens, k + 3);
                    origin = (HasSqlxPrefix(text, tokens, k) ? "sqlx::" : string.Empty) + name + "!";
                }
                else if (SqlxFunctions.Contains(name) && !IsPunct(text, tokens, k + 1, '!') && !IsKeywordFn(text, tokens, k - 1))
                {
                    int open = SkipTurbofish(text, tokens, k + 1);
                    if (IsPunct(text, tokens, open, '('))
                    {
                        str = open + 1;
                        bool prefixed = HasSqlxPrefix(text, tokens, k);
                        origin = (prefixed ? "sqlx::" : string.Empty) + name;
                        needsSqlLook = !prefixed;
                    }
                }
                else if (name == "with_text" && IsPath(text, tokens, k, "DbCommand") && IsPunct(text, tokens, k + 1, '('))
                {
                    str = k + 2;
                    origin = "DbCommand::with_text";
                }
                else if (name == "new" && IsPath(text, tokens, k, "TableAdapter") && IsPunct(text, tokens, k + 1, '('))
                {
                    str = NextArgument(text, tokens, k + 2);
                    origin = "TableAdapter::new";
                }
                else if (CommandFields.Contains(name) && !IsPunct(text, tokens, k - 1, ':'))
                {
                    // `cmd.command_text = "…"` or `DbCommand { command_text: "…".into(), .. }`: the next token must be the string.
                    if (IsPunct(text, tokens, k + 1, '=') || IsPunct(text, tokens, k + 1, ':'))
                    {
                        str = k + 2;
                        origin = name;
                    }
                }

                if (str < 0 || str >= tokens.Count || tokens[str].Kind != TokenKind.String || origin is null)
                {
                    continue;
                }

                var literal = Create(text, tokens[str], origin);
                if (needsSqlLook && !LooksLikeSql(literal.Sql.Text))
                {
                    continue;
                }

                result.Add(literal);
            }

            return result;
        }

        /// <summary>
        /// The literals after a single edit, without lexing the document again, when the edit stays inside one literal's
        /// content and types no character that can end or reshape it (<c>"</c>, <c>\</c>, <c>#</c>); null otherwise (the
        /// caller then scans the whole text again). <paramref name="getText"/> reads the new document (start, length).
        /// </summary>
        public static IReadOnlyList<SqlStringLiteral>? TryApplyEdit(IReadOnlyList<SqlStringLiteral> previous, int position, string oldText, string newText, Func<int, int, string> getText)
        {
            if (previous is null || oldText is null || newText is null || getText is null)
            {
                return null;
            }

            if (HasSensitiveCharacter(oldText) || HasSensitiveCharacter(newText))
            {
                return null;
            }

            int oldEnd = position + oldText.Length;
            int index = -1;
            for (int i = 0; i < previous.Count; i++)
            {
                var literal = previous[i];
                if (literal.ContentStart <= position && oldEnd <= literal.ContentEnd)
                {
                    index = i;
                    break;
                }

                if (literal.Start >= oldEnd)
                {
                    break;
                }
            }

            if (index < 0)
            {
                return null;
            }

            int delta = newText.Length - oldText.Length;
            var old = previous[index];
            int contentLength = old.ContentLength + delta;
            var content = getText(old.ContentStart, contentLength);
            var sql = old.IsRaw ? new SqlText(content, null) : Decode(content, 0, content.Length);
            if (SqlxFunctions.Contains(old.Origin) && !LooksLikeSql(sql.Text))
            {
                // A bare `query(` only counts when its text reads like SQL: the edit may have changed that.
                return null;
            }

            var result = new List<SqlStringLiteral>(previous.Count);
            for (int i = 0; i < previous.Count; i++)
            {
                if (i < index)
                {
                    result.Add(previous[i]);
                }
                else if (i == index)
                {
                    result.Add(new SqlStringLiteral(old.Start, old.Length + delta, old.ContentStart, contentLength, old.Hashes, old.IsRaw, old.Origin, sql));
                }
                else
                {
                    result.Add(previous[i].Shift(delta));
                }
            }

            return result;
        }

        /// <summary>True when the text starts (after blanks and comments) with a SQL statement keyword.</summary>
        public static bool LooksLikeSql(string sql)
        {
            int i = 0;
            while (i < sql.Length)
            {
                if (char.IsWhiteSpace(sql[i]) || sql[i] == '(')
                {
                    i++;
                }
                else if (sql[i] == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
                {
                    while (i < sql.Length && sql[i] != '\n')
                    {
                        i++;
                    }
                }
                else if (sql[i] == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
                {
                    int end = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    i = end < 0 ? sql.Length : end + 2;
                }
                else
                {
                    break;
                }
            }

            int start = i;
            while (i < sql.Length && char.IsLetter(sql[i]))
            {
                i++;
            }

            return i > start && (i == sql.Length || !char.IsLetterOrDigit(sql[i]) && sql[i] != '_') && StatementKeywords.Contains(sql.Substring(start, i - start));
        }

        private static bool HasSensitiveCharacter(string text) => text.IndexOf('"') >= 0 || text.IndexOf('\\') >= 0 || text.IndexOf('#') >= 0;

        private static SqlStringLiteral Create(string text, Token token, string origin)
        {
            var sql = token.Raw
                ? new SqlText(text.Substring(token.ContentStart, token.ContentLength), null)
                : Decode(text, token.ContentStart, token.ContentLength);
            return new SqlStringLiteral(token.Start, token.Length, token.ContentStart, token.ContentLength, token.Hashes, token.Raw, origin, sql);
        }

        /// <summary>Resolves a plain string's escapes, keeping each decoded character's content offset.</summary>
        internal static SqlText Decode(string text, int start, int length)
        {
            if (text.IndexOf('\\', start, length) < 0)
            {
                return new SqlText(text.Substring(start, length), null);
            }

            var builder = new StringBuilder(length);
            var map = new List<int>(length + 1);
            int end = start + length;
            int i = start;
            while (i < end)
            {
                char c = text[i];
                if (c != '\\' || i + 1 >= end)
                {
                    builder.Append(c);
                    map.Add(i - start);
                    i++;
                    continue;
                }

                char e = text[i + 1];
                int at = i - start;
                switch (e)
                {
                    case 'n':
                        builder.Append('\n');
                        map.Add(at);
                        i += 2;
                        break;
                    case 'r':
                        builder.Append('\r');
                        map.Add(at);
                        i += 2;
                        break;
                    case 't':
                        builder.Append('\t');
                        map.Add(at);
                        i += 2;
                        break;
                    case '0':
                        builder.Append('\0');
                        map.Add(at);
                        i += 2;
                        break;
                    case '\r':
                    case '\n':
                        // A line continuation: the line break and the next line's leading blanks are not in the string.
                        i += 1;
                        while (i < end && char.IsWhiteSpace(text[i]))
                        {
                            i++;
                        }

                        break;
                    case 'x':
                    {
                        int j = i + 2;
                        int value = 0;
                        while (j < end && j < i + 4 && Uri.IsHexDigit(text[j]))
                        {
                            value = (value * 16) + Convert.ToInt32(text[j].ToString(), 16);
                            j++;
                        }

                        builder.Append((char)value);
                        map.Add(at);
                        i = j;
                        break;
                    }

                    case 'u':
                    {
                        int j = i + 2;
                        int value = 0;
                        if (j < end && text[j] == '{')
                        {
                            j++;
                            while (j < end && text[j] != '}')
                            {
                                if (Uri.IsHexDigit(text[j]))
                                {
                                    value = (value * 16) + Convert.ToInt32(text[j].ToString(), 16);
                                }

                                j++;
                            }

                            j = Math.Min(end, j + 1);
                        }

                        var decoded = value <= 0x10FFFF && (value < 0xD800 || value > 0xDFFF) ? char.ConvertFromUtf32(value) : "?";
                        foreach (var ch in decoded)
                        {
                            builder.Append(ch);
                            map.Add(at);
                        }

                        i = j;
                        break;
                    }

                    default:
                        // \" \' \\ and anything unknown: the character itself.
                        builder.Append(e);
                        map.Add(at);
                        i += 2;
                        break;
                }
            }

            map.Add(length);
            return new SqlText(builder.ToString(), map.ToArray());
        }

        private static string Text(string text, Token token) => text.Substring(token.Start, token.Length);

        private static bool IsPunct(string text, List<Token> tokens, int index, char c) =>
            index >= 0 && index < tokens.Count && tokens[index].Kind == TokenKind.Punctuation && tokens[index].Char(text) == c;

        private static bool IsOpenDelimiter(string text, List<Token> tokens, int index) =>
            IsPunct(text, tokens, index, '(') || IsPunct(text, tokens, index, '[') || IsPunct(text, tokens, index, '{');

        private static bool IsKeywordFn(string text, List<Token> tokens, int index) =>
            index >= 0 && tokens[index].Kind == TokenKind.Identifier && Text(text, tokens[index]) == "fn";

        /// <summary>True when the identifier at <paramref name="k"/> is preceded by <c>sqlx::</c>.</summary>
        private static bool HasSqlxPrefix(string text, List<Token> tokens, int k) =>
            IsPunct(text, tokens, k - 1, ':') && IsPunct(text, tokens, k - 2, ':') && k - 3 >= 0
            && tokens[k - 3].Kind == TokenKind.Identifier && Text(text, tokens[k - 3]) == "sqlx";

        /// <summary>True when the identifier at <paramref name="k"/> is preceded by <c><paramref name="type"/>::</c>.</summary>
        private static bool IsPath(string text, List<Token> tokens, int k, string type) =>
            IsPunct(text, tokens, k - 1, ':') && IsPunct(text, tokens, k - 2, ':') && k - 3 >= 0
            && tokens[k - 3].Kind == TokenKind.Identifier && Text(text, tokens[k - 3]) == type;

        /// <summary>Skips <c>::&lt;…&gt;</c> after a function name; returns the index of the next token.</summary>
        private static int SkipTurbofish(string text, List<Token> tokens, int index)
        {
            if (!(IsPunct(text, tokens, index, ':') && IsPunct(text, tokens, index + 1, ':') && IsPunct(text, tokens, index + 2, '<')))
            {
                return index;
            }

            int depth = 0;
            for (int i = index + 2; i < tokens.Count; i++)
            {
                if (tokens[i].Kind != TokenKind.Punctuation)
                {
                    continue;
                }

                char c = tokens[i].Char(text);
                if (c == '<')
                {
                    depth++;
                }
                else if (c == '>' && !IsPunct(text, tokens, i - 1, '-'))
                {
                    depth--;
                    if (depth == 0)
                    {
                        return i + 1;
                    }
                }
                else if (c == ';' || c == '{' || c == '}')
                {
                    return -1;
                }
            }

            return -1;
        }

        /// <summary>The token after the first top-level <c>,</c> starting at <paramref name="index"/> (a macro/call's next argument), or -1.</summary>
        private static int NextArgument(string text, List<Token> tokens, int index)
        {
            int depth = 0;
            for (int i = index; i < tokens.Count; i++)
            {
                if (tokens[i].Kind != TokenKind.Punctuation)
                {
                    continue;
                }

                char c = tokens[i].Char(text);
                switch (c)
                {
                    case '(':
                    case '[':
                    case '{':
                    case '<':
                        depth++;
                        break;
                    case ')':
                    case ']':
                    case '}':
                        if (depth == 0)
                        {
                            return -1;
                        }

                        depth--;
                        break;
                    case '>':
                        if (!IsPunct(text, tokens, i - 1, '-') && !IsPunct(text, tokens, i - 1, '='))
                        {
                            depth = Math.Max(0, depth - 1);
                        }

                        break;
                    case ',':
                        if (depth == 0)
                        {
                            return i + 1;
                        }

                        break;
                    case ';':
                        return -1;
                }
            }

            return -1;
        }

        private static List<Token> Lex(string text)
        {
            var tokens = new List<Token>(Math.Max(16, text.Length / 6));
            int n = text.Length;
            int i = 0;
            while (i < n)
            {
                char c = text[i];
                if (char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }

                if (c == '/' && i + 1 < n)
                {
                    if (text[i + 1] == '/')
                    {
                        while (i < n && text[i] != '\n')
                        {
                            i++;
                        }

                        continue;
                    }

                    if (text[i + 1] == '*')
                    {
                        int depth = 1;
                        i += 2;
                        while (i < n && depth > 0)
                        {
                            if (text[i] == '/' && i + 1 < n && text[i + 1] == '*')
                            {
                                depth++;
                                i += 2;
                            }
                            else if (text[i] == '*' && i + 1 < n && text[i + 1] == '/')
                            {
                                depth--;
                                i += 2;
                            }
                            else
                            {
                                i++;
                            }
                        }

                        continue;
                    }
                }

                if (c == '"')
                {
                    tokens.Add(PlainString(text, i, i, TokenKind.String));
                    i = End(tokens);
                    continue;
                }

                if (c == 'r' || c == 'b' || c == 'c')
                {
                    // r"…", r#"…"#, b"…", br"…", c"…", cr#"…"#, b'x', r#ident.
                    int j = i;
                    bool prefixedByte = false;
                    if ((c == 'b' || c == 'c') && j + 1 < n)
                    {
                        if (text[j + 1] == '"')
                        {
                            tokens.Add(PlainString(text, i, j + 1, TokenKind.OtherLiteral));
                            i = End(tokens);
                            continue;
                        }

                        if (c == 'b' && text[j + 1] == '\'')
                        {
                            i = CharLiteral(text, j + 1, tokens, i);
                            continue;
                        }

                        if (text[j + 1] == 'r')
                        {
                            j++;
                            prefixedByte = true;
                        }
                    }

                    if (text[j] == 'r' && j + 1 < n && (text[j + 1] == '"' || text[j + 1] == '#'))
                    {
                        int k = j + 1;
                        int hashes = 0;
                        while (k < n && text[k] == '#')
                        {
                            hashes++;
                            k++;
                        }

                        if (k < n && text[k] == '"')
                        {
                            tokens.Add(RawString(text, i, k, hashes, prefixedByte ? TokenKind.OtherLiteral : TokenKind.String));
                            i = End(tokens);
                            continue;
                        }

                        if (hashes == 1 && !prefixedByte && k < n && IsIdentifierStart(text[k]))
                        {
                            // r#type: a raw identifier.
                            int end = k;
                            while (end < n && IsIdentifierPart(text[end]))
                            {
                                end++;
                            }

                            tokens.Add(new Token { Kind = TokenKind.Identifier, Start = k, Length = end - k });
                            i = end;
                            continue;
                        }
                    }
                }

                if (c == '\'')
                {
                    i = CharLiteral(text, i, tokens, i);
                    continue;
                }

                if (IsIdentifierStart(c))
                {
                    int end = i + 1;
                    while (end < n && IsIdentifierPart(text[end]))
                    {
                        end++;
                    }

                    tokens.Add(new Token { Kind = TokenKind.Identifier, Start = i, Length = end - i });
                    i = end;
                    continue;
                }

                if (char.IsDigit(c))
                {
                    int end = i + 1;
                    while (end < n && (char.IsLetterOrDigit(text[end]) || text[end] == '_'
                        || (text[end] == '.' && end + 1 < n && char.IsDigit(text[end + 1]) && text[end - 1] != '.')))
                    {
                        end++;
                    }

                    tokens.Add(new Token { Kind = TokenKind.OtherLiteral, Start = i, Length = end - i });
                    i = end;
                    continue;
                }

                tokens.Add(new Token { Kind = TokenKind.Punctuation, Start = i, Length = 1 });
                i++;
            }

            return tokens;
        }

        private static int End(List<Token> tokens)
        {
            var last = tokens[tokens.Count - 1];
            return last.Start + last.Length;
        }

        /// <summary>A <c>"…"</c> string whose opening quote is at <paramref name="quote"/>.</summary>
        private static Token PlainString(string text, int start, int quote, TokenKind kind)
        {
            int i = quote + 1;
            int n = text.Length;
            while (i < n && text[i] != '"')
            {
                i += text[i] == '\\' ? 2 : 1;
            }

            int contentEnd = Math.Min(i, n);
            int end = Math.Min(n, i + 1);
            return new Token { Kind = kind, Start = start, Length = end - start, ContentStart = quote + 1, ContentLength = contentEnd - (quote + 1) };
        }

        private static Token RawString(string text, int start, int quote, int hashes, TokenKind kind)
        {
            int n = text.Length;
            int i = quote + 1;
            while (i < n)
            {
                if (text[i] == '"')
                {
                    int k = 0;
                    while (k < hashes && i + 1 + k < n && text[i + 1 + k] == '#')
                    {
                        k++;
                    }

                    if (k == hashes)
                    {
                        return new Token { Kind = kind, Start = start, Length = i + 1 + hashes - start, ContentStart = quote + 1, ContentLength = i - (quote + 1), Hashes = hashes, Raw = true };
                    }
                }

                i++;
            }

            return new Token { Kind = kind, Start = start, Length = n - start, ContentStart = quote + 1, ContentLength = n - (quote + 1), Hashes = hashes, Raw = true };
        }

        /// <summary>A char literal (<c>'a'</c>, <c>'\n'</c>, <c>'\u{1F600}'</c>) or a lifetime/label (<c>'a</c>, <c>'static</c>).</summary>
        private static int CharLiteral(string text, int quote, List<Token> tokens, int start)
        {
            int n = text.Length;
            int i = quote + 1;
            if (i < n && text[i] == '\\')
            {
                i += 2;
                while (i < n && text[i] != '\'' && text[i] != '\n')
                {
                    i++;
                }

                int end = Math.Min(n, i + 1);
                tokens.Add(new Token { Kind = TokenKind.OtherLiteral, Start = start, Length = end - start });
                return end;
            }

            int width = i < n && char.IsHighSurrogate(text[i]) ? 2 : 1;
            if (i + width < n && text[i + width] == '\'')
            {
                tokens.Add(new Token { Kind = TokenKind.OtherLiteral, Start = start, Length = i + width + 1 - start });
                return i + width + 1;
            }

            // A lifetime or a loop label.
            int j = i;
            while (j < n && IsIdentifierPart(text[j]))
            {
                j++;
            }

            tokens.Add(new Token { Kind = TokenKind.OtherLiteral, Start = start, Length = Math.Max(1, j - start) });
            return Math.Max(start + 1, j);
        }

        private static bool IsIdentifierStart(char c) => c == '_' || char.IsLetter(c);

        private static bool IsIdentifierPart(char c) => c == '_' || char.IsLetterOrDigit(c);
    }
}
