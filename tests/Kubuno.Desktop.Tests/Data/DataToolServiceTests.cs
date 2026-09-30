using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Desktop.Logic.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Desktop.Tests.Data
{
    [TestClass]
    public sealed class DataToolServiceTests
    {
        [TestCleanup]
        public void Cleanup() => DataText.ForceFrench = null;

        [TestMethod]
        public async Task TargetsAreSerializedAsTheProtocolSays()
        {
            var client = new ScriptedClient((m, p) => new JsonObject { ["serverVersion"] = "3.45.1", ["elapsedMs"] = 12 });
            var service = new DataToolService(client);

            var test = await service.TestConnectionAsync(DataConnectionTarget.Inline(DataProviderKind.Sqlite, "sqlite:C:\\db\\shop.db?mode=rwc"), CancellationToken.None);
            await service.TestConnectionAsync(DataConnectionTarget.Explorer("Shop"), CancellationToken.None);
            await service.TestConnectionAsync(DataConnectionTarget.Project(@"C:\src\app", "Shop", DataProviderKind.Postgres), CancellationToken.None);

            Assert.AreEqual("3.45.1", test.ServerVersion);
            Assert.AreEqual(12, test.ElapsedMs);
            Assert.AreEqual("{\"target\":{\"provider\":\"sqlite\",\"connectionString\":\"sqlite:C:\\\\db\\\\shop.db?mode=rwc\"}}", client.Requests[0].Parameters!.ToJsonString());
            Assert.AreEqual("{\"target\":{\"explorer\":\"Shop\"}}", client.Requests[1].Parameters!.ToJsonString());
            Assert.AreEqual("{\"target\":{\"project\":{\"manifestDir\":\"C:\\\\src\\\\app\",\"connection\":\"Shop\",\"provider\":\"postgres\"}}}", client.Requests[2].Parameters!.ToJsonString());
            Assert.AreEqual("sqlite", DataConnectionTarget.Inline(DataProviderKind.Sqlite, "secret").Description, "a description never shows the string");
        }

        [TestMethod]
        public async Task ExplorerListAndAdd()
        {
            var client = new ScriptedClient((m, p) => m == "explorer.list"
                ? new JsonObject { ["connections"] = new JsonArray(new JsonObject { ["name"] = "Shop", ["provider"] = "sqlite", ["store"] = "credman", ["display"] = "C:\\db\\shop.db" }) }
                : new JsonObject { ["connection"] = new JsonObject { ["name"] = "Pg", ["provider"] = "postgres", ["store"] = "usersecrets", ["display"] = "localhost:5432/shop (user kubuno)" } });
            var service = new DataToolService(client);

            var list = await service.ListConnectionsAsync(CancellationToken.None);
            var added = await service.AddConnectionAsync("Pg", DataProviderKind.Postgres, "Host=localhost;", CredentialStoreKind.UserSecrets, overwrite: true, CancellationToken.None);

            Assert.AreEqual(DataProviderKind.Sqlite, list.Single().ProviderKind);
            Assert.AreEqual("C:\\db\\shop.db", list.Single().Display);
            Assert.AreEqual("localhost:5432/shop (user kubuno)", added.Display);
            var parameters = client.Requests[1].Parameters!;
            Assert.AreEqual("usersecrets", (string)parameters["store"]!);
            Assert.AreEqual("postgres", (string)parameters["provider"]!);
            Assert.IsTrue((bool)parameters["overwrite"]!);
        }

        [TestMethod]
        public async Task QueryResultsKeepNullsAndMessages()
        {
            var client = new ScriptedClient((m, p) => JsonNode.Parse(
                "{\"resultSets\":[{\"columns\":[{\"name\":\"id\",\"dbType\":\"INTEGER\"},{\"name\":\"a.b [c]\",\"dbType\":\"TEXT\"},{\"name\":\"id\",\"dbType\":\"INTEGER\"}]," +
                "\"rows\":[[\"1\",\"Ada\",null],[2,true,\"3\"]],\"truncated\":true}],\"messages\":[\"3 row(s) affected\"],\"elapsedMs\":5}")!);
            var service = new DataToolService(client);

            var result = await service.ExecuteQueryAsync(DataConnectionTarget.Explorer("Shop"), "select 1", 1000, 30, CancellationToken.None);

            var set = result.ResultSets.Single();
            Assert.IsTrue(set.Truncated);
            CollectionAssert.AreEqual(new[] { "1", "Ada", null }, set.Rows[0]);
            CollectionAssert.AreEqual(new[] { "2", "true", "3" }, set.Rows[1]);
            Assert.AreEqual(5, result.ElapsedMs);
            DataText.ForceFrench = true;
            Assert.AreEqual("3 ligne(s) affectée(s)", QueryText.LocalizeMessage(result.Messages.Single()));
            Assert.AreEqual("2 ligne(s) - 5 ms - 1000 premières lignes", QueryText.Status(result, 1000));
            CollectionAssert.AreEqual(new[] { "id", "a.b [c]", "id (2)" }, QueryText.ColumnHeaders(set).ToArray(), "names with dots and brackets are kept as text; duplicates made unique");
            var parameters = client.Requests[0].Parameters!;
            Assert.AreEqual(1000, (int)parameters["maxRows"]!);
            Assert.AreEqual(30, (int)parameters["timeoutSeconds"]!);
        }

        [TestMethod]
        public async Task ScriptAndDataTopRequests()
        {
            var client = new ScriptedClient((m, p) => m == "script.generate"
                ? new JsonObject { ["sql"] = "INSERT INTO \"customers\" (\"name\") VALUES (@name);" }
                : new JsonObject { ["columns"] = new JsonArray(new JsonObject { ["name"] = "id" }), ["rows"] = new JsonArray(new JsonArray("1")), ["truncated"] = false });
            var service = new DataToolService(client);

            string sql = await service.GenerateScriptAsync(DataConnectionTarget.Explorer("Shop"), "main", "customers", ScriptKind.Insert, CancellationToken.None);
            var top = await service.DataTopAsync(DataConnectionTarget.Explorer("Shop"), "main", "customers", 200, CancellationToken.None);

            Assert.AreEqual("INSERT INTO \"customers\" (\"name\") VALUES (@name);", sql);
            Assert.AreEqual("insert", (string)client.Requests[0].Parameters!["kind"]!);
            Assert.AreEqual(200, (int)client.Requests[1].Parameters!["limit"]!);
            Assert.AreEqual("1", top.Rows.Single()[0]);
        }

        [TestMethod]
        public void QueryTextHelpers()
        {
            Assert.AreEqual("SELECT * FROM \"customers\" LIMIT 200;", QueryText.TopSelect(DataProviderKind.Sqlite, "main", "customers", 200));
            Assert.AreEqual("SELECT * FROM \"shop\".\"odd\"\"name\" LIMIT 5;", QueryText.TopSelect(DataProviderKind.Postgres, "shop", "odd\"name", 5));
            Assert.AreEqual("SELECT * FROM `shop`.`a``b` LIMIT 5;", QueryText.TopSelect(DataProviderKind.MySql, "shop", "a`b", 5));
            Assert.AreEqual("SELECT TOP (5) * FROM [dbo].[a]]b];", QueryText.TopSelect(DataProviderKind.SqlServer, "dbo", "a]b", 5));
            Assert.AreEqual("select 2", QueryText.ToExecute("select 1;\nselect 2", "select 2"));
            Assert.AreEqual("select 1", QueryText.ToExecute("select 1", "  \n"));
            Assert.AreEqual("select 1", QueryText.ToExecute("select 1", null));
            DataText.ForceFrench = false;
            Assert.AreEqual("12 row(s) affected", QueryText.LocalizeMessage("12 row(s) affected"));
            Assert.AreEqual("other", QueryText.LocalizeMessage("other"));
            var top = new ResultSetInfo(new[] { new ResultColumnInfo("id", null) }, Enumerable.Range(0, 200).Select(i => new string?[] { i.ToString() }).ToList(), truncated: false);
            Assert.AreEqual("200 row(s) - 7 ms - first 200 rows", QueryText.Status(top, 7, 200));
        }
    }
}
