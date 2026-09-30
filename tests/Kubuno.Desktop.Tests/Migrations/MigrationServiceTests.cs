using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Desktop.Logic.Data;
using Kubuno.Desktop.Logic.Migrations;
using Kubuno.Desktop.Tests.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Desktop.Tests.Migrations
{
    /// <summary>The <c>migrate.*</c> / <c>sqlx.*</c> requests as the protocol (and the helper's e2e test) shapes them.</summary>
    [TestClass]
    public sealed class MigrationServiceTests
    {
        private static readonly DataConnectionTarget Target = DataConnectionTarget.Project(@"C:\src\shop", "Shop", DataProviderKind.Sqlite);

        [TestMethod]
        public async Task AddSendsTheSchemaOnlyWhenThereIsOne()
        {
            var client = new ScriptedClient((m, p) => JsonNode.Parse("{\"files\":[\"C:\\\\m\\\\1_a.up.sql\",\"C:\\\\m\\\\1_a.down.sql\"]}")!);
            var service = new MigrationService(new DataToolService(client));

            var files = await service.AddAsync(@"C:\src\shop\migrations", "create customers", reversible: true, schema: "shop", CancellationToken.None);
            await service.AddAsync(@"C:\src\shop\migrations", "add vip", reversible: false, schema: null, CancellationToken.None);

            CollectionAssert.AreEqual(new[] { @"C:\m\1_a.up.sql", @"C:\m\1_a.down.sql" }, files.ToList());
            Assert.AreEqual("migrate.add", client.Requests[0].Method);
            Assert.AreEqual("{\"migrationsDir\":\"C:\\\\src\\\\shop\\\\migrations\",\"description\":\"create customers\",\"reversible\":true,\"schema\":\"shop\"}", client.Requests[0].Parameters!.ToJsonString());
            Assert.AreEqual("{\"migrationsDir\":\"C:\\\\src\\\\shop\\\\migrations\",\"description\":\"add vip\",\"reversible\":false}", client.Requests[1].Parameters!.ToJsonString());
        }

        [TestMethod]
        public async Task RunRevertAndStatus()
        {
            var client = new ScriptedClient((m, p) => m switch
            {
                "migrate.run" => JsonNode.Parse("{\"applied\":[20260930044040,20260930044041]}")!,
                "migrate.revert" => JsonNode.Parse("{\"reverted\":null}")!,
                _ => JsonNode.Parse("{\"migrations\":[{\"version\":1,\"description\":\"a\",\"applied\":true,\"appliedAt\":\"2026-09-30T12:00:01Z\",\"checksumMatches\":true,\"reversible\":true,\"file\":null,\"dirty\":false,\"missing\":true}],\"pendingCount\":0}")!,
            });
            var service = new MigrationService(new DataToolService(client));

            var applied = await service.RunAsync(Target, @"C:\src\shop\migrations", CancellationToken.None);
            var reverted = await service.RevertAsync(Target, @"C:\src\shop\migrations", CancellationToken.None);
            var status = await service.StatusAsync(Target, @"C:\src\shop\migrations", CancellationToken.None);

            CollectionAssert.AreEqual(new[] { 20260930044040L, 20260930044041L }, applied.ToList());
            Assert.IsNull(reverted);
            Assert.IsTrue(status.Migrations.Single().Missing);
            Assert.IsNull(status.Migrations.Single().File);
            Assert.AreEqual("{\"target\":{\"project\":{\"manifestDir\":\"C:\\\\src\\\\shop\",\"connection\":\"Shop\",\"provider\":\"sqlite\"}},\"migrationsDir\":\"C:\\\\src\\\\shop\\\\migrations\"}", client.Requests[0].Parameters!.ToJsonString());
            Assert.AreEqual("migrate.revert", client.Requests[1].Method);
        }

        [TestMethod]
        public async Task RevertReturnsTheVersion()
        {
            var client = new ScriptedClient((m, p) => JsonNode.Parse("{\"reverted\":20260930044041}")!);
            Assert.AreEqual(20260930044041L, await new MigrationService(new DataToolService(client)).RevertAsync(Target, @"C:\m", CancellationToken.None));
        }

        [TestMethod]
        public async Task SqlxStatusAndPrepare()
        {
            var client = new ScriptedClient((m, p) => m == "sqlx.status"
                ? JsonNode.Parse("{\"stale\":true,\"reason\":\"no offline query cache (.sqlx) although `shop.kbdata` needs one\",\"queryFiles\":0}")!
                : JsonNode.Parse("{\"queryFiles\":3,\"output\":\"Finished\",\"mechanism\":\"cargo-check\"}")!);
            var service = new MigrationService(new DataToolService(client));

            var status = await service.SqlxStatusAsync(@"C:\src\shop", CancellationToken.None);
            var prepared = await service.SqlxPrepareAsync(@"C:\src\shop", Target, CancellationToken.None);

            Assert.IsTrue(status.Stale);
            Assert.AreEqual(0, status.QueryFiles);
            StringAssert.Contains(status.Reason, "shop.kbdata");
            Assert.AreEqual(3, prepared.QueryFiles);
            Assert.AreEqual("cargo-check", prepared.Mechanism);
            Assert.AreEqual("{\"manifestDir\":\"C:\\\\src\\\\shop\"}", client.Requests[0].Parameters!.ToJsonString());
            Assert.AreEqual("{\"manifestDir\":\"C:\\\\src\\\\shop\",\"target\":{\"project\":{\"manifestDir\":\"C:\\\\src\\\\shop\",\"connection\":\"Shop\",\"provider\":\"sqlite\"}}}", client.Requests[1].Parameters!.ToJsonString());

            // A .rsproj's own CargoTargetDir is passed on (cargo builds where the project builds).
            await service.SqlxPrepareAsync(@"C:\src\shop", Target, CancellationToken.None, @"C:\build\rsproj\shop");
            StringAssert.EndsWith(client.Requests[2].Parameters!.ToJsonString(), ",\"targetDir\":\"C:\\\\build\\\\rsproj\\\\shop\"}");
        }

        [TestMethod]
        public async Task CopyToUserSecretsUsesTheConnectionStringsKey()
        {
            var client = new ScriptedClient((m, p) => new JsonObject());
            await new MigrationService(new DataToolService(client)).CopyToUserSecretsAsync("Shop (explorer)", "mig-live-check", "Shop", CancellationToken.None);
            Assert.AreEqual("secrets.copyToProject", client.Requests[0].Method);
            Assert.AreEqual("{\"explorer\":\"Shop (explorer)\",\"userSecretsId\":\"mig-live-check\",\"key\":\"ConnectionStrings:Shop\",\"store\":\"usersecrets\"}", client.Requests[0].Parameters!.ToJsonString());
        }
    }
}
