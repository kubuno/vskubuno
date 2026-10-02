using System;
using System.Text.Json.Serialization;

namespace Kubuno.Shared.Mcp.Bridge.Discovery
{
    /// <summary>
    /// One <c>%LOCALAPPDATA%\Kubuno\vs-mcp\&lt;pid&gt;.json</c> discovery file, written by
    /// <see cref="PipeProtocol.VsMcpBridgeHost"/> when a Visual Studio instance's bridge starts
    /// listening, and read by Kubuno.Shared.Mcp to find a pipe to connect to. See docs/MCP.md "Discovery".
    /// </summary>
    public sealed class BridgeDiscoveryInfo
    {
        /// <summary>The devenv.exe process id; also the file's name (<c>&lt;pid&gt;.json</c>).</summary>
        [JsonPropertyName("pid")]
        public int Pid { get; set; }

        /// <summary>Local named pipe name (no leading <c>\\.\pipe\</c>), unique per VS instance and per bridge start.</summary>
        [JsonPropertyName("pipeName")]
        public string PipeName { get; set; } = string.Empty;

        /// <summary>Always "devenv" today; kept explicit so a stale/foreign file with a reused pid is easy to reject.</summary>
        [JsonPropertyName("processName")]
        public string ProcessName { get; set; } = string.Empty;

        [JsonPropertyName("startedAtUtc")]
        public DateTimeOffset StartedAtUtc { get; set; }

        [JsonPropertyName("visualStudioVersion")]
        public string? VisualStudioVersion { get; set; }

        /// <summary>Best-effort label shown when several instances are running and the user must pick one via <c>KUBUNO_VS_PID</c>.</summary>
        [JsonPropertyName("solutionOrFolderPath")]
        public string? SolutionOrFolderPath { get; set; }
    }
}
