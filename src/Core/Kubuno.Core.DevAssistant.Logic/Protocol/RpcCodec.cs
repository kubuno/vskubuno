using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kubuno.Core.DevAssistant.Logic.Protocol
{
    /// <summary>JSON (de)serialization of the channel: camelCase, nulls omitted, one compact object per line.</summary>
    public static class RpcCodec
    {
        /// <summary>The options every DTO of the channel is written and read with.</summary>
        public static JsonSerializerOptions Options { get; } = CreateOptions(indented: false);

        /// <summary>The same options, indented (« Voir la requête », logs).</summary>
        public static JsonSerializerOptions IndentedOptions { get; } = CreateOptions(indented: true);

        /// <summary>One line of the channel: the message as compact JSON, without the line terminator.</summary>
        public static string Serialize(RpcMessage message) => JsonSerializer.Serialize(message, Options);

        /// <summary>Parses one line of the channel; throws <see cref="JsonException"/> when it is not a JSON-RPC object.</summary>
        public static RpcMessage Deserialize(string line)
        {
            var message = JsonSerializer.Deserialize<RpcMessage>(line, Options);
            if (message is null || (message.Method is null && message.Id is null))
            {
                throw new JsonException("Not a JSON-RPC message.");
            }

            return message;
        }

        public static JsonElement? ToElement(object? value) =>
            value is null ? null : value is JsonElement element ? element : JsonSerializer.SerializeToElement(value, value.GetType(), Options);

        public static T? FromElement<T>(JsonElement element) => JsonSerializer.Deserialize<T>(element.GetRawText(), Options);

        /// <summary>Parses a JSON text into a detached element (a tool input or schema).</summary>
        public static JsonElement ParseElement(string json)
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }

        private static JsonSerializerOptions CreateOptions(bool indented) => new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = indented,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        };
    }
}
