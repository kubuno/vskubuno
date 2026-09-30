using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Kubuno.VisualStudio.Core.Sql;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.VisualStudio.Tests.Sql
{
    [TestClass]
    public sealed class RustSqlLiteralScannerTests
    {
        private static string[] Sqls(string rust) => RustSqlLiteralScanner.Scan(rust).Select(l => l.Sql.Text).ToArray();

        [TestMethod]
        public void MacroFormsWithAndWithoutThePrefix()
        {
            var rust = @"
let a = sqlx::query!(""SELECT id FROM customers WHERE id = $1"", id);
let b = query!(""SELECT 1"");
let c = sqlx::query_scalar!(""SELECT count(*) FROM orders"");
let d = sqlx::query_unchecked!(""DELETE FROM orders"");
let e = sqlx::query_scalar_unchecked!(""SELECT 2"");
";
            CollectionAssert.AreEqual(
                new[] { "SELECT id FROM customers WHERE id = $1", "SELECT 1", "SELECT count(*) FROM orders", "DELETE FROM orders", "SELECT 2" },
                Sqls(rust));
            var origins = RustSqlLiteralScanner.Scan(rust).Select(l => l.Origin).ToArray();
            CollectionAssert.AreEqual(new[] { "sqlx::query!", "query!", "sqlx::query_scalar!", "sqlx::query_unchecked!", "sqlx::query_scalar_unchecked!" }, origins);
        }

        [TestMethod]
        public void QueryAsTakesTheSecondArgument()
        {
            var rust = @"
let rows = sqlx::query_as!(Customer, ""SELECT * FROM customers"").fetch_all(&pool).await?;
let x = query_as_unchecked!(Wrapper<HashMap<String, i64>>, r#""SELECT ""name"" FROM t""#, a);
let y = sqlx::query_as!(Customer, CONST_SQL); // not a literal
";
            CollectionAssert.AreEqual(new[] { "SELECT * FROM customers", "SELECT \"name\" FROM t" }, Sqls(rust));
        }

        [TestMethod]
        public void FunctionFormsWithTurbofish()
        {
            var rust = @"
sqlx::query(""UPDATE t SET a = ?"").bind(1);
sqlx::query_as::<_, Customer>(""SELECT * FROM customers WHERE id = ?1"");
sqlx::query_scalar::<_, i64>(r""SELECT count(*) FROM t"");
sqlx::raw_sql(""CREATE TABLE x (id INTEGER)"");
";
            CollectionAssert.AreEqual(new[] { "UPDATE t SET a = ?", "SELECT * FROM customers WHERE id = ?1", "SELECT count(*) FROM t", "CREATE TABLE x (id INTEGER)" }, Sqls(rust));
        }

        [TestMethod]
        public void BareFunctionsOnlyWhenTheTextReadsLikeSql()
        {
            var rust = @"
use sqlx::query;
query(""SELECT 1"");
query(""hello world"");
http.query(""name=value"");
fn query(s: &str) {}
";
            CollectionAssert.AreEqual(new[] { "SELECT 1" }, Sqls(rust));
        }

        [TestMethod]
        public void KubunoDataForms()
        {
            var rust = @"
let cmd = DbCommand::with_text(""SELECT * FROM customers"");
let adapter = TableAdapter::new(""Shop"", ""SELECT id, name FROM customers"");
cmd.command_text = ""DELETE FROM orders WHERE id = @id"".into();
let c = DbCommand { command_text: ""SELECT 1"".to_string(), ..Default::default() };
adapter.update_command = String::from(""UPDATE x""); // not a direct literal
let p = DbCommand::procedure(""refresh_totals""); // a procedure name, not SQL
if cmd.command_text == ""x"" {}
";
            CollectionAssert.AreEqual(
                new[] { "SELECT * FROM customers", "SELECT id, name FROM customers", "DELETE FROM orders WHERE id = @id", "SELECT 1" },
                Sqls(rust));
        }

        [TestMethod]
        public void RawStringsWithHashesAndMultipleLines()
        {
            var rust = "let q = sqlx::query!(r##\"\nSELECT \"#id\"\nFROM customers -- \"# still inside\n\"##);";
            var literal = RustSqlLiteralScanner.Scan(rust).Single();
            Assert.AreEqual("\nSELECT \"#id\"\nFROM customers -- \"# still inside\n", literal.Sql.Text);
            Assert.IsTrue(literal.IsRaw);
            Assert.AreEqual(2, literal.Hashes);
            Assert.AreEqual(rust.IndexOf("r##", StringComparison.Ordinal), literal.Start);
            Assert.AreEqual(rust.IndexOf("\"##)", StringComparison.Ordinal) + 3, literal.End);
        }

        [TestMethod]
        public void EscapesAreDecodedAndMappedBack()
        {
            var rust = "sqlx::query!(\"SELECT \\\"name\\\" FROM t\\n WHERE a = 'x' \\\n       AND b = 1\")";
            var literal = RustSqlLiteralScanner.Scan(rust).Single();
            Assert.AreEqual("SELECT \"name\" FROM t\n WHERE a = 'x' AND b = 1", literal.Sql.Text);

            // Each decoded character maps back to its source character.
            int from = literal.Sql.Text.IndexOf("FROM", StringComparison.Ordinal);
            Assert.AreEqual("FROM", rust.Substring(literal.ToDocument(from), 4));
            int and = literal.Sql.Text.IndexOf("AND", StringComparison.Ordinal);
            Assert.AreEqual("AND", rust.Substring(literal.ToDocument(and), 3));
            Assert.AreEqual(and, literal.ToSql(rust.IndexOf("AND", StringComparison.Ordinal)));
            Assert.AreEqual(literal.Sql.Text.Length, literal.ToSql(literal.ContentEnd));
        }

        [TestMethod]
        public void CommentsStringsAndCharsNeverStartASqlLiteral()
        {
            var rust = @"
// sqlx::query!(""SELECT 1"")
/* outer /* nested sqlx::query!(""SELECT 2"") */ still comment query!(""SELECT 3"") */
let s = ""query!(\""SELECT 4\"")"";
let c = '""'; let d = '\''; let life: &'static str = ""x"";
fn f<'a>(x: &'a str) -> &'a str { x }
let q = sqlx::query!(""SELECT 5"");
let b = b""query!(\""SELECT 6\"")"";
";
            CollectionAssert.AreEqual(new[] { "SELECT 5" }, Sqls(rust));
        }

        [TestMethod]
        public void ByteStringsAndRawIdentifiersAreNotSql()
        {
            var rust = @"
let r#type = 1;
sqlx::query!(br""SELECT 1"");
let x = sqlx::query!(r#""SELECT 2""#);
";
            CollectionAssert.AreEqual(new[] { "SELECT 2" }, Sqls(rust));
        }

        [TestMethod]
        public void UnterminatedLiteralRunsToTheEnd()
        {
            var rust = "sqlx::query!(\"SELECT * FROM cust";
            var literal = RustSqlLiteralScanner.Scan(rust).Single();
            Assert.AreEqual("SELECT * FROM cust", literal.Sql.Text);
            Assert.AreEqual(rust.Length, literal.End);
            Assert.AreEqual(rust.Length, literal.ContentEnd);
        }

        [TestMethod]
        public void AnEditInsideALiteralIsAppliedWithoutRescanning()
        {
            var rust = "let a = sqlx::query!(\"SELECT id FROM customers\");\nlet b = sqlx::query!(\"SELECT 1\");";
            var before = RustSqlLiteralScanner.Scan(rust);
            int at = rust.IndexOf(" FROM", StringComparison.Ordinal);
            var after = rust.Insert(at, ", name");
            var updated = RustSqlLiteralScanner.TryApplyEdit(before, at, string.Empty, ", name", (s, l) => after.Substring(s, l));
            Assert.IsNotNull(updated);
            CollectionAssert.AreEqual(RustSqlLiteralScanner.Scan(after).Select(l => (l.Start, l.Length, l.Sql.Text)).ToArray(), updated!.Select(l => (l.Start, l.Length, l.Sql.Text)).ToArray());

            // The second literal keeps its tokens (shifted, not re-tokenized).
            Assert.AreSame(before[1].Sql, updated[1].Sql);

            // A quote, a backslash, or an edit outside any literal needs a full scan.
            Assert.IsNull(RustSqlLiteralScanner.TryApplyEdit(before, at, string.Empty, "\"", (s, l) => string.Empty));
            Assert.IsNull(RustSqlLiteralScanner.TryApplyEdit(before, at, string.Empty, "\\", (s, l) => string.Empty));
            Assert.IsNull(RustSqlLiteralScanner.TryApplyEdit(before, 0, string.Empty, "x", (s, l) => string.Empty));
        }

        [TestMethod]
        public void ScanningALargeFileIsFast()
        {
            var builder = new StringBuilder();
            for (int i = 0; i < 4000; i++)
            {
                builder.Append("fn f").Append(i).Append("(pool: &Pool) -> Result<()> { // comment ").Append(i).Append('\n')
                    .Append("    let x = sqlx::query!(r#\"SELECT id, name FROM customers WHERE id = $1\"#, 'a').fetch_one(pool); /* c */ Ok(())\n}\n");
            }

            var text = builder.ToString();
            RustSqlLiteralScanner.Scan(text);
            var watch = Stopwatch.StartNew();
            var literals = RustSqlLiteralScanner.Scan(text);
            watch.Stop();
            Assert.AreEqual(4000, literals.Count);
            Assert.IsTrue(watch.ElapsedMilliseconds < 500, "12 000 lines scanned in " + watch.ElapsedMilliseconds + " ms");
        }
    }
}
