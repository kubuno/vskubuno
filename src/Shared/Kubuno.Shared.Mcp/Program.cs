using System.Threading.Tasks;
using Kubuno.Shared.Mcp.Connectivity;
using Kubuno.Shared.Mcp.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace Kubuno.Shared.Mcp
{
    /// <summary>
    /// Entry point for <c>kubuno-vs-mcp</c>: a stdio MCP server that Claude Code connects to (see
    /// docs/MCP.md "Registering the server with Claude Code"). It speaks MCP over stdin/stdout to
    /// Claude Code, and Kubuno.Shared.Mcp.Bridge's private pipe protocol to whichever Visual Studio
    /// instance's bridge <see cref="PipeBridgeConnector"/> discovers.
    /// </summary>
    internal static class Program
    {
        private const string ServerVersion = "0.1.0";

        private static async Task<int> Main(string[] args)
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

            // MCP frames stdout as the JSON-RPC transport: nothing else may ever be written there.
            // All logging goes to stderr, which Claude Code does not parse as protocol traffic.
            builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

            builder.Services.AddSingleton<IBridgeConnector, PipeBridgeConnector>();

            builder.Services
                .AddMcpServer(options =>
                {
                    options.ServerInfo = new Implementation { Name = "kubuno-vs-mcp", Version = ServerVersion };
                    options.ServerInstructions =
                        "Read-only view of what the developer is looking at right now in Visual Studio: active " +
                        "document, selection, Error List, open documents, solution/folder root (with detected " +
                        "Cargo workspaces), debugger state, and an output pane. These tools never modify Visual " +
                        "Studio or the developer's files - make edits with your own file tools as usual, then use " +
                        "these tools to see how the developer's own edits and the IDE's own diagnostics look.";
                })
                .WithStdioServerTransport()
                .WithTools<KubunoVsTools>();

            await builder.Build().RunAsync().ConfigureAwait(false);
            return 0;
        }
    }
}
