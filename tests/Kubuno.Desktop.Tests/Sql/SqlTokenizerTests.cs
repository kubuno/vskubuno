using System.Linq;
using Kubuno.VisualStudio.Core.Sql;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.VisualStudio.Tests.Sql
{
    [TestClass]
    public sealed class SqlTokenizerTests
    {
        private static (SqlTokenKind Kind, string Text)[] Tokens(string sql) =>
            SqlTokenizer.Tokenize(sql).Select(t => (t.Kind, t.GetText(sql))).ToArray();

        private static SqlTokenKind KindOf(string sql, string text) =>
            SqlTokenizer.Tokenize(sql).First(t => t.GetText(sql) == text).Kind;

        [TestMethod]
        public void KeywordsNamesNumbersStringsAndOperators()
        {
            var sql = "SELECT c.name, count(*) AS n FROM customers c WHERE c.total >= 10.5e2 AND c.email LIKE 'a''b%' GROUP BY 1";
            var tokens = Tokens(sql);
            CollectionAssert.Contains(tokens, (SqlTokenKind.Keyword, "SELECT"));
            CollectionAssert.Contains(tokens, (SqlTokenKind.Identifier, "c"));
            CollectionAssert.Contains(tokens, (SqlTokenKind.Punctuation, "."));
            CollectionAssert.Contains(tokens, (SqlTokenKind.Identifier, "name"));
            CollectionAssert.Contains(tokens, (SqlTokenKind.Function, "count"));
            CollectionAssert.Contains(tokens, (SqlTokenKind.Operator, "*"));
            CollectionAssert.Contains(tokens, (SqlTokenKind.Operator, ">="));
            CollectionAssert.Contains(tokens, (SqlTokenKind.Number, "10.5e2"));
            CollectionAssert.Contains(tokens, (SqlTokenKind.String, "'a''b%'"));
            CollectionAssert.Contains(tokens, (SqlTokenKind.Number, "1"));
            Assert.AreEqual(SqlTokenKind.Keyword, KindOf(sql, "GROUP"));
        }

        [TestMethod]
        public void EveryParameterStyle()
        {
            var sql = "SELECT $1, ?, ?2, @name, :other, x::text, @@version FROM t";
            var parameters = Tokens(sql).Where(t => t.Kind == SqlTokenKind.Parameter).Select(t => t.Text).ToArray();
            CollectionAssert.AreEqual(new[] { "$1", "?", "?2", "@name", ":other" }, parameters);
            Assert.AreEqual(SqlTokenKind.Operator, KindOf(sql, "::"));
            Assert.AreEqual(SqlTokenKind.Identifier, KindOf(sql, "text"));
        }

        [TestMethod]
        public void CommentsAndDollarQuotes()
        {
            var sql = "SELECT 1 -- trailing 'x'\n/* a /* nested */ still */ SELECT $$ it's -- not a comment $$, $fn$ x $fn$";
            var tokens = Tokens(sql);
            CollectionAssert.Contains(tokens, (SqlTokenKind.Comment, "-- trailing 'x'"));
            CollectionAssert.Contains(tokens, (SqlTokenKind.Comment, "/* a /* nested */ still */"));
            CollectionAssert.Contains(tokens, (SqlTokenKind.String, "$$ it's -- not a comment $$"));
            CollectionAssert.Contains(tokens, (SqlTokenKind.String, "$fn$ x $fn$"));
        }

        [TestMethod]
        public void QuotedIdentifiers()
        {
            var sql = "SELECT \"Order Id\", `key` FROM \"my\"\"table\"";
            var quoted = SqlTokenizer.Tokenize(sql).Where(t => t.Kind == SqlTokenKind.QuotedIdentifier).Select(t => t.GetName(sql)).ToArray();
            CollectionAssert.AreEqual(new[] { "Order Id", "key", "my\"table" }, quoted);
        }

        [TestMethod]
        public void NamesAfterADotAndTableNamesBeforeAParenAreNotKeywordsOrFunctions()
        {
            var sql = "INSERT INTO orders(id, status) SELECT o.order, o.key FROM orders o JOIN t2 ON t2.id = o.id";
            Assert.AreEqual(SqlTokenKind.Identifier, KindOf(sql, "orders"));
            Assert.AreEqual(SqlTokenKind.Identifier, KindOf(sql, "order"));
            Assert.AreEqual(SqlTokenKind.Identifier, KindOf(sql, "key"));

            var call = "SELECT my_fn(1), main.other_fn(2), left(name, 2) FROM t LEFT JOIN u ON true";
            Assert.AreEqual(SqlTokenKind.Function, KindOf(call, "my_fn"));
            Assert.AreEqual(SqlTokenKind.Function, KindOf(call, "other_fn"));
            Assert.AreEqual(SqlTokenKind.Function, KindOf(call, "left"));
            Assert.AreEqual(SqlTokenKind.Keyword, KindOf(call, "LEFT"));
            Assert.AreEqual(SqlTokenKind.Function, KindOf("SELECT CURRENT_TIMESTAMP", "CURRENT_TIMESTAMP"));
        }

        [TestMethod]
        public void CommonColumnNamesAreNotKeywords()
        {
            foreach (var word in new[] { "name", "type", "date", "text", "status", "value", "email" })
            {
                Assert.IsFalse(SqlLanguage.IsKeyword(word), word);
            }
        }

        [TestMethod]
        public void UnterminatedTokensRunToTheEnd()
        {
            var tokens = Tokens("SELECT 'abc");
            Assert.AreEqual((SqlTokenKind.String, "'abc"), tokens.Last());
            Assert.AreEqual((SqlTokenKind.Comment, "/* x"), Tokens("SELECT /* x").Last());
        }
    }
}
