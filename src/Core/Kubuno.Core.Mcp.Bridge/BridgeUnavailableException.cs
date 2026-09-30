using System;

namespace Kubuno.Core.Mcp.Bridge
{
    /// <summary>
    /// Thrown by the pipe client (<see cref="PipeProtocol.VsMcpBridgeClient"/>) and by Kubuno.Core.Mcp's
    /// connector when no live bridge can be reached: Visual Studio is not running, no Kubuno
    /// extension instance registered itself, or the discovered pipe has gone away since discovery.
    /// </summary>
    /// <remarks>
    /// Kept a plain, serialization-free exception type so it can cross the net48/net8.0 split of
    /// Kubuno.Core.Mcp.Bridge and be caught by Kubuno.Core.Mcp's tool methods to produce a graceful
    /// <c>McpException</c> (see src/Core/Kubuno.Core.Mcp/Tools/KubunoVsTools.cs) instead of an opaque crash.
    /// </remarks>
    public sealed class BridgeUnavailableException : Exception
    {
        public BridgeUnavailableException(string message) : base(message)
        {
        }

        public BridgeUnavailableException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
