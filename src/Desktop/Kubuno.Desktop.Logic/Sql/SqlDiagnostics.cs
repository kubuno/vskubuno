using System;
using System.Collections.Generic;
using System.Linq;

namespace Kubuno.Desktop.Logic.Sql
{
    /// <summary>A warning on a SQL name (decoded SQL offsets).</summary>
    public sealed class SqlDiagnostic
    {
        public SqlDiagnostic(int start, int length, string message)
        {
            Start = start;
            Length = length;
            Message = message;
        }

        public int Start { get; }

        public int Length { get; }

        public string Message { get; }

        public override string ToString() => Start + "+" + Length + ": " + Message;
    }

    /// <summary>
    /// The names a SQL text uses that the schema snapshot does not know, flagged before <c>cargo sqlx prepare</c> fails:
    /// a table after <c>FROM</c>/<c>JOIN</c>/<c>UPDATE</c>/<c>INTO</c>, a column after <c>alias.</c> / <c>table.</c>.
    /// Deliberately conservative - nothing is flagged when in doubt: no snapshot, quoted identifiers, CTE names, subquery
    /// and function aliases, ambiguous aliases, schemas the snapshot does not have, system tables, three-part names,
    /// tables the same text creates or alters, unqualified columns.
    /// </summary>
    public static class SqlDiagnostics
    {
        public static IReadOnlyList<SqlDiagnostic> Analyze(string sql, IReadOnlyList<SqlToken> tokens, SqlSchemaIndex index, bool french)
        {
            var result = new List<SqlDiagnostic>();
            if (index is null || index.IsEmpty)
            {
                return result;
            }

            var defined = SqlStatementAnalyzer.DefinedNames(sql, tokens);
            foreach (var statement in SqlStatementAnalyzer.Analyze(sql, tokens))
            {
                foreach (var table in statement.Tables)
                {
                    if (UnknownTable(table, statement, defined, index) is { } message)
                    {
                        result.Add(new SqlDiagnostic(table.NameStart, table.NameLength, Format(message, table, index, french)));
                    }
                }

                CheckColumns(sql, statement, defined, index, french, result);
            }

            return result;
        }

        private enum TableProblem
        {
            Unknown,
            UnknownInSchema,
        }

        private static TableProblem? UnknownTable(SqlTableRef table, SqlStatement statement, HashSet<string> defined, SqlSchemaIndex index)
        {
            if (table.Kind != SqlTableRefKind.Table || table.Quoted || table.MultiPart || table.Name.Length == 0
                || defined.Contains(table.Name) || statement.CteNames.Contains(table.Name) || SqlLanguage.IsSystemName(table.Name)
                || (table.Schema != null && SqlLanguage.IsSystemName(table.Schema)))
            {
                return null;
            }

            if (table.Schema != null)
            {
                if (index.FindSchema(table.Schema) is null)
                {
                    // A schema the snapshot does not have (another database, an extension's schema...): no opinion.
                    return null;
                }

                return index.FindTable(table.Schema, table.Name) is null ? TableProblem.UnknownInSchema : (TableProblem?)null;
            }

            return index.FindTables(table.Name).Count == 0 ? TableProblem.Unknown : (TableProblem?)null;
        }

        private static string Format(TableProblem? problem, SqlTableRef table, SqlSchemaIndex index, bool french)
        {
            var connection = index.ConnectionLabel;
            if (problem == TableProblem.UnknownInSchema)
            {
                return french
                    ? $"Table inconnue « {table.Name} » dans le schéma « {table.Schema} » (schéma de la connexion {connection})"
                    : $"Unknown table '{table.Name}' in schema '{table.Schema}' (schema of the {connection} connection)";
            }

            return french
                ? $"Table inconnue « {table.Name} » (schéma de la connexion {connection})"
                : $"Unknown table '{table.Name}' (schema of the {connection} connection)";
        }

        private static void CheckColumns(string sql, SqlStatement statement, HashSet<string> defined, SqlSchemaIndex index, bool french, List<SqlDiagnostic> result)
        {
            var tokens = statement.Tokens;
            for (int i = 0; i + 2 < tokens.Count; i++)
            {
                var qualifierToken = tokens[i];
                var dot = tokens[i + 1];
                var columnToken = tokens[i + 2];
                if (qualifierToken.Kind != SqlTokenKind.Identifier || !dot.IsPunctuation(sql, '.') || columnToken.Kind != SqlTokenKind.Identifier)
                {
                    continue;
                }

                // Adjacent `a.b` only; not the middle of `a.b.c`, not a call `a.f(…)`, not a table name `schema.table`.
                if (qualifierToken.End != dot.Start || dot.End != columnToken.Start
                    || (i > 0 && tokens[i - 1].IsPunctuation(sql, '.'))
                    || (i + 3 < tokens.Count && (tokens[i + 3].IsPunctuation(sql, '.') || tokens[i + 3].IsPunctuation(sql, '(')))
                    || statement.Tables.Any(t => t.NameStart == columnToken.Start))
                {
                    continue;
                }

                var qualifier = qualifierToken.GetText(sql);
                var table = statement.Resolve(qualifier);
                if (table is null || table.Kind != SqlTableRefKind.Table || table.Quoted || table.MultiPart || defined.Contains(table.Name)
                    || statement.CteNames.Contains(table.Name))
                {
                    continue;
                }

                var info = table.Schema is null ? index.FindTable(null, table.Name) : (index.FindSchema(table.Schema) is null ? null : index.FindTable(table.Schema, table.Name));
                if (info is null || info.Columns.Count == 0)
                {
                    continue;
                }

                var column = columnToken.GetText(sql);
                if (info.FindColumn(column) != null || HasOtherTable(index, table, column))
                {
                    continue;
                }

                var message = french
                    ? $"Colonne inconnue « {column} » dans « {info.Name} » (schéma de la connexion {info.Connection})"
                    : $"Unknown column '{column}' in '{info.Name}' (schema of the {info.Connection} connection)";
                result.Add(new SqlDiagnostic(columnToken.Start, columnToken.Length, message));
            }
        }

        /// <summary>
        /// When an unqualified table name exists in several schemas (or connections), the column may belong to another one
        /// than the default: no warning unless no candidate has it.
        /// </summary>
        private static bool HasOtherTable(SqlSchemaIndex index, SqlTableRef table, string column) =>
            table.Schema is null && index.FindTables(table.Name).Any(t => t.FindColumn(column) != null);
    }
}
