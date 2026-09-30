using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Kubuno.Desktop.Logic.Sql
{
    public enum SqlCompletionKind
    {
        Column,
        Alias,
        Table,
        View,
        Cte,
        Schema,
        Function,
        Keyword,
    }

    /// <summary>One item of the SQL completion list.</summary>
    public sealed class SqlCompletionEntry
    {
        public SqlCompletionEntry(SqlCompletionKind kind, string displayText, string insertText, string description, string suffix)
        {
            Kind = kind;
            DisplayText = displayText;
            InsertText = insertText;
            Description = description;
            Suffix = suffix;
        }

        public SqlCompletionKind Kind { get; }

        public string DisplayText { get; }

        /// <summary>The text committed (quoted when the name needs it, escaped for a plain Rust string).</summary>
        public string InsertText { get; }

        /// <summary>The tooltip text (French or English).</summary>
        public string Description { get; }

        /// <summary>The grey text at the right of the item (a column's type, a table's schema).</summary>
        public string Suffix { get; }

        /// <summary>Columns and aliases first, then tables, schemas, functions, keywords.</summary>
        public string SortText => ((int)Kind).ToString(System.Globalization.CultureInfo.InvariantCulture) + DisplayText.ToLowerInvariant();

        /// <summary>The KnownMonikers name of the item's icon.</summary>
        public string IconMonikerName => SqlCompletion.IconMonikerName(Kind);

        public override string ToString() => Kind + " " + DisplayText;
    }

    /// <summary>The SQL completion list for a caret position: keywords, and from the schema snapshot tables, columns, functions.</summary>
    public static class SqlCompletion
    {
        private static readonly Regex PlainName = new Regex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);

        public static string IconMonikerName(SqlCompletionKind kind) => kind switch
        {
            SqlCompletionKind.Column => "Column",
            SqlCompletionKind.Alias => "LocalVariable",
            SqlCompletionKind.Table => "Table",
            SqlCompletionKind.View => "View",
            SqlCompletionKind.Cte => "TableGroup",
            SqlCompletionKind.Schema => "DatabaseSchema",
            SqlCompletionKind.Function => "ScalarFunction",
            _ => "IntellisenseKeyword",
        };

        /// <summary>The icons the list uses (checked against the installed image catalog by a test).</summary>
        public static IEnumerable<string> AllIconMonikerNames => Enum.GetValues(typeof(SqlCompletionKind)).Cast<SqlCompletionKind>().Select(IconMonikerName);

        /// <param name="sql">The decoded SQL of the literal.</param>
        /// <param name="tokens">Its tokens.</param>
        /// <param name="caret">The caret, as a decoded SQL offset.</param>
        /// <param name="index">The schema (<see cref="SqlSchemaIndex.Empty"/> when none: keywords and built-in functions only).</param>
        /// <param name="escapeForPlainRustString">True for a <c>"…"</c> literal: a quoted name's <c>"</c> is inserted as <c>\"</c>.</param>
        public static IReadOnlyList<SqlCompletionEntry> GetEntries(string sql, IReadOnlyList<SqlToken> tokens, int caret, SqlSchemaIndex index, bool french, bool escapeForPlainRustString, out SqlCompletionContext context)
        {
            context = SqlStatementAnalyzer.GetCompletionContext(sql, tokens, caret);
            var entries = new List<SqlCompletionEntry>();
            var provider = index.Snapshots.Select(s => s.Provider).FirstOrDefault(p => p != null);
            switch (context.Kind)
            {
                case SqlCompletionContextKind.None:
                    return entries;
                case SqlCompletionContextKind.Qualified:
                    AddQualified(entries, context, index, french, provider, escapeForPlainRustString);
                    return entries;
                case SqlCompletionContextKind.Tables:
                    AddTables(entries, context, index, french, provider, escapeForPlainRustString);
                    return entries;
                case SqlCompletionContextKind.Columns:
                    AddColumns(entries, context, index, french, provider, escapeForPlainRustString);
                    if (context.InsertTable != null)
                    {
                        return entries;
                    }

                    AddFunctions(entries, index, french, KeywordCase(sql, tokens, context));
                    AddKeywords(entries, french, KeywordCase(sql, tokens, context));
                    return entries;
                default:
                    AddFunctions(entries, index, french, KeywordCase(sql, tokens, context));
                    AddKeywords(entries, french, KeywordCase(sql, tokens, context));
                    return entries;
            }
        }

        /// <summary>True when the text before <paramref name="caret"/> ends with a keyword after which a space should open the list.</summary>
        public static bool IsSpaceTrigger(string sql, IReadOnlyList<SqlToken> tokens, int caret, bool schemaLoaded)
        {
            if (caret < 2 || !char.IsWhiteSpace(sql[caret - 1]))
            {
                return false;
            }

            var previous = tokens.LastOrDefault(t => t.End <= caret - 1 && t.Kind != SqlTokenKind.Comment);
            if (previous.Length == 0 || previous.End != caret - 1 || previous.Kind != SqlTokenKind.Keyword)
            {
                return false;
            }

            var word = previous.GetText(sql);
            if (SqlLanguage.TableTriggerKeywords.Contains(word))
            {
                return schemaLoaded;
            }

            return schemaLoaded && SqlLanguage.ColumnTriggerKeywords.Contains(word);
        }

        private static void AddQualified(List<SqlCompletionEntry> entries, SqlCompletionContext context, SqlSchemaIndex index, bool french, string? provider, bool escape)
        {
            var qualifier = context.Qualifier ?? string.Empty;
            var table = context.Statement?.Resolve(qualifier);
            if (table != null)
            {
                if (table.Kind == SqlTableRefKind.Table && Find(index, table) is { } info)
                {
                    foreach (var column in info.Columns)
                    {
                        entries.Add(Column(column, new[] { info }, french, provider, escape));
                    }
                }

                return;
            }

            if (context.Statement?.HasKey(qualifier) == true)
            {
                return;
            }

            if (index.FindSchema(qualifier) is { } schema)
            {
                foreach (var t in schema.Tables)
                {
                    entries.Add(Table(t, index, french, provider, escape, qualify: false));
                }

                foreach (var f in schema.Functions)
                {
                    entries.Add(Function(f, french));
                }

                return;
            }

            // A table named without alias in another statement form (`customers.` typed before FROM): its columns.
            var candidates = index.FindTables(qualifier);
            if (candidates.Count == 1)
            {
                foreach (var column in candidates[0].Columns)
                {
                    entries.Add(Column(column, candidates, french, provider, escape));
                }
            }
        }

        private static void AddTables(List<SqlCompletionEntry> entries, SqlCompletionContext context, SqlSchemaIndex index, bool french, string? provider, bool escape)
        {
            foreach (var table in index.Tables)
            {
                entries.Add(Table(table, index, french, provider, escape, qualify: !index.IsDefaultSchema(table.Schema)));
            }

            foreach (var schema in index.Schemas.Where(s => s.Name.Length > 0 && !index.IsDefaultSchema(s.Name)))
            {
                entries.Add(new SqlCompletionEntry(SqlCompletionKind.Schema, schema.Name, Quote(schema.Name, provider, escape),
                    french ? $"schéma {schema.Name} ({schema.Tables.Count} tables et vues)" : $"schema {schema.Name} ({schema.Tables.Count} tables and views)", string.Empty));
            }

            foreach (var cte in context.Statement?.CteNames ?? (IReadOnlyCollection<string>)new string[0])
            {
                entries.Add(new SqlCompletionEntry(SqlCompletionKind.Cte, cte, cte,
                    french ? $"requête nommée {cte} (WITH)" : $"common table expression {cte} (WITH)", string.Empty));
            }
        }

        private static void AddColumns(List<SqlCompletionEntry> entries, SqlCompletionContext context, SqlSchemaIndex index, bool french, string? provider, bool escape)
        {
            var statement = context.Statement;
            if (statement is null)
            {
                return;
            }

            var tables = context.InsertTable != null ? new[] { context.InsertTable } : statement.Tables.ToArray();
            var byColumn = new Dictionary<string, (SqlColumnInfo Column, List<SqlTableInfo> Tables)>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();
            foreach (var table in tables)
            {
                if (table.Kind != SqlTableRefKind.Table || !(Find(index, table) is { } info))
                {
                    continue;
                }

                foreach (var column in info.Columns)
                {
                    if (byColumn.TryGetValue(column.Name, out var existing))
                    {
                        if (!existing.Tables.Contains(info))
                        {
                            existing.Tables.Add(info);
                        }
                    }
                    else
                    {
                        byColumn[column.Name] = (column, new List<SqlTableInfo> { info });
                        order.Add(column.Name);
                    }
                }
            }

            foreach (var name in order)
            {
                var (column, owners) = byColumn[name];
                entries.Add(Column(column, owners, french, provider, escape));
            }

            if (context.InsertTable != null)
            {
                return;
            }

            foreach (var table in statement.Tables.Where(t => t.Alias != null).GroupBy(t => t.Alias!, StringComparer.OrdinalIgnoreCase).Select(g => g.First()))
            {
                var target = table.Kind == SqlTableRefKind.Derived ? (french ? "sous-requête" : "subquery") : table.Name;
                entries.Add(new SqlCompletionEntry(SqlCompletionKind.Alias, table.Alias!, table.Alias!,
                    french ? $"alias de {target}" : $"alias of {target}", string.Empty));
            }
        }

        private static void AddFunctions(List<SqlCompletionEntry> entries, SqlSchemaIndex index, bool french, bool lower)
        {
            foreach (var function in index.Functions)
            {
                entries.Add(Function(function, french));
            }

            var known = new HashSet<string>(entries.Where(e => e.Kind == SqlCompletionKind.Function).Select(e => e.DisplayText), StringComparer.OrdinalIgnoreCase);
            foreach (var (name, signature) in SqlLanguage.CompletionFunctions)
            {
                if (known.Add(name))
                {
                    var text = lower ? name.ToLowerInvariant() : name;
                    entries.Add(new SqlCompletionEntry(SqlCompletionKind.Function, text, text, signature + "\n" + (french ? "fonction SQL" : "SQL function"), string.Empty));
                }
            }
        }

        private static void AddKeywords(List<SqlCompletionEntry> entries, bool french, bool lower)
        {
            foreach (var keyword in SqlLanguage.CompletionKeywords)
            {
                var text = lower ? keyword.ToLowerInvariant() : keyword;
                entries.Add(new SqlCompletionEntry(SqlCompletionKind.Keyword, text, text, french ? "mot clé SQL" : "SQL keyword", string.Empty));
            }
        }

        /// <summary>The case the text writes its keywords in (lower when its first keyword is), else the case of the typed prefix.</summary>
        private static bool KeywordCase(string sql, IReadOnlyList<SqlToken> tokens, SqlCompletionContext context)
        {
            foreach (var token in tokens)
            {
                if (token.Kind == SqlTokenKind.Keyword && token.Start != context.PrefixStart)
                {
                    var text = token.GetText(sql);
                    return text == text.ToLowerInvariant();
                }
            }

            return context.Prefix.Length > 0 && context.Prefix == context.Prefix.ToLowerInvariant();
        }

        private static SqlTableInfo? Find(SqlSchemaIndex index, SqlTableRef table) =>
            table.Schema is null ? index.FindTable(null, table.Name) : index.FindTable(table.Schema, table.Name);

        private static SqlCompletionEntry Column(SqlColumnInfo column, IReadOnlyList<SqlTableInfo> owners, bool french, string? provider, bool escape)
        {
            var owner = owners[0];
            var builder = new StringBuilder();
            builder.Append(french ? "(colonne) " : "(column) ").Append(owner.Name).Append('.').Append(column.Name);
            if (!string.IsNullOrEmpty(column.DbType))
            {
                builder.Append(" : ").Append(column.DbType);
            }

            var traits = new List<string>();
            if (column.PrimaryKey)
            {
                traits.Add(french ? "clé primaire" : "primary key");
            }

            if (column.AutoIncrement)
            {
                traits.Add(french ? "auto-incrément" : "auto-increment");
            }

            traits.Add(column.Nullable ? (french ? "accepte NULL" : "nullable") : "NOT NULL");
            builder.Append(" (").Append(string.Join(", ", traits)).Append(')');
            if (!string.IsNullOrEmpty(column.RustType))
            {
                builder.Append('\n').Append(french ? "Type Rust : " : "Rust type: ").Append(column.Nullable ? "Option<" + column.RustType + ">" : column.RustType);
            }

            if (owners.Count > 1)
            {
                builder.Append('\n').Append(french ? "Aussi dans : " : "Also in: ").Append(string.Join(", ", owners.Skip(1).Select(o => o.Name)));
            }

            builder.Append('\n').Append(french ? "Connexion : " : "Connection: ").Append(owner.Connection);
            return new SqlCompletionEntry(SqlCompletionKind.Column, column.Name, Quote(column.Name, provider, escape), builder.ToString(), column.DbType ?? string.Empty);
        }

        private static SqlCompletionEntry Table(SqlTableInfo table, SqlSchemaIndex index, bool french, string? provider, bool escape, bool qualify)
        {
            var noun = table.IsView ? (french ? "vue" : "view") : "table";
            var qualified = table.Schema.Length > 0 ? table.Schema + "." + table.Name : table.Name;
            var description = french
                ? $"({noun}) {qualified} - {table.Columns.Count} colonnes\n{string.Join(", ", table.Columns.Take(12).Select(c => c.Name))}{(table.Columns.Count > 12 ? ", …" : string.Empty)}\nConnexion : {table.Connection}"
                : $"({noun}) {qualified} - {table.Columns.Count} columns\n{string.Join(", ", table.Columns.Take(12).Select(c => c.Name))}{(table.Columns.Count > 12 ? ", …" : string.Empty)}\nConnection: {table.Connection}";
            var insert = qualify && table.Schema.Length > 0 ? Quote(table.Schema, provider, escape) + "." + Quote(table.Name, provider, escape) : Quote(table.Name, provider, escape);
            return new SqlCompletionEntry(table.IsView ? SqlCompletionKind.View : SqlCompletionKind.Table, table.Name, insert, description, index.IsDefaultSchema(table.Schema) ? string.Empty : table.Schema);
        }

        private static SqlCompletionEntry Function(SqlFunctionInfo function, bool french)
        {
            var noun = string.Equals(function.Kind, "procedure", StringComparison.OrdinalIgnoreCase) ? (french ? "procédure" : "procedure") : (french ? "fonction" : "function");
            var signature = $"{function.Schema}{(function.Schema.Length > 0 ? "." : string.Empty)}{function.Name}({function.Arguments}){(string.IsNullOrEmpty(function.ReturnType) ? string.Empty : " → " + function.ReturnType)}";
            return new SqlCompletionEntry(SqlCompletionKind.Function, function.Name, function.Name, $"({noun}) {signature}\n{(french ? "Connexion : " : "Connection: ")}{function.Connection}", function.ReturnType ?? string.Empty);
        }

        /// <summary>
        /// The name as SQL text: unchanged when plain, else quoted (<c>"…"</c>, <c>`…`</c> for MySQL, <c>[…]</c> for SQL Server);
        /// PostgreSQL also quotes names with capitals (they fold to lower case otherwise).
        /// </summary>
        public static string Quote(string name, string? provider, bool escapeForPlainRustString)
        {
            bool plain = PlainName.IsMatch(name) && !SqlLanguage.IsKeyword(name)
                && !(string.Equals(provider, "postgres", StringComparison.OrdinalIgnoreCase) && name != name.ToLowerInvariant());
            if (plain)
            {
                return name;
            }

            if (string.Equals(provider, "mysql", StringComparison.OrdinalIgnoreCase))
            {
                return "`" + name.Replace("`", "``") + "`";
            }

            if (string.Equals(provider, "sqlserver", StringComparison.OrdinalIgnoreCase))
            {
                return "[" + name.Replace("]", "]]") + "]";
            }

            var quote = escapeForPlainRustString ? "\\\"" : "\"";
            return quote + name.Replace("\"", escapeForPlainRustString ? "\\\"\\\"" : "\"\"") + quote;
        }
    }
}
