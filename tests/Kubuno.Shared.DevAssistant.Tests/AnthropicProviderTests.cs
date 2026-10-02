using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Shared.DevAssistant.Host.Providers;
using Kubuno.Shared.DevAssistant.Logic.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Shared.DevAssistant.Tests
{
    /// <summary>
    /// The official SDK path of <see cref="AnthropicProvider"/>, against a local HTTP server replaying a recorded SSE
    /// stream (TestData\anthropic-tool-use.sse): no network, no real key.
    /// </summary>
    [TestClass]
    public sealed class AnthropicProviderTests
    {
        private const string FakeKey = "test-key-not-a-secret";

        [TestMethod]
        public async Task Streams_text_thinking_and_tool_use_and_captures_the_exact_body()
        {
            var sse = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "anthropic-tool-use.sse"), Encoding.UTF8);
            using var server = new RecordedServer(sse, "text/event-stream");
            var provider = new AnthropicProvider(() => FakeKey, server.BaseUrl);

            var request = new ProviderRequest
            {
                Model = "claude-opus-5-5",
                Effort = "high",
                MaxTokens = 32000,
                System = new[] { new SystemPart { Text = "rules digest", CacheBreakpoint = true }, new SystemPart { Text = "command digest", CacheBreakpoint = true } },
                Tools = new[]
                {
                    new ToolDescriptor { Name = "kbview_registry", Description = "registry", InputSchema = RpcCodec.ParseElement(@"{""type"":""object"",""properties"":{""components"":{""type"":""array"",""items"":{""type"":""string""}}}}") },
                    new ToolDescriptor { Name = "fs_read", Description = "read", InputSchema = RpcCodec.ParseElement(@"{""type"":""object"",""properties"":{""path"":{""type"":""string""}},""required"":[""path""]}") },
                },
                Messages = new[] { new ChatTurn { Role = ChatRoles.User, Blocks = { ChatBlock.FromText("Lis main.rs «secret:npm-token#a1b2c3»") } } },
            };

            var events = new List<ProviderEvent>();
            await foreach (var item in provider.StreamAsync(request, CancellationToken.None))
            {
                events.Add(item);
            }

            var text = string.Concat(events.Where(e => e.Kind == ProviderEventKind.TextDelta).Select(e => e.Text));
            Assert.AreEqual("Je lis le fichier.", text);
            Assert.AreEqual("Lecture nécessaire.", string.Concat(events.Where(e => e.Kind == ProviderEventKind.ThinkingDelta).Select(e => e.Text)));
            var blocks = events.Where(e => e.Kind == ProviderEventKind.BlockCompleted).Select(e => e.Block!).ToList();
            Assert.AreEqual(3, blocks.Count);
            Assert.AreEqual("sig-abc", blocks[0].Signature, "thinking signatures are kept for an exact replay");
            Assert.AreEqual(ChatBlockTypes.ToolUse, blocks[2].Type);
            Assert.AreEqual("toolu_01", blocks[2].ToolUseId);
            Assert.AreEqual("src/main.rs", blocks[2].Input!.Value.GetProperty("path").GetString(), "streamed partial JSON is reassembled");
            var usage = events.Single(e => e.Kind == ProviderEventKind.Usage).Usage!;
            Assert.AreEqual(120, usage.InputTokens);
            Assert.AreEqual(3000, usage.CacheReadInputTokens);
            Assert.AreEqual(42, usage.OutputTokens);
            Assert.AreEqual("tool_use", events.Last().Text);

            // « Voir la requête »: the captured body is exactly what reached the server, and holds no key.
            var body = events.First(e => e.Kind == ProviderEventKind.RequestBody).Text;
            Assert.AreEqual(server.LastBody, body);
            Assert.AreEqual(FakeKey, server.LastApiKey, "the key travels in the x-api-key header");
            Assert.IsFalse(body.Contains(FakeKey));
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            Assert.AreEqual("claude-opus-5-5", root.GetProperty("model").GetString());
            Assert.IsTrue(root.GetProperty("stream").GetBoolean());
            Assert.AreEqual("high", root.GetProperty("output_config").GetProperty("effort").GetString());
            Assert.AreEqual("adaptive", root.GetProperty("thinking").GetProperty("type").GetString());
            Assert.AreEqual("ephemeral", root.GetProperty("cache_control").GetProperty("type").GetString(), "automatic caching of the conversation");
            Assert.AreEqual(2, root.GetProperty("system").EnumerateArray().Count(s => s.TryGetProperty("cache_control", out _)), "one breakpoint per cached system part");
            var tools = root.GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToList();
            CollectionAssert.AreEqual(new[] { "fs_read", "kbview_registry" }, tools, "deterministic, sorted tool list (cache prefix)");
            Assert.IsTrue(root.GetProperty("tools")[0].GetProperty("eager_input_streaming").GetBoolean());
            StringAssert.Contains(root.GetProperty("messages")[0].GetProperty("content")[0].GetProperty("text").GetString(), "«secret:npm-token#a1b2c3»", "only masked text is sent");
        }

        [TestMethod]
        public async Task Lists_models_from_the_models_api_and_falls_back_offline()
        {
            const string models = "{\"data\":[{\"type\":\"model\",\"id\":\"claude-opus-5-5\",\"display_name\":\"Claude Opus 5.5\",\"created_at\":\"2026-08-01T00:00:00Z\",\"max_input_tokens\":1000000,\"max_tokens\":128000},{\"type\":\"model\",\"id\":\"claude-sonnet-5-5\",\"display_name\":\"Claude Sonnet 5.5\",\"created_at\":\"2026-08-01T00:00:00Z\",\"max_input_tokens\":1000000,\"max_tokens\":128000}],\"has_more\":false,\"first_id\":\"claude-opus-5-5\",\"last_id\":\"claude-sonnet-5-5\"}";
            using var server = new RecordedServer(models, "application/json");
            var listed = await new AnthropicProvider(() => FakeKey, server.BaseUrl).ListModelsAsync(CancellationToken.None);
            Assert.IsTrue(listed.FromProvider, listed.Error);
            CollectionAssert.AreEqual(new[] { "claude-opus-5-5", "claude-sonnet-5-5" }, listed.Models.Select(m => m.Id).ToList());
            Assert.AreEqual(1_000_000, listed.Models[0].MaxInputTokens);

            var noKey = await new AnthropicProvider(() => null, server.BaseUrl).ListModelsAsync(CancellationToken.None);
            Assert.IsFalse(noKey.FromProvider);
            StringAssert.StartsWith(noKey.Error, "no-api-key");
            Assert.AreEqual("claude-opus-5-5", noKey.Models[0].Id, "offline fallback list");
        }

        /// <summary>A one-response HTTP/1.1 server on a loopback port (TcpListener: no URL reservation needed).</summary>
        private sealed class RecordedServer : IDisposable
        {
            private readonly TcpListener _listener = new TcpListener(IPAddress.Loopback, 0);
            private readonly CancellationTokenSource _stop = new CancellationTokenSource();

            public RecordedServer(string response, string contentType)
            {
                _listener.Start();
                BaseUrl = "http://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port;
                _ = Task.Run(() => ServeAsync(response, contentType));
            }

            public string BaseUrl { get; }

            public string? LastBody { get; private set; }

            public string? LastApiKey { get; private set; }

            private async Task ServeAsync(string response, string contentType)
            {
                while (!_stop.IsCancellationRequested)
                {
                    TcpClient client;
                    try
                    {
                        client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    }
                    catch (Exception)
                    {
                        return;
                    }

                    using (client)
                    {
                        var stream = client.GetStream();
                        var header = new StringBuilder();
                        var buffer = new byte[1];
                        while (!header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
                        {
                            if (await stream.ReadAsync(buffer, 0, 1) == 0)
                            {
                                break;
                            }

                            header.Append((char)buffer[0]);
                        }

                        var lines = header.ToString().Split("\r\n");
                        int length = 0;
                        foreach (var line in lines)
                        {
                            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                            {
                                length = int.Parse(line.Substring(15).Trim());
                            }
                            else if (line.StartsWith("x-api-key:", StringComparison.OrdinalIgnoreCase))
                            {
                                LastApiKey = line.Substring(10).Trim();
                            }
                        }

                        var body = new byte[length];
                        int read = 0;
                        while (read < length)
                        {
                            read += await stream.ReadAsync(body, read, length - read);
                        }

                        LastBody = Encoding.UTF8.GetString(body);
                        var payload = Encoding.UTF8.GetBytes(response);
                        var head = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: {contentType}; charset=utf-8\r\nContent-Length: {payload.Length}\r\nrequest-id: req_test\r\nConnection: close\r\n\r\n");
                        await stream.WriteAsync(head);
                        await stream.WriteAsync(payload);
                        await stream.FlushAsync();
                    }
                }
            }

            public void Dispose()
            {
                _stop.Cancel();
                _listener.Stop();
            }
        }
    }
}
