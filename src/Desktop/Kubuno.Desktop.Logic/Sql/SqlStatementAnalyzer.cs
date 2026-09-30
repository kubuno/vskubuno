using System;
using System.Collections.Generic;
using System.Linq;

namespace Kubuno.Desktop.Logic.Sql
{
    public enum SqlTableRefKind
    {
        /// <summary>A table or view name (<c>FROM customers c</c>).</summary>
        Table,

        /// <summary>A name the statement's <c>WITH</c> defines.</summary>
        Cte,

        /// <summary>A subquery with an alias (<c>FROM (SELECT …) s</c>): its columns are not known.</summary>
        Derived,

        /// <summary>A set-returning function (<c>FROM generate_series(1, 3) g</c>).</summary>
        TableFunction,
    }

    /// <summary>A table the statement reads or writes (<c>FROM</c>, <c>JOIN</c>, <c>UPDATE</c>, <c>INTO</c>).</summary>
    public sealed class SqlTableRef
    {
        public SqlTableRef(SqlTableRefKind kind, string? schema, string name, string? alias, int nameStart, int nameLength, bool quoted, bool multiPart)
        {
            Kind = kind;
            Schema = schema;
            Name = name;
            Alias = alias;
            NameStart = nameStart;
            NameLength = nameLength;
            Quoted = quoted;
            MultiPart = multiPart;
        }

        public SqlTableRefKind Kind { get; }

        public string? Schema { get; }

        /// <summary>The table name (empty for a subquery).</summary>
        public string Name { get; }

        public string? Alias { get; }

        /// <summary>The name token (decoded SQL offsets).</summary>
        public int NameStart { get; }

        public int NameLength { get; }

        /// <summary>True when the name or its schema is a quoted identifier.</summary>
        public bool Quoted { get; }

        /// <summary>True for a three-part name (<c>db.schema.table</c>): never resolved.</summary>
        public bool MultiPart { get; }

        /// <summary>The name the rest of the statement uses for it: the alias, else the table name.</summary>
        public string Key => Alias ?? Name;

        public override string ToString() => Kind + " " + (Schema is null ? string.Empty : Schema + ".") + Name + (Alias is null ? string.Empty : " " + Alias);
    }

    /// <summary>One statement of a SQL text (split on top-level <c>;</c>) and the tables it names.</summary>
    public sealed class SqlStatement
    {
        private readonly Dictionary<string, SqlTableRef?> _byKey = new Dictionary<string, SqlTableRef?>(StringComparer.OrdinalIgnoreCase);

        internal SqlStatement(int start, int end, List<SqlToken> tokens, List<SqlTableRef> tables, HashSet<string> cteNames)
        {
            Start = start;
            End = end;
            Tokens = tokens;
            Tables = tables;
            CteNames = cteNames;
            foreach (var table in tables)
            {
                if (table.Kind != SqlTableRefKind.Derived && table.Name.Length == 0)
                {
                    continue;
                }

                var key = table.Key;
                if (key.Length == 0)
                {
                    continue;
                }

                if (_byKey.TryGetValue(key, out var existing))
                {
                    if (existing != null && !Same(existing, table))
                    {
                        _byKey[key] = null;
                    }
                }
                else
                {
                    _byKey[key] = table;
                }
            }
        }

        /// <summary>The first character of the statement (decoded SQL offset).</summary>
        public int Start { get; }

        /// <summary>The end of the statement (the <c>;</c> or the end of the text).</summary>
        public int End { get; }

        /// <summary>The statement's tokens, comments excluded.</summary>
        public IReadOnlyList<SqlToken> Tokens { get; }

        public IReadOnlyList<SqlTableRef> Tables { get; }

        public IReadOnlyCollection<string> CteNames { get; }

        /// <summary>The table an alias or unaliased table name denotes; null when unknown or ambiguous.</summary>
        public SqlTableRef? Resolve(string qualifier) => _byKey.TryGetValue(qualifier, out var table) ? table : null;

        /// <summary>True when <paramref name="qualifier"/> is an alias/table key of this statement (even ambiguous).</summary>
        public bool HasKey(string qualifier) => _byKey.ContainsKey(qualifier);

        private static bool Same(SqlTableRef a, SqlTableRef b) =>
            a.Kind == b.Kind
            && string.Equals(a.Schema, b.Schema, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase)
            && a.Kind != SqlTableRefKind.Derived;
    }

    public enum SqlCompletionContextKind
    {
        /// <summary>No completion here (inside a SQL string, comment or parameter).</summary>
        None,

        Keywords,

        /// <summary>After <c>FROM</c>/<c>JOIN</c>/<c>INTO</c>/<c>UPDATE</c>/<c>TABLE</c>: tables, views, schemas.</summary>
        Tables,

        /// <summary>In a select list, a condition, <c>SET</c>, <c>ORDER BY</c>...: the columns of the statement's tables.</summary>
        Columns,

        /// <summary>After <c>qualifier.</c>: the columns of an alias/table or the tables of a schema.</summary>
        Qualified,
    }

    /// <summary>Where the caret is, for the SQL completion.</summary>
    public sealed class SqlCompletionContext
    {
        public SqlCompletionContext(SqlCompletionContextKind kind, int prefixStart, string prefix, SqlStatement? statement, string? qualifier, SqlTableRef? insertTable)
        {
            Kind = kind;
            PrefixStart = prefixStart;
            Prefix = prefix;
            Statement = statement;
            Qualifier = qualifier;
            InsertTable = insertTable;
        }

        public SqlCompletionContextKind Kind { get; }

        /// <summary>The start of the word being typed (decoded SQL offset).</summary>
        public int PrefixStart { get; }

        public string Prefix { get; }

        public SqlStatement? Statement { get; }

        /// <summary>The name before the <c>.</c> (quotes removed) for <see cref="SqlCompletionContextKind.Qualified"/>.</summary>
        public string? Qualifier { get; }

        /// <summary>The table of <c>INSERT INTO t (|</c>: its columns only.</summary>
        public SqlTableRef? InsertTable { get; }
    }

    /// <summary>What the SQL of a literal names: statements, their tables and aliases, the caret's completion context.</summary>
    public static class SqlStatementAnalyzer
    {
        private static readonly HashSet<string> ClauseKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SELECT", "FROM", "JOIN", "WHERE", "ON", "SET", "BY", "HAVING", "RETURNING", "VALUES", "INTO", "UPDATE", "WHEN",
            "THEN", "ELSE", "AND", "OR", "USING", "LIMIT", "OFFSET", "CASE", "NOT", "DISTINCT", "TABLE", "WITH", "AS",
        };

        private static readonly HashSet<string> ColumnClauses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SELECT", "WHERE", "ON", "SET", "BY", "HAVING", "RETURNING", "WHEN", "THEN", "ELSE", "AND", "OR", "NOT", "DISTINCT",
            "CASE", "USING",
        };

        /// <summary>The statements of <paramref name="sql"/> (its tokens from <see cref="SqlTokenizer"/>).</summary>
        public static IReadOnlyList<SqlStatement> Analyze(string sql, IReadOnlyList<SqlToken> tokens)
        {
            var statements = new List<SqlStatement>();
            var current = new List<SqlToken>();
            int start = 0;
            int depth = 0;
            foreach (var token in tokens)
            {
                if (token.Kind == SqlTokenKind.Comment)
                {
                    continue;
                }

                if (token.IsPunctuation(sql, '('))
                {
                    depth++;
                }
                else if (token.IsPunctuation(sql, ')'))
                {
                    depth = Math.Max(0, depth - 1);
                }
                else if (token.IsPunctuation(sql, ';') && depth == 0)
                {
                    statements.Add(Build(sql, start, token.Start, current));
                    current = new List<SqlToken>();
                    start = token.End;
                    continue;
                }

                current.Add(token);
            }

            statements.Add(Build(sql, start, sql.Length, current));
            return statements;
        }

        /// <summary>The statement containing <paramref name="offset"/>.</summary>
        public static SqlStatement StatementAt(IReadOnlyList<SqlStatement> statements, int offset) =>
            statements.FirstOrDefault(s => offset >= s.Start && offset <= s.End) ?? statements[statements.Count - 1];

        /// <summary>
        /// The names a text creates or alters (<c>CREATE TABLE x</c>, <c>CREATE VIEW x</c>, <c>ALTER TABLE x</c>, the target
        /// of <c>RENAME TO x</c>): the snapshot may not know them yet, so they are never flagged.
        /// </summary>
        public static HashSet<string> DefinedNames(string sql, IReadOnlyList<SqlToken> tokens)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = tokens.Where(t => t.Kind != SqlTokenKind.Comment).ToList();
            for (int i = 0; i < list.Count; i++)
            {
                bool create = list[i].IsKeyword(sql, "CREATE") || list[i].IsKeyword(sql, "ALTER");
                bool rename = list[i].IsKeyword(sql, "RENAME") && i + 1 < list.Count && list[i + 1].IsKeyword(sql, "TO");
                if (!create && !rename)
                {
                    continue;
                }

                int j = i + 1;
                if (rename)
                {
                    j++;
                }
                else
                {
                    while (j < list.Count && list[j].Kind == SqlTokenKind.Keyword && !list[j].IsKeyword(sql, "TABLE") && !list[j].IsKeyword(sql, "VIEW"))
                    {
                        j++;
                    }

                    if (j >= list.Count)
                    {
                        continue;
                    }

                    j++;
                    while (j < list.Count && (list[j].IsKeyword(sql, "IF") || list[j].IsKeyword(sql, "NOT") || list[j].IsKeyword(sql, "EXISTS") || list[j].IsKeyword(sql, "ONLY")))
                    {
                        j++;
                    }
                }

                // The last part of a (possibly qualified) name.
                while (j < list.Count && IsName(list[j]))
                {
                    names.Add(list[j].GetName(sql));
                    if (j + 2 < list.Count && list[j + 1].IsPunctuation(sql, '.') && IsName(list[j + 2]))
                    {
                        j += 2;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            return names;
        }

        /// <summary>The completion context at <paramref name="caret"/> (a decoded SQL offset).</summary>
        public static SqlCompletionContext GetCompletionContext(string sql, IReadOnlyList<SqlToken> tokens, int caret)
        {
            caret = Math.Max(0, Math.Min(caret, sql.Length));
            int prefixStart = caret;
            while (prefixStart > 0 && SqlTokenizer.IsIdentifierPart(sql[prefixStart - 1]))
            {
                prefixStart--;
            }

            var prefix = sql.Substring(prefixStart, caret - prefixStart);
            foreach (var token in tokens)
            {
                if (token.Start >= caret)
                {
                    break;
                }

                if (IsInside(sql, token, caret) && (token.Kind == SqlTokenKind.Comment || token.Kind == SqlTokenKind.String || token.Kind == SqlTokenKind.QuotedIdentifier || token.Kind == SqlTokenKind.Number))
                {
                    return new SqlCompletionContext(SqlCompletionContextKind.None, prefixStart, prefix, null, null, null);
                }
            }

            if (prefixStart > 0)
            {
                char before = sql[prefixStart - 1];
                bool cast = before == ':' && prefixStart > 1 && sql[prefixStart - 2] == ':';
                if ((before == '@' || before == '$' || before == '?' || before == ':') && !cast)
                {
                    return new SqlCompletionContext(SqlCompletionContextKind.None, prefixStart, prefix, null, null, null);
                }

                if (cast)
                {
                    return new SqlCompletionContext(SqlCompletionContextKind.Keywords, prefixStart, prefix, null, null, null);
                }
            }

            var statements = Analyze(sql, tokens);
            var statement = StatementAt(statements, caret);
            var before_ = statement.Tokens.Where(t => t.End <= prefixStart && t.Start < prefixStart).ToList();

            if (prefixStart > 0 && sql[prefixStart - 1] == '.')
            {
                // `alias.|` or `schema.|`: the qualifier is the name right before the dot.
                if (before_.Count >= 2 && before_[before_.Count - 1].IsPunctuation(sql, '.') && IsName(before_[before_.Count - 2])
                    && before_[before_.Count - 2].End == before_[before_.Count - 1].Start)
                {
                    var qualifier = before_[before_.Count - 2].GetName(sql);
                    return new SqlCompletionContext(SqlCompletionContextKind.Qualified, prefixStart, prefix, statement, qualifier, null);
                }

                return new SqlCompletionContext(SqlCompletionContextKind.None, prefixStart, prefix, statement, null, null);
            }

            if (before_.Count == 0)
            {
                return new SqlCompletionContext(SqlCompletionContextKind.Keywords, prefixStart, prefix, statement, null, null);
            }

            var previous = before_[before_.Count - 1];
            if (previous.Kind == SqlTokenKind.Keyword && SqlLanguage.TableTriggerKeywords.Contains(previous.GetText(sql))
                && !(previous.IsKeyword(sql, "UPDATE") && before_.Count >= 2 && (before_[before_.Count - 2].IsKeyword(sql, "DO") || before_[before_.Count - 2].IsKeyword(sql, "KEY"))))
            {
                return new SqlCompletionContext(SqlCompletionContextKind.Tables, prefixStart, prefix, statement, null, null);
            }

            // `INSERT INTO t (a, |`: the columns of t.
            int open = EnclosingOpenParen(sql, before_);
            if (open >= 1 && IsName(before_[open - 1]))
            {
                int nameIndex = open - 1;
                int into = nameIndex - 1;
                if (into >= 2 && before_[into].IsPunctuation(sql, '.') && IsName(before_[into - 1]))
                {
                    into -= 2;
                }

                if (into >= 0 && before_[into].IsKeyword(sql, "INTO"))
                {
                    var target = statement.Tables.FirstOrDefault(t => t.NameStart == before_[nameIndex].Start);
                    if (target != null)
                    {
                        return new SqlCompletionContext(SqlCompletionContextKind.Columns, prefixStart, prefix, statement, null, target);
                    }
                }
            }

            var clause = NearestClause(sql, before_);
            if (clause != null && (string.Equals(clause, "FROM", StringComparison.OrdinalIgnoreCase) || string.Equals(clause, "JOIN", StringComparison.OrdinalIgnoreCase))
                && previous.IsPunctuation(sql, ','))
            {
                return new SqlCompletionContext(SqlCompletionContextKind.Tables, prefixStart, prefix, statement, null, null);
            }

            if (clause != null && ColumnClauses.Contains(clause))
            {
                return new SqlCompletionContext(SqlCompletionContextKind.Columns, prefixStart, prefix, statement, null, null);
            }

            return new SqlCompletionContext(SqlCompletionContextKind.Keywords, prefixStart, prefix, statement, null, null);
        }

        internal static bool IsName(SqlToken token) =>
            token.Kind == SqlTokenKind.Identifier || token.Kind == SqlTokenKind.QuotedIdentifier || token.Kind == SqlTokenKind.Function;

        /// <summary>True when <paramref name="caret"/> is inside <paramref name="token"/> (after its first character, before its closing one).</summary>
        private static bool IsInside(string sql, SqlToken token, int caret)
        {
            if (caret <= token.Start || caret > token.End)
            {
                return false;
            }

            if (caret < token.End)
            {
                return true;
            }

            // At the end: inside only when the token is not closed (a line comment, an unterminated string or comment).
            switch (token.Kind)
            {
                case SqlTokenKind.Comment:
                    return sql[token.Start] == '-' || !(token.Length >= 4 && sql[token.End - 1] == '/' && sql[token.End - 2] == '*');
                case SqlTokenKind.String:
                    return token.Length < 2 || (sql[token.Start] == '\'' && sql[token.End - 1] != '\'');
                case SqlTokenKind.QuotedIdentifier:
                    return token.Length < 2 || sql[token.End - 1] != (sql[token.Start] == '[' ? ']' : sql[token.Start]);
                case SqlTokenKind.Number:
                    return false;
                default:
                    return false;
            }
        }

        /// <summary>The index in <paramref name="tokens"/> of the <c>(</c> enclosing the end of the list, or -1.</summary>
        private static int EnclosingOpenParen(string sql, List<SqlToken> tokens)
        {
            int depth = 0;
            for (int i = tokens.Count - 1; i >= 0; i--)
            {
                if (tokens[i].IsPunctuation(sql, ')'))
                {
                    depth++;
                }
                else if (tokens[i].IsPunctuation(sql, '('))
                {
                    if (depth == 0)
                    {
                        return i;
                    }

                    depth--;
                }
            }

            return -1;
        }

        /// <summary>The nearest clause keyword before the caret, skipping parenthesised groups that are closed.</summary>
        private static string? NearestClause(string sql, List<SqlToken> tokens)
        {
            int depth = 0;
            for (int i = tokens.Count - 1; i >= 0; i--)
            {
                var token = tokens[i];
                if (token.IsPunctuation(sql, ')'))
                {
                    depth++;
                }
                else if (token.IsPunctuation(sql, '('))
                {
                    depth = Math.Max(0, depth - 1);
                }
                else if (depth == 0 && token.Kind == SqlTokenKind.Keyword && ClauseKeywords.Contains(token.GetText(sql)))
                {
                    return token.GetText(sql);
                }
            }

            return null;
        }

        private static SqlStatement Build(string sql, int start, int end, List<SqlToken> tokens)
        {
            var ctes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var tables = new List<SqlTableRef>();
            for (int i = 0; i < tokens.Count; i++)
            {
                if (tokens[i].IsKeyword(sql, "WITH"))
                {
                    ReadCtes(sql, tokens, i + 1, ctes);
                }
            }

            for (int i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];
                if (token.Kind != SqlTokenKind.Keyword)
                {
                    continue;
                }

                bool from = token.IsKeyword(sql, "FROM");
                bool into = token.IsKeyword(sql, "INTO");
                bool join = token.IsKeyword(sql, "JOIN");
                bool update = token.IsKeyword(sql, "UPDATE");
                if (!(from || into || join || update))
                {
                    continue;
                }

                if (from && IsNotTableFrom(sql, tokens, i))
                {
                    continue;
                }

                if (update && i > 0 && (tokens[i - 1].IsKeyword(sql, "DO") || tokens[i - 1].IsKeyword(sql, "KEY") || tokens[i - 1].IsKeyword(sql, "FOR")))
                {
                    continue;
                }

                int next = ReadTableRef(sql, tokens, i + 1, into, ctes, tables);
                while (from && next < tokens.Count && next > i + 1 && tokens[next].IsPunctuation(sql, ','))
                {
                    int after = ReadTableRef(sql, tokens, next + 1, false, ctes, tables);
                    if (after == next + 1)
                    {
                        break;
                    }

                    next = after;
                }
            }

            return new SqlStatement(start, end, tokens, tables, ctes);
        }

        /// <summary><c>EXTRACT(YEAR FROM x)</c>, <c>SUBSTRING(s FROM 2)</c>, <c>IS DISTINCT FROM</c>: a FROM that names no table.</summary>
        private static bool IsNotTableFrom(string sql, List<SqlToken> tokens, int index)
        {
            if (index >= 1 && tokens[index - 1].IsKeyword(sql, "DISTINCT") && index >= 2 && (tokens[index - 2].IsKeyword(sql, "IS") || tokens[index - 2].IsKeyword(sql, "NOT")))
            {
                return true;
            }

            if (index >= 1 && tokens[index - 1].IsKeyword(sql, "DELETE"))
            {
                return false;
            }

            int open = EnclosingOpenParen(sql, tokens.GetRange(0, index));
            return open >= 1 && tokens[open - 1].Kind == SqlTokenKind.Function;
        }

        private static void ReadCtes(string sql, List<SqlToken> tokens, int j, HashSet<string> ctes)
        {
            if (j < tokens.Count && tokens[j].IsKeyword(sql, "RECURSIVE"))
            {
                j++;
            }

            while (j < tokens.Count && IsName(tokens[j]))
            {
                ctes.Add(tokens[j].GetName(sql));
                j++;
                if (j < tokens.Count && tokens[j].IsPunctuation(sql, '('))
                {
                    j = Match(sql, tokens, j) + 1;
                }

                if (!(j < tokens.Count && tokens[j].IsKeyword(sql, "AS")))
                {
                    return;
                }

                j++;
                while (j < tokens.Count && (tokens[j].IsKeyword(sql, "NOT") || tokens[j].IsKeyword(sql, "MATERIALIZED")))
                {
                    j++;
                }

                if (!(j < tokens.Count && tokens[j].IsPunctuation(sql, '(')))
                {
                    return;
                }

                j = Match(sql, tokens, j) + 1;
                if (!(j < tokens.Count && tokens[j].IsPunctuation(sql, ',')))
                {
                    return;
                }

                j++;
            }
        }

        /// <summary>Reads one table reference at <paramref name="j"/>; returns the index after it (== j when there is none).</summary>
        private static int ReadTableRef(string sql, List<SqlToken> tokens, int j, bool columnListFollows, HashSet<string> ctes, List<SqlTableRef> tables)
        {
            int start = j;
            while (j < tokens.Count && (tokens[j].IsKeyword(sql, "ONLY") || tokens[j].IsKeyword(sql, "LATERAL")))
            {
                j++;
            }

            if (j >= tokens.Count)
            {
                return start;
            }

            if (tokens[j].IsPunctuation(sql, '('))
            {
                int close = Match(sql, tokens, j);
                int after = close + 1;
                var alias = ReadAlias(sql, tokens, ref after);
                tables.Add(new SqlTableRef(SqlTableRefKind.Derived, null, string.Empty, alias, tokens[j].Start, 0, false, false));
                return after;
            }

            if (!IsName(tokens[j]))
            {
                return start;
            }

            var parts = new List<SqlToken> { tokens[j] };
            while (j + 2 < tokens.Count && tokens[j + 1].IsPunctuation(sql, '.') && IsName(tokens[j + 2])
                && tokens[j].End == tokens[j + 1].Start && tokens[j + 1].End == tokens[j + 2].Start)
            {
                j += 2;
                parts.Add(tokens[j]);
            }

            if (j + 1 < tokens.Count && tokens[j + 1].IsPunctuation(sql, '.') && tokens[j].End == tokens[j + 1].Start)
            {
                // `FROM billing.|` being typed: not a table yet.
                return j + 2;
            }

            int next = j + 1;
            var kind = SqlTableRefKind.Table;
            if (next < tokens.Count && tokens[next].IsPunctuation(sql, '('))
            {
                // `INSERT INTO t (a, b)` is a column list; elsewhere `name(…)` is a set-returning function.
                if (!columnListFollows)
                {
                    kind = SqlTableRefKind.TableFunction;
                }

                next = Match(sql, tokens, next) + 1;
            }

            var nameToken = parts[parts.Count - 1];
            var name = nameToken.GetName(sql);
            string? schema = parts.Count >= 2 ? parts[parts.Count - 2].GetName(sql) : null;
            if (kind == SqlTableRefKind.Table && parts.Count == 1 && ctes.Contains(name))
            {
                kind = SqlTableRefKind.Cte;
            }

            string? aliasName = columnListFollows ? ReadAliasAfterAs(sql, tokens, ref next) : ReadAlias(sql, tokens, ref next);
            bool quoted = parts.Any(p => p.Kind == SqlTokenKind.QuotedIdentifier);
            tables.Add(new SqlTableRef(kind, schema, name, aliasName, nameToken.Start, nameToken.Length, quoted, parts.Count > 2));
            return next;
        }

        /// <summary><c>[AS] alias</c> (an unquoted alias must not be a keyword).</summary>
        private static string? ReadAlias(string sql, List<SqlToken> tokens, ref int j)
        {
            bool hasAs = j < tokens.Count && tokens[j].IsKeyword(sql, "AS");
            int k = hasAs ? j + 1 : j;
            if (k < tokens.Count && (tokens[k].Kind == SqlTokenKind.Identifier || tokens[k].Kind == SqlTokenKind.QuotedIdentifier))
            {
                j = k + 1;
                if (j < tokens.Count && tokens[j].IsPunctuation(sql, '('))
                {
                    // `AS s(a, b)`: column aliases.
                    j = Match(sql, tokens, j) + 1;
                }

                return tokens[k].GetName(sql);
            }

            return null;
        }

        /// <summary>After <c>INSERT INTO t</c> only <c>AS alias</c> (PostgreSQL) names an alias.</summary>
        private static string? ReadAliasAfterAs(string sql, List<SqlToken> tokens, ref int j)
        {
            if (j + 1 < tokens.Count && tokens[j].IsKeyword(sql, "AS") && (tokens[j + 1].Kind == SqlTokenKind.Identifier || tokens[j + 1].Kind == SqlTokenKind.QuotedIdentifier))
            {
                var alias = tokens[j + 1].GetName(sql);
                j += 2;
                return alias;
            }

            return null;
        }

        /// <summary>The index of the <c>)</c> matching the <c>(</c> at <paramref name="open"/> (the last token when unclosed).</summary>
        private static int Match(string sql, List<SqlToken> tokens, int open)
        {
            int depth = 0;
            for (int i = open; i < tokens.Count; i++)
            {
                if (tokens[i].IsPunctuation(sql, '('))
                {
                    depth++;
                }
                else if (tokens[i].IsPunctuation(sql, ')'))
                {
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }
                }
            }

            return tokens.Count - 1;
        }
    }
}
