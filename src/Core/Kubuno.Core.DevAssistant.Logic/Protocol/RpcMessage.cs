using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kubuno.Core.DevAssistant.Logic.Protocol
{
    /// <summary>
    /// One JSON-RPC 2.0 message on the VSIX ⇄ <c>kubuno-dev-assistant.exe</c> channel (docs/AI-ASSISTANT.md section 9.1):
    /// a request (<see cref="Id"/> and <see cref="Method"/>), a notification (<see cref="Method"/> only) or a response
    /// (<see cref="Id"/> with <see cref="Result"/> or <see cref="Error"/>). Framed as one compact JSON object per line on
    /// the child's stdio (<see cref="RpcCodec"/>), like <c>kubuno-data-tool</c>.
    /// </summary>
    public sealed class RpcMessage
    {
        [JsonPropertyName("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";

        [JsonPropertyName("id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public long? Id { get; set; }

        [JsonPropertyName("method")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Method { get; set; }

        [JsonPropertyName("params")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public JsonElement? Params { get; set; }

        [JsonPropertyName("result")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public JsonElement? Result { get; set; }

        [JsonPropertyName("error")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public RpcError? Error { get; set; }

        [JsonIgnore]
        public bool IsRequest => Method is not null && Id is not null;

        [JsonIgnore]
        public bool IsNotification => Method is not null && Id is null;

        [JsonIgnore]
        public bool IsResponse => Method is null && Id is not null;

        public static RpcMessage Request(long id, string method, object? parameters) =>
            new RpcMessage { Id = id, Method = method, Params = RpcCodec.ToElement(parameters) };

        public static RpcMessage Notification(string method, object? parameters) =>
            new RpcMessage { Method = method, Params = RpcCodec.ToElement(parameters) };

        public static RpcMessage Response(long id, object? result) =>
            new RpcMessage { Id = id, Result = RpcCodec.ToElement(result) ?? RpcCodec.ToElement(new object()) };

        public static RpcMessage Failure(long id, int code, string message) =>
            new RpcMessage { Id = id, Error = new RpcError { Code = code, Message = message } };

        /// <summary>The params deserialized as <typeparamref name="T"/> (a new instance when absent).</summary>
        public T ParamsAs<T>()
            where T : new() => Params is { } p ? RpcCodec.FromElement<T>(p) ?? new T() : new T();

        /// <summary>The result deserialized as <typeparamref name="T"/>; throws <see cref="RpcException"/> for an error response.</summary>
        public T ResultAs<T>()
            where T : new()
        {
            if (Error is { } error)
            {
                throw new RpcException(error.Code, error.Message);
            }

            return Result is { } r ? RpcCodec.FromElement<T>(r) ?? new T() : new T();
        }
    }

    /// <summary>A JSON-RPC error object.</summary>
    public sealed class RpcError
    {
        [JsonPropertyName("code")]
        public int Code { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>JSON-RPC error codes used on this channel.</summary>
    public static class RpcErrorCodes
    {
        public const int ParseError = -32700;
        public const int MethodNotFound = -32601;
        public const int InvalidParams = -32602;
        public const int InternalError = -32603;
        public const int Cancelled = -32800;
    }

    /// <summary>An error response turned into an exception on the caller's side.</summary>
    public sealed class RpcException : Exception
    {
        public RpcException(int code, string message)
            : base(message)
        {
            Code = code;
        }

        public int Code { get; }
    }
}
