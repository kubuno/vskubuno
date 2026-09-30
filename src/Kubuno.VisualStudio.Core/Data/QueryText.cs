using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Kubuno.VisualStudio.Core.Data
{
    /// <summary>SQL text helpers of the query window: identifier quoting, the text shown for "Show data", what F5 runs.</summary>
    public static class QueryText
    {
        private static readonly Regex RowsAffected = new Regex(@"^\s*(\d+)\s+row\(s\)\s+affected\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>A provider-quoted identifier: <c>"x"</c> (PostgreSQL, SQLite), <c>`x`</c> (MySQL), <c>[x]</c> (SQL Server).</summary>
        public static string QuoteIdentifier(DataProviderKind? provider, string name) => provider switch
        {
            DataProviderKind.MySql => "`" + name.Replace("`", "``") + "`",
            DataProviderKind.SqlServer => "[" + name.Replace("]", "]]") + "]",
            _ => "\"" + name.Replace("\"", "\"\"") + "\"",
        };

        /// <summary>A qualified object name; SQLite's implicit <c>main</c> schema is left out.</summary>
        public static string QualifiedName(DataProviderKind? provider, string? schema, string table)
        {
            bool omitSchema = string.IsNullOrEmpty(schema) || (provider == DataProviderKind.Sqlite && string.Equals(schema, "main", StringComparison.OrdinalIgnoreCase));
            return omitSchema ? QuoteIdentifier(provider, table) : QuoteIdentifier(provider, schema!) + "." + QuoteIdentifier(provider, table);
        }

        /// <summary>The SELECT "Show data" runs (<c>data.top</c>), shown in the editor of its query window.</summary>
        public static string TopSelect(DataProviderKind? provider, string? schema, string table, int limit)
        {
            string name = QualifiedName(provider, schema, table);
            string n = limit.ToString(CultureInfo.InvariantCulture);
            return provider == DataProviderKind.SqlServer ? $"SELECT TOP ({n}) * FROM {name};" : $"SELECT * FROM {name} LIMIT {n};";
        }

        /// <summary>What Execute runs: the selection when there is one (not only whitespace), else the whole text.</summary>
        public static string ToExecute(string text, string? selection) =>
            !string.IsNullOrWhiteSpace(selection) ? selection! : text;

        /// <summary>A tool message in the UI language ("3 row(s) affected" → "3 ligne(s) affectée(s)").</summary>
        public static string LocalizeMessage(string message)
        {
            var match = RowsAffected.Match(message);
            if (match.Success)
            {
                return DataText.T($"{match.Groups[1].Value} row(s) affected", $"{match.Groups[1].Value} ligne(s) affectée(s)");
            }

            if (string.Equals(message.Trim(), "Command completed.", StringComparison.Ordinal))
            {
                return DataText.T("Command completed.", "Commande terminée.");
            }

            return message;
        }

        /// <summary>The status line after a query: rows, elapsed time, truncation.</summary>
        public static string Status(QueryResultInfo result, int maxRows)
        {
            int rows = result.ResultSets.Sum(r => r.Rows.Count);
            bool truncated = result.ResultSets.Any(r => r.Truncated);
            string text = DataText.T($"{rows} row(s)", $"{rows} ligne(s)") + $" - {result.ElapsedMs} ms";
            if (truncated)
            {
                text += " - " + Truncated(maxRows);
            }

            return text;
        }

        /// <summary>The status line of a "Show data" window.</summary>
        public static string Status(ResultSetInfo result, long elapsedMs, int limit)
        {
            string text = DataText.T($"{result.Rows.Count} row(s)", $"{result.Rows.Count} ligne(s)") + $" - {elapsedMs} ms";
            if (result.Truncated || result.Rows.Count >= limit)
            {
                text += " - " + Truncated(limit);
            }

            return text;
        }

        public static string Truncated(int maxRows) =>
            DataText.T($"first {maxRows} rows", $"{maxRows} premières lignes");

        /// <summary>The result grid's column headers, made unique (a result may repeat a name: <c>id</c>, <c>id</c>).</summary>
        public static IReadOnlyList<string> ColumnHeaders(ResultSetInfo result)
        {
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            var headers = new List<string>();
            foreach (var column in result.Columns)
            {
                string name = string.IsNullOrEmpty(column.Name) ? "(" + DataText.T("no name", "sans nom") + ")" : column.Name;
                if (seen.TryGetValue(name, out int count))
                {
                    seen[name] = count + 1;
                    headers.Add($"{name} ({count + 1})");
                }
                else
                {
                    seen[name] = 1;
                    headers.Add(name);
                }
            }

            return headers;
        }
    }
}
