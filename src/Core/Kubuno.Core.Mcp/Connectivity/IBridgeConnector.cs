using System.Threading;
using System.Threading.Tasks;
using Kubuno.Core.Mcp.Bridge.Contracts;

namespace Kubuno.Core.Mcp.Connectivity
{
    /// <summary>
    /// What <see cref="Tools.KubunoVsTools"/> depends on to reach a Visual Studio bridge -
    /// abstracted so tests can substitute a fake bridge instead of a real named pipe (see
    /// tests/Kubuno.Core.Mcp.Tests/Fakes/FakeBridgeConnector.cs and docs/MCP.md "Testing").
    /// </summary>
    public interface IBridgeConnector
    {
        /// <summary>
        /// Sends one request to whichever Visual Studio instance's bridge this connector resolves
        /// to. Implementations should throw <see cref="Kubuno.Core.Mcp.Bridge.BridgeUnavailableException"/>
        /// when no live bridge can be reached, so callers can turn that into a graceful MCP tool
        /// error instead of an opaque failure.
        /// </summary>
        Task<BridgeResponse> SendAsync(string method, object? parameters, CancellationToken cancellationToken);
    }
}
