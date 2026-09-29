using System;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Kubuno.VisualStudio.Core.IntelliSense
{
    /// <summary>Small helpers over <see cref="JsonNode"/> for LSP messages.</summary>
    public static class LspJson
    {
        /// <summary>True when <paramref name="json"/> is a request for <paramref name="method"/>; <paramref name="id"/> is its id, as raw JSON.</summary>
        public static bool IsRequest(string json, string method, out string? id)
        {
            id = null;
            if (json.IndexOf("\"" + method + "\"", StringComparison.Ordinal) < 0)
            {
                return false;
            }

            if (Parse(json) is not JsonObject message || (string?)message["method"] != method || message["id"] is not JsonNode idNode)
            {
                return false;
            }

            id = idNode.ToJsonString();
            return true;
        }

        /// <summary>True when <paramref name="json"/> is the response to the request with the raw JSON id <paramref name="id"/>.</summary>
        public static bool IsResponse(string json, string id)
        {
            if (json.IndexOf("\"method\"", StringComparison.Ordinal) >= 0 && json.IndexOf("\"result\"", StringComparison.Ordinal) < 0 && json.IndexOf("\"error\"", StringComparison.Ordinal) < 0)
            {
                return false;
            }

            return Parse(json) is JsonObject message && message["method"] is null && message["id"] is JsonNode idNode && idNode.ToJsonString() == id;
        }

        public static JsonNode? Parse(string json)
        {
            try
            {
                return JsonNode.Parse(json);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>The object at <paramref name="path"/> under <paramref name="root"/>, created (with every missing parent) when absent.</summary>
        public static JsonObject Ensure(JsonObject root, params string[] path)
        {
            var current = root;
            foreach (var name in path)
            {
                if (current[name] is not JsonObject next)
                {
                    next = new JsonObject();
                    current[name] = next;
                }

                current = next;
            }

            return current;
        }
    }
}
