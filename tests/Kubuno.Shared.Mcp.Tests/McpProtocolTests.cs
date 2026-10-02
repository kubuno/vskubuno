using System;
using System.IO.Pipelines;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Core.Mcp.Bridge.Contracts;
using Kubuno.Core.Mcp.Connectivity;
using Kubuno.Core.Mcp.Tests.Fakes;
using Kubuno.Core.Mcp.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Kubuno.Core.Mcp.Tests
{
    /// <summary>
    /// End-to-end MCP protocol tests: a real <c>ModelContextProtocol</c> server - the exact same
    /// <c>AddMcpServer()...WithTools&lt;KubunoVsTools&gt;()</c> wiring as src/Core/Kubuno.Core.Mcp/Program.cs -
    /// talking to a real <c>ModelContextProtocol</c> client over a pair of in-memory duplex
    /// streams, with <see cref="KubunoVsTools"/> wired to a <see cref="FakeBridgeConnector"/>
    /// instead of a real Visual Studio pipe (per the task: "protocol initialize/tools/list/
    /// tools/call round-trip against the server with a fake bridge"). This exercises the MCP wire
    /// protocol exactly as Claude Code would see it; the pipe transport to Visual Studio itself is
    /// covered separately by PipeEndToEndTests/PipeMessageFramingTests.
    /// </summary>
    [TestClass]
    public sealed class McpProtocolTests
    {
        private static readonly string[] ExpectedToolNames =
        {
            "vs_active_document",
            "vs_selection",
            "vs_error_list",
            "vs_open_documents",
            "vs_solution_or_folder",
            "vs_debugger_state",
            "vs_output_pane",
        };

        private IHost? _host;
        private McpClient? _client;

        [TestCleanup]
        public async Task CleanupAsync()
        {
            if (_client is IAsyncDisposable asyncDisposableClient)
            {
                await asyncDisposableClient.DisposeAsync();
            }
            else if (_client is IDisposable disposableClient)
            {
                disposableClient.Dispose();
            }

            if (_host is not null)
            {
                await _host.StopAsync();
                _host.Dispose();
            }
        }

        [TestMethod]
        public async Task Initialize_ThenListTools_ReturnsAllSevenReadOnlyTools()
        {
            McpClient client = await StartServerAndConnectAsync(new FakeBridgeConnector());

            var tools = await client.ListToolsAsync(cancellationToken: CancellationToken.None);

            CollectionAssert.AreEquivalent(ExpectedToolNames, tools.Select(t => t.Name).ToList());
        }

        [TestMethod]
        public async Task CallTool_ActiveDocument_ReturnsFakeBridgeDataAsText()
        {
            McpClient client = await StartServerAndConnectAsync(new FakeBridgeConnector());

            CallToolResult result = await client.CallToolAsync(
                "vs_active_document",
                arguments: null,
                cancellationToken: CancellationToken.None);

            Assert.IsFalse(result.IsError ?? false);
            string text = AssertSingleTextBlock(result);
            // The tool returns the bridge's DTO serialized as (escaped) JSON text, not the raw
            // path string - deserialize it back rather than substring-matching escaped backslashes.
            ActiveDocumentInfo? info = JsonSerializer.Deserialize<ActiveDocumentInfo>(text);
            Assert.AreEqual(FakeVsContextProvider.DocumentPath, info!.Path);
            Assert.AreEqual("Rust", info.Language);
        }

        [TestMethod]
        public async Task CallTool_ActiveDocument_ForwardsLineRangeArguments()
        {
            McpClient client = await StartServerAndConnectAsync(new FakeBridgeConnector());

            CallToolResult result = await client.CallToolAsync(
                "vs_active_document",
                new System.Collections.Generic.Dictionary<string, object?> { ["startLine"] = 2, ["endLine"] = 3 }!,
                cancellationToken: CancellationToken.None);

            string text = AssertSingleTextBlock(result);
            StringAssert.Contains(text, "lines 2-3");
        }

        [TestMethod]
        public async Task CallTool_ErrorList_ReturnsFakeBridgeItems()
        {
            McpClient client = await StartServerAndConnectAsync(new FakeBridgeConnector());

            CallToolResult result = await client.CallToolAsync(
                "vs_error_list",
                arguments: null,
                cancellationToken: CancellationToken.None);

            string text = AssertSingleTextBlock(result);
            StringAssert.Contains(text, "mismatched types");
            StringAssert.Contains(text, "unused import");
        }

        [TestMethod]
        public async Task CallTool_WhenBridgeUnavailable_ReturnsGracefulError_NotAProtocolCrash()
        {
            var connector = new FakeBridgeConnector
            {
                Unavailable = new Kubuno.Core.Mcp.Bridge.BridgeUnavailableException(
                    "Visual Studio is not running, or no running instance has the Kubuno bridge loaded."),
            };
            McpClient client = await StartServerAndConnectAsync(connector);

            CallToolResult result = await client.CallToolAsync(
                "vs_selection",
                arguments: null,
                cancellationToken: CancellationToken.None);

            Assert.IsTrue(result.IsError ?? false);
            string text = AssertSingleTextBlock(result);
            StringAssert.Contains(text, "Visual Studio is not running");
        }

        private async Task<McpClient> StartServerAndConnectAsync(FakeBridgeConnector connector)
        {
            // Two one-directional pipes make one duplex channel: the client writes into
            // clientToServer and the server reads it; the server writes into serverToClient and
            // the client reads it. Mirrors exactly what a real OS pipe/stdio pair provides, just
            // in-memory, so both McpServer and McpClient run their real wire protocol unmodified.
            var clientToServer = new Pipe();
            var serverToClient = new Pipe();

            HostApplicationBuilder builder = Host.CreateApplicationBuilder();
            builder.Services.AddSingleton<IBridgeConnector>(connector);
            builder.Services
                .AddMcpServer(options =>
                {
                    options.ServerInfo = new Implementation { Name = "kubuno-vs-mcp-tests", Version = "0.0.0" };
                })
                .WithStreamServerTransport(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream())
                .WithTools<KubunoVsTools>();

            _host = builder.Build();
            await _host.StartAsync();

            var transport = new StreamClientTransport(
                serverInput: clientToServer.Writer.AsStream(),
                serverOutput: serverToClient.Reader.AsStream());

            _client = await McpClient.CreateAsync(transport, cancellationToken: CancellationToken.None);
            return _client;
        }

        private static string AssertSingleTextBlock(CallToolResult result)
        {
            Assert.AreEqual(1, result.Content.Count, "Expected exactly one content block.");
            Assert.IsInstanceOfType(result.Content[0], typeof(TextContentBlock));
            return ((TextContentBlock)result.Content[0]).Text;
        }
    }
}
