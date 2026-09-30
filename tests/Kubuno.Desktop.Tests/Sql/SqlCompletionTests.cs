using System.Linq;
using Kubuno.VisualStudio.Core.Sql;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Kubuno.VisualStudio.Tests.Sql.SqlFixtures;

namespace Kubuno.VisualStudio.Tests.Sql
{
    [TestClass]
    public sealed class SqlCompletionTests
    {
        [TestMethod]
        public void AfterFromTheTablesAndViews()
        {
            var entries = Complete("SELECT * FROM |", Shop, out var context);
            Assert.AreEqual(SqlCompletionContextKind.Tables, context.Kind);
            CollectionAssert.AreEquivalent(new[] { "customers", "orders" }, Names(entries, SqlCompletionKind.Table));
            CollectionAssert.AreEquivalent(new[] { "order_totals" }, Names(entries, SqlCompletionKind.View));
            Assert.AreEqual(0, Names(entries, SqlCompletionKind.Keyword).Length);

            foreach (var marked in new[] { "SELECT * FROM customers c JOIN |", "INSERT INTO cu|", "UPDATE |", "DELETE FROM |", "SELECT * FROM customers, |" })
            {
                Complete(marked, Shop, out var other);
                Assert.AreEqual(SqlCompletionContextKind.Tables, other.Kind, marked);
            }
        }

        [TestMethod]
        public void InTheSelectListAndConditionsTheColumnsOfTheStatementTables()
        {
            var entries = Complete("SELECT | FROM customers c JOIN orders o ON o.customer_id = c.id", Shop, out var context);
            Assert.AreEqual(SqlCompletionContextKind.Columns, context.Kind);
            var columns = Names(entries, SqlCompletionKind.Column);
            CollectionAssert.AreEquivalent(new[] { "id", "name", "email", "created_at", "customer_id", "total", "status" }, columns);
            CollectionAssert.AreEquivalent(new[] { "c", "o" }, Names(entries, SqlCompletionKind.Alias));
            Assert.IsTrue(Names(entries, SqlCompletionKind.Keyword).Contains("FROM"));
            Assert.IsTrue(Names(entries, SqlCompletionKind.Function).Contains("COUNT"));

            var id = entries.First(e => e.Kind == SqlCompletionKind.Column && e.DisplayText == "id");
            StringAssert.Contains(id.Description, "customers.id");
            StringAssert.Contains(id.Description, "orders");
            Assert.AreEqual("INTEGER", id.Suffix);

            foreach (var marked in new[]
            {
                "SELECT id FROM customers WHERE |", "SELECT id FROM customers WHERE id = 1 AND |", "UPDATE customers SET |",
                "SELECT id FROM customers ORDER BY |", "SELECT id FROM customers GROUP BY name HAVING |", "SELECT count(|) FROM orders",
            })
            {
                var list = Complete(marked, Shop, out var other);
                Assert.AreEqual(SqlCompletionContextKind.Columns, other.Kind, marked);
                Assert.IsTrue(Names(list, SqlCompletionKind.Column).Length > 0, marked);
            }
        }

        [TestMethod]
        public void AliasDotGivesThatTablesColumnsWithTheirType()
        {
            var entries = Complete("SELECT c.| FROM customers c", Shop, out var context, french: true);
            Assert.AreEqual(SqlCompletionContextKind.Qualified, context.Kind);
            Assert.AreEqual("c", context.Qualifier);
            CollectionAssert.AreEqual(new[] { "id", "name", "email", "created_at" }, entries.Select(e => e.DisplayText).ToArray());
            var email = entries.Single(e => e.DisplayText == "email");
            StringAssert.Contains(email.Description, "(colonne) customers.email : TEXT");
            StringAssert.Contains(email.Description, "Option<String>");
            StringAssert.Contains(email.Description, "Connexion : Shop");
            StringAssert.Contains(entries.Single(e => e.DisplayText == "id").Description, "clé primaire");

            // The unaliased table name works as a qualifier too, and the prefix typed after the dot does not matter.
            CollectionAssert.AreEqual(new[] { "id", "customer_id", "total", "status" }, Complete("SELECT orders.to| FROM orders", Shop, out _).Select(e => e.DisplayText).ToArray());
        }

        [TestMethod]
        public void SchemaDotGivesThatSchemasTablesAndFunctions()
        {
            var entries = Complete("SELECT * FROM billing.|", Sales, out var context);
            Assert.AreEqual(SqlCompletionContextKind.Qualified, context.Kind);
            CollectionAssert.AreEqual(new[] { "invoices" }, Names(entries, SqlCompletionKind.Table));
            CollectionAssert.AreEqual(new[] { "invoice_total" }, Names(entries, SqlCompletionKind.Function));
            Assert.AreEqual("invoices", entries.First(e => e.Kind == SqlCompletionKind.Table).InsertText);
        }

        [TestMethod]
        public void TablesOfANonDefaultSchemaAreInsertedQualified()
        {
            var entries = Complete("SELECT * FROM |", Sales, out _);
            Assert.AreEqual("products", entries.Single(e => e.DisplayText == "products").InsertText);
            Assert.AreEqual("billing.invoices", entries.Single(e => e.DisplayText == "invoices").InsertText);
            Assert.AreEqual("billing", entries.Single(e => e.DisplayText == "invoices").Suffix);
            CollectionAssert.AreEqual(new[] { "billing" }, Names(entries, SqlCompletionKind.Schema));
        }

        [TestMethod]
        public void NamesThatNeedQuotesAreQuotedAndEscapedInAPlainRustString()
        {
            var raw = Complete("SELECT p.| FROM products p", Sales, out _);
            Assert.AreEqual("\"UnitPrice\"", raw.Single(e => e.DisplayText == "UnitPrice").InsertText);
            var plain = Complete("SELECT p.| FROM products p", Sales, out _, plainRustString: true);
            Assert.AreEqual("\\\"UnitPrice\\\"", plain.Single(e => e.DisplayText == "UnitPrice").InsertText);
            Assert.AreEqual("`key`", SqlCompletion.Quote("key", "mysql", false));
            Assert.AreEqual("[Order Id]", SqlCompletion.Quote("Order Id", "sqlserver", false));
            Assert.AreEqual("email", SqlCompletion.Quote("email", "postgres", true));
        }

        [TestMethod]
        public void InsertColumnListGivesOnlyThatTablesColumns()
        {
            var entries = Complete("INSERT INTO orders (customer_id, |) VALUES (?, ?)", Shop, out var context);
            Assert.AreEqual(SqlCompletionContextKind.Columns, context.Kind);
            CollectionAssert.AreEqual(new[] { "id", "customer_id", "total", "status" }, entries.Select(e => e.DisplayText).ToArray());
        }

        [TestMethod]
        public void CteAndSubqueryAliasesAreOfferedButHaveNoKnownColumns()
        {
            var tables = Complete("WITH recent AS (SELECT * FROM orders) SELECT * FROM |", Shop, out _);
            CollectionAssert.Contains(Names(tables, SqlCompletionKind.Cte), "recent");

            Assert.AreEqual(0, Complete("SELECT s.| FROM (SELECT id FROM orders) s", Shop, out _).Count);
            Assert.AreEqual(0, Complete("WITH r AS (SELECT 1 AS x) SELECT r.| FROM r", Shop, out _).Count);
        }

        [TestMethod]
        public void WithoutASnapshotOnlyKeywordsAndBuiltInFunctions()
        {
            var entries = Complete("SELECT | FROM customers", SqlSchemaIndex.Empty, out var context);
            Assert.AreEqual(SqlCompletionContextKind.Columns, context.Kind);
            Assert.IsTrue(entries.All(e => e.Kind == SqlCompletionKind.Keyword || e.Kind == SqlCompletionKind.Function));
            Assert.AreEqual(0, Complete("SELECT * FROM |", SqlSchemaIndex.Empty, out _).Count);
        }

        [TestMethod]
        public void NothingInsideSqlStringsCommentsAndParameters()
        {
            foreach (var marked in new[] { "SELECT 'abc|' FROM t", "SELECT 1 -- note |", "SELECT /* x| */ 1", "SELECT * FROM t WHERE id = @na|", "SELECT $1|" })
            {
                Assert.AreEqual(0, Complete(marked, Shop, out var context).Count, marked);
                Assert.AreEqual(SqlCompletionContextKind.None, context.Kind, marked);
            }
        }

        [TestMethod]
        public void KeywordsFollowTheCaseTheTextUses()
        {
            var lower = Complete("select id from customers wh|", Shop, out _);
            Assert.IsTrue(Names(lower, SqlCompletionKind.Keyword).Contains("where"));
            var upper = Complete("SELECT id FROM customers WH|", Shop, out _);
            Assert.IsTrue(Names(upper, SqlCompletionKind.Keyword).Contains("WHERE"));
            var first = Complete("s|", Shop, out _);
            Assert.IsTrue(Names(first, SqlCompletionKind.Keyword).Contains("select"));
        }

        [TestMethod]
        public void StatementsAreSeparated()
        {
            var entries = Complete("SELECT * FROM orders; SELECT | FROM customers", Shop, out _);
            var columns = Names(entries, SqlCompletionKind.Column);
            CollectionAssert.DoesNotContain(columns, "status");
            CollectionAssert.Contains(columns, "email");
        }

        [TestMethod]
        public void SpaceTriggers()
        {
            bool Trigger(string marked, bool loaded = true)
            {
                var (sql, caret) = Caret(marked);
                return SqlCompletion.IsSpaceTrigger(sql, SqlTokenizer.Tokenize(sql), caret, loaded);
            }

            Assert.IsTrue(Trigger("SELECT * FROM |"));
            Assert.IsTrue(Trigger("select * from customers c join |"));
            Assert.IsTrue(Trigger("SELECT |"));
            Assert.IsFalse(Trigger("SELECT * FROM |", loaded: false));
            Assert.IsFalse(Trigger("SELECT id |"));
            Assert.IsFalse(Trigger("SELECT 'FROM |"));
            Assert.IsFalse(Trigger("SELECT * FROM  |"));
        }

        [TestMethod]
        public void EveryIconExistsInTheInstalledImageCatalog()
        {
            var catalog = System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles),
                @"Microsoft Visual Studio\18\Community\Common7\IDE\Microsoft.VisualStudio.ImageCatalog.dll");
            if (!System.IO.File.Exists(catalog))
            {
                Assert.Inconclusive("Visual Studio 2026 Community is not installed at the expected path: " + catalog);
            }

            var knownMonikers = System.Reflection.Assembly.LoadFrom(catalog).GetType("Microsoft.VisualStudio.Imaging.KnownMonikers", throwOnError: true)!;
            var missing = SqlCompletion.AllIconMonikerNames.Distinct()
                .Where(name => knownMonikers.GetProperty(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static) == null)
                .ToList();
            Assert.AreEqual(0, missing.Count, "Unknown KnownMonikers: " + string.Join(", ", missing));
        }
    }
}
