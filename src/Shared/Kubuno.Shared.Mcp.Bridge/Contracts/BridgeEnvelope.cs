using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kubuno.Shared.Mcp.Bridge.Contracts
{
    /// <summary>
    /// One request sent over the named pipe from the MCP server process (Kubuno.Shared.Mcp) to the
    /// bridge running inside devenv.exe (Kubuno.Shared.Mcp.Bridge's <see cref="PipeProtocol.VsMcpBridgeHost"/>).
    /// </summary>
    /// <remarks>
    /// This is a small, purpose-built envelope - not the MCP JSON-RPC wire format itself. The MCP
    /// protocol is spoken only between Claude Code and the Kubuno.Shared.Mcp process (via the
    /// <c>ModelContextProtocol</c> package); the pipe between Kubuno.Shared.Mcp and the VS-side bridge is
    /// a private, simpler request/response contract, one message per line-framed JSON document
    /// (see <see cref="PipeProtocol.PipeMessageFraming"/>).
    /// </remarks>
    public sealed class BridgeRequest
    {
        /// <summary>Correlates a <see cref="BridgeResponse"/> back to this request on a shared connection.</summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>The tool method name, e.g. <c>"vs_active_document"</c>. See <see cref="PipeProtocol.BridgeDispatcher"/>.</summary>
        [JsonPropertyName("method")]
        public string Method { get; set; } = string.Empty;

        /// <summary>Optional method-specific parameters, deserialized by the dispatcher.</summary>
        [JsonPropertyName("params")]
        public JsonElement? Params { get; set; }
    }

    /// <summary>The bridge's reply to a <see cref="BridgeRequest"/>.</summary>
    public sealed class BridgeResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("success")]
        public bool Success { get; set; }

        /// <summary>Present when <see cref="Success"/> is <see langword="true"/>: the tool-specific result DTO.</summary>
        [JsonPropertyName("result")]
        public JsonElement? Result { get; set; }

        /// <summary>Present when <see cref="Success"/> is <see langword="false"/>.</summary>
        [JsonPropertyName("error")]
        public BridgeErrorInfo? Error { get; set; }

        public static BridgeResponse Ok(string id, JsonElement result) =>
            new BridgeResponse { Id = id, Success = true, Result = result };

        public static BridgeResponse Fail(string id, string code, string message) =>
            new BridgeResponse { Id = id, Success = false, Error = new BridgeErrorInfo { Code = code, Message = message } };
    }

    /// <summary>Machine-readable error reported by the bridge (unknown method, VS API failure, etc.).</summary>
    public sealed class BridgeErrorInfo
    {
        [JsonPropertyName("code")]
        public string Code { get; set; } = string.Empty;

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;
    }
}
