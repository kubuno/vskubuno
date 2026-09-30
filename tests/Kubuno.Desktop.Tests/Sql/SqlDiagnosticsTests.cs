using System.Linq;
using Kubuno.Desktop.Logic.Sql;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Kubuno.Desktop.Tests.Sql.SqlFixtures;

namespace Kubuno.Desktop.Tests.Sql
{
    [TestClass]
    public sealed class SqlDiagnosticsTests
    {
        [TestMethod]
        public void UnknownColumnAfterAnAlias()
        {
            var sql = "SELECT c.emial, c.name FROM customers c WHERE c.id = $1";
            var diagnostic = Diagnose(sql, Shop).Single();
            Assert.AreEqual("emial", sql.Substring(diagnostic.Start, diagnostic.Length));
            Assert.AreEqual("Colonne inconnue « emial » dans « customers » (schéma de la connexion Shop)", diagnostic.Message);
            Assert.AreEqual("Unknown column 'emial' in 'customers' (schema of the Shop connection)", Diagnose(sql, Shop, french: false).Single().Message);
        }

        [TestMethod]
        public void UnknownColumnAfterATableNameAndUnknownTables()
        {
            CollectionAssert.AreEqual(new[] { "totl" }, Flagged("SELECT orders.totl FROM orders", Shop));
            CollectionAssert.AreEqual(new[] { "custmers" }, Flagged("SELECT * FROM custmers", Shop));
            CollectionAssert.AreEqual(new[] { "ordres", "customer" }, Flagged("SELECT * FROM customers c JOIN ordres o ON o.id = c.id; UPDATE customer SET name = ?", Shop));
            CollectionAssert.AreEqual(new[] { "custmers" }, Flagged("INSERT INTO custmers (name) VALUES (?)", Shop));
            CollectionAssert.AreEqual(new[] { "custmers" }, Flagged("DELETE FROM custmers WHERE id = ?", Shop));
            Assert.AreEqual("Table inconnue « custmers » (schéma de la connexion Shop)", Diagnose("SELECT * FROM custmers", Shop).Single().Message);
        }

        [TestMethod]
        public void SchemaQualifiedNames()
        {
            CollectionAssert.AreEqual(new[] { "invoice" }, Flagged("SELECT * FROM billing.invoice", Sales));
            StringAssert.Contains(Diagnose("SELECT * FROM billing.invoice", Sales).Single().Message, "dans le schéma « billing »");
            CollectionAssert.AreEqual(new string[0], Flagged("SELECT i.amount FROM billing.invoices i", Sales));
            CollectionAssert.AreEqual(new[] { "amont" }, Flagged("SELECT i.amont FROM billing.invoices i", Sales));
        }

        [TestMethod]
        public void ValidSqlIsNotFlagged()
        {
            var valid = new[]
            {
                "SELECT c.id, c.name, c.email FROM customers c",
                "SELECT customers.email FROM customers",
                "SELECT o.total, t.total FROM orders o JOIN order_totals t ON t.customer_id = o.customer_id",
                "SELECT * FROM CUSTOMERS C WHERE C.EMAIL IS NOT NULL",
                "UPDATE orders SET status = 'paid' WHERE id = $1 RETURNING id",
                "INSERT INTO orders (customer_id, total, status) VALUES (?1, ?2, ?3)",
                "INSERT INTO customers (name) VALUES (?) ON CONFLICT (email) DO UPDATE SET name = excluded.name",
                "SELECT c.* FROM customers c",
            };
            foreach (var sql in valid)
            {
                CollectionAssert.AreEqual(new string[0], Flagged(sql, Shop), sql);
            }
        }

        [TestMethod]
        public void NoFalsePositives()
        {
            var cases = new[]
            {
                // CTE names and their columns.
                "WITH recent AS (SELECT id, total FROM orders) SELECT r.whatever FROM recent r",
                "WITH RECURSIVE tree(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM tree WHERE n < 5) SELECT n FROM tree",
                // Subquery and set-returning function aliases.
                "SELECT s.anything FROM (SELECT id FROM orders) s",
                "SELECT s.anything FROM (SELECT id FROM orders) AS s(anything)",
                "SELECT g.x FROM generate_series(1, 3) g",
                // Quoted identifiers are never guessed at.
                "SELECT \"c\".\"emial\" FROM \"Customers\" \"c\"",
                "SELECT * FROM \"NotThere\"",
                // A schema the snapshot does not have, system tables, three-part names.
                "SELECT * FROM other.stuff",
                "SELECT name FROM sqlite_master",
                "SELECT * FROM pg_catalog.pg_class",
                "SELECT * FROM information_schema.tables",
                "SELECT * FROM db.main.customers",
                // Tables the same text creates, alters or renames.
                "CREATE TABLE audit (id INTEGER); INSERT INTO audit (id) VALUES (1)",
                "CREATE TEMP TABLE IF NOT EXISTS scratch AS SELECT id FROM orders; SELECT s.id FROM scratch s",
                "ALTER TABLE customers ADD COLUMN phone TEXT; SELECT c.phone FROM customers c",
                "ALTER TABLE customers RENAME TO clients; SELECT * FROM clients",
                // FROM that names no table.
                "SELECT EXTRACT(YEAR FROM created_at) FROM customers",
                "SELECT substring(name FROM 2 FOR 3) FROM customers",
                "SELECT * FROM customers WHERE email IS DISTINCT FROM name",
                // An ambiguous alias (same name for two tables in the statement).
                "SELECT x.foo FROM customers x WHERE EXISTS (SELECT 1 FROM orders x)",
                // Record fields, not columns: `excluded.`, `new.` in upserts / triggers.
                "INSERT INTO orders (id) VALUES (1) ON CONFLICT (id) DO UPDATE SET total = excluded.whatever",
                // Unqualified columns are never checked.
                "SELECT nothing_like_this FROM customers",
                // UPDATE of ON DUPLICATE KEY / FOR UPDATE.
                "INSERT INTO orders (id) VALUES (1) ON DUPLICATE KEY UPDATE status = 'x'",
                "SELECT * FROM orders FOR UPDATE",
            };
            foreach (var sql in cases)
            {
                CollectionAssert.AreEqual(new string[0], Flagged(sql, Shop), sql);
            }
        }

        [TestMethod]
        public void NothingWithoutASnapshot()
        {
            Assert.AreEqual(0, Diagnose("SELECT c.emial FROM custmers c", SqlSchemaIndex.Empty).Count);
        }

        [TestMethod]
        public void MergedSnapshotsKnowEveryConnectionsTables()
        {
            var both = new SqlSchemaIndex(new[] { Snapshot("Shop"), Snapshot("Sales") });
            Assert.AreEqual("Shop, Sales", both.ConnectionLabel);
            CollectionAssert.AreEqual(new string[0], Flagged("SELECT * FROM customers c JOIN products p ON p.id = c.id", both));
            var diagnostic = Diagnose("SELECT p.nam FROM products p", both).Single();
            StringAssert.Contains(diagnostic.Message, "connexion Sales");
            StringAssert.Contains(Diagnose("SELECT * FROM nowhere", both).Single().Message, "connexion Shop, Sales");
        }
    }
}
