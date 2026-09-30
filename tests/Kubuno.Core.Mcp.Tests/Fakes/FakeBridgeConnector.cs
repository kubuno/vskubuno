using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Core.Mcp.Bridge;
using Kubuno.Core.Mcp.Bridge.Contracts;
using Kubuno.Core.Mcp.Bridge.PipeProtocol;
using Kubuno.Core.Mcp.Connectivity;

namespace Kubuno.Core.Mcp.Tests.Fakes
{
    /// <summary>
    /// <see cref="IBridgeConnector"/> that dispatches straight to an in-memory <see cref="BridgeDispatcher"/>
    /// (over a <see cref="FakeVsContextProvider"/> by default), with no pipe involved. This is the
    /// "fake bridge" the MCP protocol round-trip tests (McpProtocolTests) are asked to use: it
    /// exercises Kubuno.Core.Mcp's tool layer and the real MCP wire protocol end-to-end, while the pipe
    /// transport itself is verified separately (see PipeEndToEndTests and PipeMessageFramingTests).
    /// </summary>
    public sealed class FakeBridgeConnector : IBridgeConnector
    {
        private readonly BridgeDispatcher _dispatcher;

        public FakeBridgeConnector(IVsContextProvider? provider = null)
        {
            _dispatcher = new BridgeDispatcher(provider ?? new FakeVsContextProvider());
        }

        /// <summary>When set, every call fails with this exception instead of dispatching - simulates "Visual Studio not running".</summary>
        public BridgeUnavailableException? Unavailable { get; set; }

        public async Task<BridgeResponse> SendAsync(string method, object? parameters, CancellationToken cancellationToken)
        {
            if (Unavailable is not null)
            {
                throw Unavailable;
            }

            var request = new BridgeRequest
            {
                Method = method,
                Params = parameters is null ? (JsonElement?)null : JsonSerializer.SerializeToElement(parameters),
            };

            return await _dispatcher.DispatchAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }
}
