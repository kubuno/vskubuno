using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Core.DevAssistant.Logic.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Core.DevAssistant.Tests
{
    [TestClass]
    public sealed class ProtocolTests
    {
        [TestMethod]
        public void Session_send_round_trips_with_provider_native_blocks()
        {
            var parameters = new SessionSendParams
            {
                SessionId = "s1",
                Provider = ProviderIds.Anthropic,
                Model = "claude-opus-5-5",
                Effort = "high",
                System = { new SystemPart { Text = "rules", CacheBreakpoint = true } },
                Tools = { new ToolDescriptor { Name = "fs_read", Description = "read", InputSchema = RpcCodec.ParseElement(@"{""type"":""object"",""properties"":{""path"":{""type"":""string""}}}"), ApprovalClass = ApprovalClass.Read } },
                History =
                {
                    new ChatTurn { Role = ChatRoles.User, Blocks = { ChatBlock.FromText("Bonjour «secret:npm-token#a1b2c3» é") } },
                    new ChatTurn
                    {
                        Role = ChatRoles.Assistant,
                        Blocks =
                        {
                            new ChatBlock { Type = ChatBlockTypes.Thinking, Text = "…", Signature = "sig==" },
                            new ChatBlock { Type = ChatBlockTypes.ToolUse, ToolUseId = "toolu_1", ToolName = "fs_read", Input = RpcCodec.ParseElement(@"{""path"":""a.rs""}") },
                        },
                    },
                    new ChatTurn { Role = ChatRoles.User, Blocks = { ChatBlock.ToolResultFor("toolu_1", "fn main() {}", isError: false) } },
                },
                CostCapUsd = 5m,
                CostSoFarUsd = 0.25m,
            };

            var line = RpcCodec.Serialize(RpcMessage.Request(7, DevAssistantMethods.SessionSend, parameters));
            StringAssert.Contains(line, "\"method\":\"session/send\"");
            StringAssert.Contains(line, "\"approvalClass\":\"read\"");
            Assert.IsFalse(line.Contains('\n'), "One message per line.");

            var message = RpcCodec.Deserialize(line);
            Assert.IsTrue(message.IsRequest);
            var back = message.ParamsAs<SessionSendParams>();
            Assert.AreEqual("claude-opus-5-5", back.Model);
            Assert.AreEqual(5m, back.CostCapUsd);
            Assert.AreEqual(3, back.History.Count);
            Assert.AreEqual("sig==", back.History[1].Blocks[0].Signature);
            Assert.AreEqual("a.rs", back.History[1].Blocks[1].Input!.Value.GetProperty("path").GetString());
            Assert.AreEqual("Bonjour «secret:npm-token#a1b2c3» é", back.History[0].Blocks[0].Text);
            Assert.AreEqual(ApprovalClass.Read, back.Tools[0].ApprovalClass);
            Assert.IsTrue(back.System[0].CacheBreakpoint);
        }

        [TestMethod]
        public void Error_responses_become_exceptions()
        {
            var message = RpcCodec.Deserialize(RpcCodec.Serialize(RpcMessage.Failure(3, RpcErrorCodes.MethodNotFound, "nope")));
            Assert.IsTrue(message.IsResponse);
            var exception = Assert.ThrowsExactly<RpcException>(() => message.ResultAs<InitializeResult>());
            Assert.AreEqual(RpcErrorCodes.MethodNotFound, exception.Code);
        }

        [TestMethod]
        public void Garbage_lines_are_rejected()
        {
            Assert.ThrowsExactly<JsonException>(() => RpcCodec.Deserialize("{\"jsonrpc\":\"2.0\"}"));
        }

        [TestMethod]
        public async Task Duplex_connection_answers_requests_in_both_directions_and_cancels()
        {
            var (a, b) = Pair();
            var notified = new TaskCompletionSource<string>();
            var started = new TaskCompletionSource<bool>();
            using var server = new RpcConnection(a.Reader, a.Writer, async (request, token) =>
            {
                if (request.Method == "slow")
                {
                    started.TrySetResult(true);
                    await Task.Delay(Timeout.Infinite, token);
                }

                return new InitializeResult { ProtocolVersion = 1, HostVersion = request.Method! };
            }, n => notified.TrySetResult(n.Method!));
            using var client = new RpcConnection(b.Reader, b.Writer, (_, _) => Task.FromResult<object?>(null), _ => { });
            server.Start();
            client.Start();

            var answer = (await client.InvokeAsync("hello", new { }, CancellationToken.None)).ResultAs<InitializeResult>();
            Assert.AreEqual("hello", answer.HostVersion);

            await client.NotifyAsync("ping", null);
            Assert.AreEqual("ping", await notified.Task.WaitAsync(TimeSpan.FromSeconds(5)));

            using var cancel = new CancellationTokenSource();
            var slow = client.InvokeAsync("slow", null, cancel.Token);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancel.Cancel();
            await Assert.ThrowsAsync<TaskCanceledException>(() => slow);
        }

        internal static ((TextReader Reader, TextWriter Writer) A, (TextReader Reader, TextWriter Writer) B) Pair()
        {
            var aToB = new AnonymousPipeServerStream(PipeDirection.Out);
            var bFromA = new AnonymousPipeClientStream(PipeDirection.In, aToB.ClientSafePipeHandle);
            var bToA = new AnonymousPipeServerStream(PipeDirection.Out);
            var aFromB = new AnonymousPipeClientStream(PipeDirection.In, bToA.ClientSafePipeHandle);
            var utf8 = new UTF8Encoding(false);
            return (
                (new StreamReader(aFromB, utf8), new StreamWriter(aToB, utf8) { NewLine = "\n" }),
                (new StreamReader(bFromA, utf8), new StreamWriter(bToA, utf8) { NewLine = "\n" }));
        }
    }
}
