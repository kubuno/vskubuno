using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kubuno.Desktop.Logic.Sql;

namespace Kubuno.Desktop.Tests.Sql
{
    /// <summary>The schema snapshot fixtures (Fixtures/Sql, the <c>schema.load</c> format) and caret helpers.</summary>
    internal static class SqlFixtures
    {
        public static string Path(string connection) => System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "Sql", connection + ".json");

        public static string Json(string connection) => File.ReadAllText(Path(connection));

        public static SchemaSnapshot Snapshot(string connection) => SchemaSnapshot.Parse(Json(connection), connection);

        /// <summary>SQLite <c>main</c>: customers(id, name, email, created_at), orders(id, customer_id, total, status), view order_totals.</summary>
        public static SqlSchemaIndex Shop => new SqlSchemaIndex(new[] { Snapshot("Shop") });

        /// <summary>PostgreSQL: public.products(id, name, "UnitPrice"), billing.invoices(id, product_id, amount), billing.invoice_total().</summary>
        public static SqlSchemaIndex Sales => new SqlSchemaIndex(new[] { Snapshot("Sales") });

        /// <summary>Splits <c>"SELECT | FROM t"</c> into the SQL and the caret offset.</summary>
        public static (string Sql, int Caret) Caret(string marked)
        {
            int caret = marked.IndexOf('|');
            if (caret < 0)
            {
                throw new ArgumentException("No | caret marker.", nameof(marked));
            }

            return (marked.Remove(caret, 1), caret);
        }

        public static IReadOnlyList<SqlCompletionEntry> Complete(string marked, SqlSchemaIndex index, out SqlCompletionContext context, bool french = false, bool plainRustString = false)
        {
            var (sql, caret) = Caret(marked);
            return SqlCompletion.GetEntries(sql, SqlTokenizer.Tokenize(sql), caret, index, french, plainRustString, out context);
        }

        public static string[] Names(IEnumerable<SqlCompletionEntry> entries, SqlCompletionKind kind) =>
            entries.Where(e => e.Kind == kind).Select(e => e.DisplayText).ToArray();

        public static IReadOnlyList<SqlDiagnostic> Diagnose(string sql, SqlSchemaIndex index, bool french = true) =>
            SqlDiagnostics.Analyze(sql, SqlTokenizer.Tokenize(sql), index, french);

        /// <summary>The flagged names.</summary>
        public static string[] Flagged(string sql, SqlSchemaIndex index) =>
            Diagnose(sql, index).Select(d => sql.Substring(d.Start, d.Length)).ToArray();
    }
}
