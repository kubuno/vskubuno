using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Kubuno.Core.DevAssistant.Host;
using Kubuno.Core.DevAssistant.Host.Providers;
using Kubuno.Core.DevAssistant.Logic.Changes;
using Kubuno.Core.DevAssistant.Logic.Cost;
using Kubuno.Core.DevAssistant.Logic.Protocol;
using Kubuno.Core.DevAssistant.Logic.Secrets;
using Kubuno.Core.DevAssistant.Logic.Tools;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Core.DevAssistant.Tests
{
    /// <summary>The host end to end over its real JSON-RPC channel, with the fake provider replaying the shipped recordings.</summary>
    [TestClass]
    public sealed class HostFlowTests
    {
        internal const string SampleView =
            "<!--\n  Sample view.\n-->\n" +
            "<Panel DesignWidth=\"640\" DesignHeight=\"400\" Title=\"{Res app_title}\">\n" +
            "  <Panel x:Name=\"banner\" X=\"40\" Y=\"24\" Width=\"560\" Height=\"96\"/>\n" +
            "  <PictureBox x:Name=\"logo\" X=\"40\" Y=\"140\" Width=\"64\" Height=\"64\"/>\n" +
            "  <Label x:Name=\"welcome\" Text=\"{Res welcome_text}\" X=\"120\" Y=\"140\" Width=\"480\" Height=\"24\"/>\n" +
            "  <Label x:Name=\"hint\" Text=\"{Res hint_text}\" X=\"120\" Y=\"172\" Width=\"480\" Height=\"40\"/>\n" +
            "  <Button x:Name=\"switch_culture\" Text=\"{Res switch_text}\" X=\"40\" Y=\"256\" Width=\"160\" Height=\"36\"/>\n" +
            "  <Button x:Name=\"ok\" Text=\"{Res ok_text}\" X=\"480\" Y=\"256\" Width=\"120\" Height=\"36\"/>\n" +
            "  <Label x:Name=\"status\" Text=\"\" X=\"40\" Y=\"316\" Width=\"560\" Height=\"20\"/>\n" +
            "</Panel>\n";

        private static string FixturesDirectory => Path.Combine(AppContext.BaseDirectory, "Fixtures");

        [TestMethod]
        public async Task Vue_command_streams_calls_tools_and_ends_with_a_valid_two_hunk_kbview_change()
        {
            var viewPath = @"C:\kubuno-build\assist-live\resources-desktop\src\main_view.kbview";
            var buffers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [viewPath] = SampleView };
            var changes = new ChangeSet();
            var masker = new SecretMasker("salt");
            var toolCalls = new List<string>();

            async Task<object?> Tools(RpcMessage request, CancellationToken token)
            {
                var invoke = request.ParamsAs<ToolInvokeParams>();
                toolCalls.Add(invoke.Name);
                await Task.Yield();
                switch (invoke.Name)
                {
                    case "vs_active_document":
                        return new ToolInvokeResult { Content = JsonSerializer.Serialize(new { hasActiveDocument = true, path = viewPath, text = SampleView }, RpcCodec.Options) };
                    case "kbview_registry":
                        return new ToolInvokeResult { Content = "Label: properties Text, X, Y, Width, Height\nSwitch: properties Checked, X, Y, Width, Height" };
                    case "kbview_apply_ops":
                        var result = KbviewOpsForTests.Apply(buffers[invoke.Input.GetProperty("path").GetString()!], invoke.Input.GetProperty("ops"), masker);
                        if (result.Error is not null)
                        {
                            return new ToolInvokeResult { Content = result.Error, IsError = true };
                        }

                        changes.Propose(viewPath, buffers[viewPath], result.Text!);
                        return new ToolInvokeResult { Content = "OK: proposed" };
                    default:
                        return new ToolInvokeResult { Content = "unknown", IsError = true };
                }
            }

            var deltas = new ConcurrentQueue<string>();
            var bodies = new ConcurrentQueue<string>();
            await using var harness = await Harness.StartAsync(Tools, notification =>
            {
                if (notification.Method == DevAssistantMethods.StreamDelta)
                {
                    deltas.Enqueue(notification.ParamsAs<StreamDeltaParams>().Text);
                }
                else if (notification.Method == DevAssistantMethods.RequestSent)
                {
                    bodies.Enqueue(notification.ParamsAs<RequestSentParams>().Body);
                }
            });

            var send = Request("/vue ajoute une ligne « Notifications » avec un interrupteur", ToolsDeclared("vs_active_document", "kbview_registry", "kbview_apply_ops"));
            var result = (await harness.Client.InvokeAsync(DevAssistantMethods.SessionSend, send, CancellationToken.None)).ResultAs<SessionSendResult>();

            Assert.AreEqual(StopReasons.EndTurn, result.StopReason, result.Error);
            CollectionAssert.AreEqual(new[] { "vs_active_document", "kbview_registry", "kbview_apply_ops" }, toolCalls);
            Assert.AreEqual(5, result.NewTurns.Count, "assistant, tool results, assistant, tool results, assistant");
            Assert.IsTrue(deltas.Count > 10, "the answer is streamed in pieces");
            Assert.AreEqual(3, bodies.Count, "one captured request per model call");
            Assert.IsTrue(result.CostUsd > 0);

            // The /vue result is a valid view with two separate hunks; accepting one of them applies only that one.
            var file = changes.Files.Single();
            Assert.AreEqual(2, file.Hunks.Count);
            KbviewOpsForTests.AssertWellFormed(file.ProposedText);
            file.Accepted.Remove(1);
            var accepted = LineDiff.ApplySelected(file.OriginalText, file.Hunks, file.Accepted);
            StringAssert.Contains(accepted, "notifications_label");
            Assert.IsFalse(accepted.Contains("<Switch"));
            KbviewOpsForTests.AssertWellFormed(accepted);

            // The history is append-only and replayable: every tool use is answered.
            var uses = result.NewTurns.SelectMany(t => t.Blocks).Where(b => b.Type == ChatBlockTypes.ToolUse).Select(b => b.ToolUseId).ToList();
            var results = result.NewTurns.SelectMany(t => t.Blocks).Where(b => b.Type == ChatBlockTypes.ToolResult).Select(b => b.ToolUseId).ToList();
            CollectionAssert.AreEquivalent(uses, results);
        }

        [TestMethod]
        public async Task The_cost_cap_stops_the_turn_before_the_next_model_call()
        {
            await using var harness = await Harness.StartAsync((_, _) => Task.FromResult<object?>(new ToolInvokeResult { Content = "{}" }), _ => { });
            var send = Request("/vue ajoute", ToolsDeclared("vs_active_document", "kbview_registry", "kbview_apply_ops"));

            // The first recorded call costs 900 in + 120 out + 3200 cached on "kubuno-fake" (Opus prices): ~0.0066 $.
            send.CostCapUsd = 0.005m;
            var result = (await harness.Client.InvokeAsync(DevAssistantMethods.SessionSend, send, CancellationToken.None)).ResultAs<SessionSendResult>();
            Assert.AreEqual(StopReasons.CostCap, result.StopReason);
            Assert.AreEqual(2, result.NewTurns.Count, "the first answer, then error results for its tool uses (no second call)");
            Assert.IsTrue(result.NewTurns[1].Blocks.All(b => b.Type == ChatBlockTypes.ToolResult && b.IsError));

            // A session already over its cap never calls the model.
            send.CostSoFarUsd = 10m;
            send.CostCapUsd = 5m;
            var none = (await harness.Client.InvokeAsync(DevAssistantMethods.SessionSend, send, CancellationToken.None)).ResultAs<SessionSendResult>();
            Assert.AreEqual(StopReasons.CostCap, none.StopReason);
            Assert.AreEqual(0, none.NewTurns.Count);
        }

        [TestMethod]
        public void Cost_ledger_prices_cache_reads_and_reports_the_hit_rate()
        {
            var usage = new UsageInfo { InputTokens = 1_000_000, OutputTokens = 1_000_000, CacheReadInputTokens = 1_000_000 };
            Assert.AreEqual(4m + 20m + 0.20m, PriceTable.Cost("claude-opus-5-5", usage));
            Assert.AreEqual(2m + 10m + 0.20m, PriceTable.Cost("claude-sonnet-5-5", usage));
            Assert.IsTrue(PriceTable.Cost("some-unknown-model", usage) > PriceTable.Cost("claude-opus-5-5", usage), "unknown models are priced high");
            var ledger = new CostLedger(1m, 0.99m);
            Assert.IsFalse(ledger.IsExhausted);
            ledger.Record("claude-opus-5-5", new UsageInfo { OutputTokens = 1000 });
            Assert.IsTrue(ledger.IsExhausted);
            Assert.AreEqual(0.5, CostLedger.CacheHitRate(new UsageInfo { InputTokens = 50, CacheReadInputTokens = 50 }), 1e-9);
        }

        [TestMethod]
        public async Task Stop_cancels_the_stream_and_answers_pending_tool_uses()
        {
            var toolStarted = new TaskCompletionSource<bool>();
            await using var harness = await Harness.StartAsync(async (request, token) =>
            {
                toolStarted.TrySetResult(true);
                await Task.Delay(Timeout.Infinite, token);
                return null;
            }, _ => { });

            var send = Request("/vue ajoute", ToolsDeclared("vs_active_document", "kbview_registry", "kbview_apply_ops"));
            var pending = harness.Client.InvokeAsync(DevAssistantMethods.SessionSend, send, CancellationToken.None);
            await toolStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await harness.Client.InvokeAsync(DevAssistantMethods.SessionCancel, new SessionCancelParams { SessionId = send.SessionId }, CancellationToken.None);
            var result = (await pending.WaitAsync(TimeSpan.FromSeconds(10))).ResultAs<SessionSendResult>();
            Assert.AreEqual(StopReasons.Cancelled, result.StopReason);
            Assert.AreEqual(ChatRoles.User, result.NewTurns.Last().Role, "the interrupted tool uses get error results");
            Assert.IsTrue(result.NewTurns.Last().Blocks.All(b => b.IsError));
        }

        [TestMethod]
        public async Task Tools_outside_the_policy_are_never_exposed_nor_run()
        {
            var ran = new List<string>();
            await using var harness = await Harness.StartAsync((request, _) =>
            {
                ran.Add(request.ParamsAs<ToolInvokeParams>().Name);
                return Task.FromResult<object?>(new ToolInvokeResult { Content = "{}" });
            }, _ => { });

            // vs_active_document is declared only as an execute-class tool here: the policy drops it, so the recorded
            // call is answered "Unknown tool" by the host itself and the VSIX is never asked to run it.
            var send = Request("/vue ajoute", new List<ToolDescriptor>
            {
                new ToolDescriptor { Name = "vs_active_document", Description = "x", InputSchema = RpcCodec.ParseElement("{\"type\":\"object\"}"), ApprovalClass = ApprovalClass.Execute },
                new ToolDescriptor { Name = "kbview_registry", Description = "x", InputSchema = RpcCodec.ParseElement("{\"type\":\"object\"}") },
            });
            send.MaxRounds = 1;
            var result = (await harness.Client.InvokeAsync(DevAssistantMethods.SessionSend, send, CancellationToken.None)).ResultAs<SessionSendResult>();
            CollectionAssert.AreEqual(new[] { "kbview_registry" }, ran);
            StringAssert.Contains(result.Error, "execute-class");
            var unknown = result.NewTurns[1].Blocks.Single(b => b.ToolUseId == "toolu_fake_vue_1");
            Assert.IsTrue(unknown.IsError);
            StringAssert.Contains(unknown.Text, "Unknown tool");
        }

        [TestMethod]
        public void Policy_makes_outbound_execution_and_database_tools_inexpressible()
        {
            foreach (var name in new[] { "vs_active_document", "vs_selection", "vs_error_list", "vs_open_documents", "fs_read", "fs_list", "fs_grep", "kbview_registry", "kbview_selected_element", "kbview_element", "kbview_validate" })
            {
                Assert.IsNull(ToolPolicy.WhyRejected(new ToolDescriptor { Name = name }), name);
            }

            Assert.IsNull(ToolPolicy.WhyRejected(new ToolDescriptor { Name = "edit_propose", ApprovalClass = ApprovalClass.Write }));
            Assert.IsNull(ToolPolicy.WhyRejected(new ToolDescriptor { Name = "kbview_apply_ops", ApprovalClass = ApprovalClass.Write }));
            foreach (var name in new[] { "git_push", "vs_git_push", "fs_publish", "kubuno_release", "edit_tag", "fs_shell", "kbview_run", "fs_exec_command", "kubuno_db_query", "fs_sql", "kubuno_npm_publish", "edit_commit", "fs_delete", "kubuno_web_fetch", "shell", "bash" })
            {
                Assert.IsNotNull(ToolPolicy.WhyRejected(new ToolDescriptor { Name = name }), name);
            }

            Assert.IsNotNull(ToolPolicy.WhyRejected(new ToolDescriptor { Name = "fs_read", ApprovalClass = ApprovalClass.Execute }), "no execute class before DA-3");
            Assert.IsNotNull(ToolPolicy.WhyRejected(new ToolDescriptor { Name = "fs_write", ApprovalClass = ApprovalClass.Write }), "writes only propose");
            Assert.IsNotNull(ToolPolicy.WhyRejected(new ToolDescriptor { Name = "fs_read", ApprovalClass = ApprovalClass.Forbidden }));
        }

        [TestMethod]
        public void Tool_inputs_are_validated_against_their_schema()
        {
            var schema = RpcCodec.ParseElement(@"{""type"":""object"",""properties"":{""path"":{""type"":""string""},""ops"":{""type"":""array"",""items"":{""type"":""object"",""properties"":{""kind"":{""type"":""string"",""enum"":[""insertChild""]},""index"":{""type"":""integer""}},""required"":[""kind""],""additionalProperties"":false}}},""required"":[""path"",""ops""],""additionalProperties"":false}");
            Assert.AreEqual(0, ToolInputValidator.Validate(schema, RpcCodec.ParseElement(@"{""path"":""a"",""ops"":[{""kind"":""insertChild"",""index"":2}]}")).Count);
            var errors = ToolInputValidator.Validate(schema, RpcCodec.ParseElement(@"{""ops"":[{""kind"":""drop"",""index"":""2"",""x"":1}],""extra"":true}"));
            Assert.AreEqual(5, errors.Count, string.Join("\n", errors));
        }

        internal static SessionSendParams Request(string text, List<ToolDescriptor> tools) => new SessionSendParams
        {
            SessionId = Guid.NewGuid().ToString("N"),
            Provider = ProviderIds.Fake,
            Model = "kubuno-fake",
            Effort = "high",
            System = { new SystemPart { Text = "rules", CacheBreakpoint = true } },
            Tools = tools,
            History = { new ChatTurn { Role = ChatRoles.User, Blocks = { ChatBlock.FromText(text) } } },
            CostCapUsd = 5m,
        };

        internal static List<ToolDescriptor> ToolsDeclared(params string[] names) => names.Select(name => new ToolDescriptor
        {
            Name = name,
            Description = name,
            InputSchema = RpcCodec.ParseElement("{\"type\":\"object\"}"),
            ApprovalClass = name.EndsWith("_apply_ops", StringComparison.Ordinal) ? ApprovalClass.Write : ApprovalClass.Read,
        }).ToList();

        /// <summary>A host over in-memory pipes, as the VSIX sees it.</summary>
        internal sealed class Harness : IAsyncDisposable
        {
            private readonly HostServer _server;

            private Harness(HostServer server, RpcConnection client)
            {
                _server = server;
                Client = client;
            }

            public RpcConnection Client { get; }

            public static async Task<Harness> StartAsync(Func<RpcMessage, CancellationToken, Task<object?>> tools, Action<RpcMessage> notifications, Func<string, string, IModelProvider>? providers = null)
            {
                var (host, vsix) = ProtocolTests.Pair();
                var server = new HostServer(host.Reader, host.Writer, providers);
                server.Start();
                var client = new RpcConnection(vsix.Reader, vsix.Writer, tools, notifications);
                client.Start();
                var init = (await client.InvokeAsync(DevAssistantMethods.Initialize, new InitializeParams { FixturesDirectory = FixturesDirectory }, CancellationToken.None)).ResultAs<InitializeResult>();
                Assert.AreEqual(DevAssistantMethods.ProtocolVersion, init.ProtocolVersion);
                return new Harness(server, client);
            }

            public ValueTask DisposeAsync()
            {
                Client.Dispose();
                _server.Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    /// <summary>
    /// A minimal stand-in for kubuno/applyEdit's insertChild (the real server is exercised by
    /// <see cref="LanguageServerScratchTests"/>): enough to check the recorded /vue ops produce a well-formed view.
    /// </summary>
    internal static class KbviewOpsForTests
    {
        public static (string? Text, string? Error) Apply(string text, JsonElement ops, SecretMasker masker)
        {
            foreach (var op in ops.EnumerateArray())
            {
                if (op.GetProperty("kind").GetString() != "insertChild" || op.GetProperty("parentId").GetString() != string.Empty)
                {
                    return (null, "only root insertChild in this stand-in");
                }

                var xml = masker.Restore(op.GetProperty("xml").GetString()).Text;
                int index = op.GetProperty("index").GetInt32();
                var lines = text.Split('\n').ToList();
                var childLines = Enumerable.Range(0, lines.Count).Where(i => lines[i].StartsWith("  <", StringComparison.Ordinal)).ToList();
                int insertAt = index < childLines.Count ? childLines[index] : lines.FindLastIndex(l => l.StartsWith("</Panel>", StringComparison.Ordinal));
                lines.Insert(insertAt, "  " + xml);
                text = string.Join("\n", lines);
            }

            return (text, null);
        }

        public static void AssertWellFormed(string text)
        {
            using var reader = new XmlTextReader(new StringReader(text)) { Namespaces = false };
            while (reader.Read())
            {
            }
        }
    }
}
