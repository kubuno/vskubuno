using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Desktop.Logic.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Desktop.Tests.Data
{
    [TestClass]
    public sealed class DataToolClientTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        private static async Task<T> WithTimeout<T>(Task<T> task)
        {
            var winner = await Task.WhenAny(task, Task.Delay(Timeout));
            Assert.AreSame(task, winner, "timed out");
            return await task;
        }

        [TestMethod]
        public async Task StartsLazilyAndFramesOneJsonLinePerRequest()
        {
            var launcher = new FakeLauncher();
            launcher.Handler = (c, r) => c.Respond(FakeConnection.IdOf(r), new JsonObject { ["version"] = "0.1.0", ["providers"] = new JsonArray("sqlite") });
            using var client = new DataToolClient(launcher);
            Assert.AreEqual(0, launcher.Started.Count, "no process before the first request");

            var service = new DataToolService(client);
            var ping = await WithTimeout(service.PingAsync(CancellationToken.None));

            Assert.AreEqual("0.1.0", ping.Version);
            CollectionAssert.AreEqual(new[] { "sqlite" }, ping.Providers);
            Assert.AreEqual(1, launcher.Started.Count);
            Assert.IsTrue(launcher.Last.Written.TryPeek(out var line));
            Assert.IsFalse(line.Contains("\n"), "one line per request");
            var request = JsonNode.Parse(line)!.AsObject();
            Assert.AreEqual("ping", (string)request["method"]!);
            Assert.IsTrue(request["id"] is JsonValue);
            Assert.IsTrue(request["params"] is JsonObject, "params always present");
        }

        [TestMethod]
        public async Task ConcurrentRequestsGetTheirOwnAnswersInAnyOrder()
        {
            var launcher = new FakeLauncher();
            var received = new ConcurrentQueue<JsonObject>();
            launcher.Handler = (c, r) => received.Enqueue(r);
            using var client = new DataToolClient(launcher);

            var tasks = Enumerable.Range(0, 20).Select(i => client.SendAsync("echo", new JsonObject { ["n"] = i }, CancellationToken.None)).ToList();
            await WithTimeout(Task.Run(async () =>
            {
                while (received.Count < 20)
                {
                    await Task.Delay(5);
                }

                return true;
            }));

            var requests = received.ToList();
            CollectionAssert.AllItemsAreUnique(requests.Select(FakeConnection.IdOf).ToList(), "ids are unique");
            // Answer in reverse order.
            foreach (var request in Enumerable.Reverse(requests))
            {
                launcher.Last.Respond(FakeConnection.IdOf(request), new JsonObject { ["n"] = request["params"]!["n"]!.DeepClone() });
            }

            for (int i = 0; i < tasks.Count; i++)
            {
                var result = await WithTimeout(tasks[i]);
                Assert.AreEqual(i, result.GetProperty("n").GetInt32());
            }
        }

        [TestMethod]
        public async Task AnErrorAnswerBecomesADataToolException()
        {
            var launcher = new FakeLauncher();
            launcher.Handler = (c, r) => c.RespondError(FakeConnection.IdOf(r), "Database", "no such table: nope");
            using var client = new DataToolClient(launcher);

            var error = await Assert.ThrowsExactlyAsync<DataToolException>(() => client.SendAsync("query.execute", new JsonObject { ["sql"] = "select * from nope" }, CancellationToken.None));

            Assert.AreEqual(DataToolErrorKinds.Database, error.Kind);
            Assert.AreEqual("no such table: nope", error.Message);
        }

        [TestMethod]
        public async Task CancellingSendsCancelWithTheRequestId()
        {
            var launcher = new FakeLauncher();
            var cancelSeen = new TaskCompletionSource<JsonObject>();
            launcher.Handler = (c, r) =>
            {
                if (FakeConnection.MethodOf(r) == "cancel")
                {
                    cancelSeen.TrySetResult(r);
                }
            };
            using var client = new DataToolClient(launcher);
            using var cts = new CancellationTokenSource();

            var running = client.SendAsync("query.execute", new JsonObject { ["sql"] = "select 1" }, cts.Token);
            await Task.Delay(50);
            long id = FakeConnection.IdOf(JsonNode.Parse(launcher.Last.Written.First())!.AsObject());
            cts.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(() => running);
            var cancel = await WithTimeout(cancelSeen.Task);
            Assert.AreEqual(id, (long)cancel["params"]!["id"]!);
            Assert.AreNotEqual(id, FakeConnection.IdOf(cancel), "the cancel request has its own id");

            // The late "Cancelled" answer of the original request is ignored, and the client keeps working.
            launcher.Last.RespondError(id, "Cancelled", "cancelled");
            launcher.Handler = (c, r) => c.Respond(FakeConnection.IdOf(r), new JsonObject());
            await WithTimeout(client.SendAsync("ping", null, CancellationToken.None));
            Assert.AreEqual(1, client.StartCount);
        }

        [TestMethod]
        public async Task ACancelledAnswerIsACancellation()
        {
            var launcher = new FakeLauncher();
            launcher.Handler = (c, r) => c.RespondError(FakeConnection.IdOf(r), "Cancelled", "cancelled");
            using var client = new DataToolClient(launcher);

            await Assert.ThrowsAsync<OperationCanceledException>(() => client.SendAsync("query.execute", null, CancellationToken.None));
        }

        [TestMethod]
        public async Task ACrashFailsThePendingRequestsAndTheNextRequestRestarts()
        {
            var launcher = new FakeLauncher();
            var logs = new ConcurrentQueue<string>();
            using var client = new DataToolClient(launcher, logs.Enqueue);

            var pending = client.SendAsync("schema.load", new JsonObject(), CancellationToken.None);
            await Task.Delay(50);
            launcher.Last.Crash();

            var error = await Assert.ThrowsExactlyAsync<DataToolException>(() => WithTimeout(pending));
            Assert.AreEqual(DataToolErrorKinds.Closed, error.Kind);

            launcher.Handler = (c, r) => c.Respond(FakeConnection.IdOf(r), new JsonObject { ["ok"] = true });
            var result = await WithTimeout(client.SendAsync("ping", null, CancellationToken.None));
            Assert.IsTrue(result.GetProperty("ok").GetBoolean());
            Assert.AreEqual(2, client.StartCount);
            Assert.AreEqual(2, launcher.Started.Count);
            Assert.IsTrue(logs.Any(l => l.Contains("exited")), string.Join("\n", logs));
        }

        [TestMethod]
        public async Task AWriteToADeadHelperIsRetriedOnAFreshOne()
        {
            var launcher = new FakeLauncher();
            launcher.Handler = (c, r) => c.Respond(FakeConnection.IdOf(r), new JsonObject());
            using var client = new DataToolClient(launcher);
            await WithTimeout(client.SendAsync("ping", null, CancellationToken.None));

            // The process died but its EOF has not been read yet: the write fails.
            launcher.Last.Crash();
            await WithTimeout(client.SendAsync("ping", null, CancellationToken.None));

            Assert.AreEqual(2, launcher.Started.Count);
        }

        [TestMethod]
        public async Task AMissingHelperIsReportedAsUnavailable()
        {
            var launcher = new FakeLauncher { FailToStart = true };
            using var client = new DataToolClient(launcher);

            var error = await Assert.ThrowsExactlyAsync<DataToolException>(() => client.SendAsync("ping", null, CancellationToken.None));
            Assert.AreEqual(DataToolErrorKinds.Unavailable, error.Kind);
        }

        [TestMethod]
        public async Task DisposeShutsTheHelperDown()
        {
            var launcher = new FakeLauncher();
            launcher.Handler = (c, r) => c.Respond(FakeConnection.IdOf(r), new JsonObject());
            var client = new DataToolClient(launcher);
            await WithTimeout(client.SendAsync("ping", null, CancellationToken.None));

            client.Dispose();

            Assert.IsTrue(launcher.Last.ShutDown);
            Assert.IsFalse(client.IsRunning);
            await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => client.SendAsync("ping", null, CancellationToken.None));
        }

        [TestMethod]
        public async Task LoggedRequestsNeverContainSecrets()
        {
            const string Secret = "p@ss;w'rd\"S3cr3t";
            var launcher = new FakeLauncher();
            launcher.Handler = (c, r) => c.Respond(FakeConnection.IdOf(r), new JsonObject { ["connection"] = new JsonObject { ["name"] = "Shop" } });
            var logs = new ConcurrentQueue<string>();
            using var client = new DataToolClient(launcher, logs.Enqueue) { TraceRequests = () => true };
            var service = new DataToolService(client);
            string connectionString = "Host=db;Username=app;Password=" + DataConnectionStringBuilder.QuoteValue(Secret) + ";";

            await WithTimeout(service.AddConnectionAsync("Shop", DataProviderKind.Postgres, connectionString, CredentialStoreKind.CredentialManager, overwrite: false, CancellationToken.None));
            launcher.Handler = (c, r) => c.Respond(FakeConnection.IdOf(r), new JsonObject { ["serverVersion"] = "16", ["elapsedMs"] = 3 });
            await WithTimeout(service.TestConnectionAsync(DataConnectionTarget.Inline(DataProviderKind.Postgres, connectionString), CancellationToken.None));
            launcher.Handler = (c, r) => c.Respond(FakeConnection.IdOf(r), new JsonObject { ["resultSets"] = new JsonArray(), ["messages"] = new JsonArray(), ["elapsedMs"] = 1 });
            await WithTimeout(service.ExecuteQueryAsync(DataConnectionTarget.Explorer("Shop"), "UPDATE users SET password = '" + Secret + "'", 1000, 30, CancellationToken.None));

            string all = string.Join("\n", logs);
            Assert.IsTrue(all.Contains("explorer.add") && all.Contains("connection.test") && all.Contains("query.execute"), all);
            Assert.IsFalse(all.Contains("S3cr3t"), all);
            Assert.IsFalse(all.Contains("Password="), all);
            Assert.IsTrue(all.Contains("\"name\":\"Shop\""), "harmless fields are kept: " + all);
            Assert.IsTrue(all.Contains(DataToolRedaction.Mask), all);

            // What was sent is the real string: redaction only affects the log.
            Assert.IsTrue(launcher.Last.Written.Any(l => JsonNode.Parse(l)!["params"]!["target"]?["connectionString"]?.GetValue<string>() == connectionString));
        }

        [TestMethod]
        public void RedactionHandlesNestedTargetsAndLeavesTheOriginalUntouched()
        {
            var parameters = new JsonObject
            {
                ["target"] = new JsonObject { ["provider"] = "sqlite", ["connectionString"] = "sqlite:C:\\secret.db" },
                ["sql"] = "select 'x'",
                ["schema"] = "main",
            };

            string logged = DataToolRedaction.ForLog("schema.load", parameters);

            Assert.IsFalse(logged.Contains("secret.db"), logged);
            Assert.IsTrue(logged.Contains("\"schema\":\"main\""), logged);
            Assert.IsTrue(logged.Contains("<10 chars>"), logged);
            Assert.AreEqual("sqlite:C:\\secret.db", (string)parameters["target"]!["connectionString"]!);
        }

        [TestMethod]
        public async Task TracingIsOffByDefault()
        {
            var launcher = new FakeLauncher();
            launcher.Handler = (c, r) => c.Respond(FakeConnection.IdOf(r), new JsonObject());
            var logs = new List<string>();
            using var client = new DataToolClient(launcher, logs.Add);

            await WithTimeout(client.SendAsync("ping", null, CancellationToken.None));

            Assert.AreEqual(0, logs.Count, string.Join("\n", logs));
        }

        [TestMethod]
        public async Task NonJsonAndUnknownIdLinesAreIgnored()
        {
            var launcher = new FakeLauncher();
            launcher.Handler = (c, r) =>
            {
                c.Push("not json");
                c.Push("{\"id\": 99999, \"result\": {}}");
                c.Push("{\"noid\": true}");
                c.Respond(FakeConnection.IdOf(r), new JsonObject { ["v"] = 1 });
            };
            using var client = new DataToolClient(launcher, _ => { });

            var result = await WithTimeout(client.SendAsync("ping", null, CancellationToken.None));

            Assert.AreEqual(1, result.GetProperty("v").GetInt32());
        }
    }
}
