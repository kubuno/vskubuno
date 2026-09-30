using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Kubuno.VisualStudio.Core.Data
{
    /// <summary>
    /// An error answered by <c>kubuno-data-tool</c> (<c>{"error": {"kind", "message"}}</c>), or a transport failure of the
    /// client itself (<see cref="DataToolErrorKinds.Closed"/> when the helper exited, <see cref="DataToolErrorKinds.Protocol"/>
    /// for an unreadable answer). The message comes from the tool, which never puts a secret or a connection string in it;
    /// the client never adds request parameters to it.
    /// </summary>
    public sealed class DataToolException : Exception
    {
        public DataToolException(string kind, string message)
            : base(message)
        {
            Kind = kind;
        }

        public DataToolException(string kind, string message, Exception inner)
            : base(message, inner)
        {
            Kind = kind;
        }

        /// <summary>The <c>DataError</c> variant name (<c>Database</c>, <c>Validation</c>, ...), see <see cref="DataToolErrorKinds"/>.</summary>
        public string Kind { get; }
    }

    /// <summary>The <c>kind</c> values of the tool's errors (kubuno-data's <c>DataError</c> variants, plus the protocol ones).</summary>
    public static class DataToolErrorKinds
    {
        public const string Config = "Config";
        public const string Secret = "Secret";
        public const string Validation = "Validation";
        public const string Conversion = "Conversion";
        public const string Database = "Database";
        public const string Concurrency = "Concurrency";
        public const string Cancelled = "Cancelled";
        public const string Closed = "Closed";
        public const string Protocol = "Protocol";
        public const string Io = "Io";

        /// <summary>The helper executable could not be found or started (client side only).</summary>
        public const string Unavailable = "Unavailable";
    }

    /// <summary>One running helper process seen as a line-oriented duplex channel (tests use an in-memory fake).</summary>
    public interface IDataToolConnection : IDisposable
    {
        /// <summary>Writes one request line (without its terminator) and flushes it.</summary>
        Task WriteLineAsync(string line);

        /// <summary>The next response line, or <see langword="null"/> once the helper has exited (EOF).</summary>
        Task<string?> ReadLineAsync();

        /// <summary>Asks the helper to exit (closes its stdin: EOF means exit 0), killing it if it does not.</summary>
        void Shutdown();
    }

    /// <summary>Starts a new helper process (<see cref="ProcessDataToolLauncher"/>); throws <see cref="DataToolException"/> (<see cref="DataToolErrorKinds.Unavailable"/>) when it cannot.</summary>
    public interface IDataToolLauncher
    {
        IDataToolConnection Start();
    }

    /// <summary>
    /// Removes secrets from request parameters before they are logged: every property whose name says it may hold a
    /// secret (<c>connectionString</c>, <c>password</c>, ...) is replaced by <c>***</c> at any depth, and SQL text by its
    /// length (a statement may carry literal data). The methods whose whole parameters are about credentials
    /// (<c>connection.test</c>, <c>explorer.add</c>, <c>secrets.copyToProject</c>) keep only their harmless fields.
    /// </summary>
    public static class DataToolRedaction
    {
        public const string Mask = "***";

        // Readable log lines (the text is only logged, never parsed back).
        private static readonly System.Text.Json.JsonSerializerOptions LogJson = new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        private static readonly HashSet<string> SecretKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "connectionString", "password", "pwd", "secret", "value", "databaseUrl",
        };

        private static readonly HashSet<string> CredentialMethods = new HashSet<string>(StringComparer.Ordinal)
        {
            "connection.test", "explorer.add", "secrets.copyToProject",
        };

        /// <summary>A loggable copy of <paramref name="parameters"/> (never the original, which is not modified).</summary>
        public static string ForLog(string method, JsonObject? parameters)
        {
            if (parameters is null)
            {
                return "{}";
            }

            var copy = (JsonObject)parameters.DeepClone();
            if (CredentialMethods.Contains(method))
            {
                // Keep only the fields that cannot hold a secret; the target is reduced to its kind.
                var kept = new JsonObject();
                foreach (var name in new[] { "name", "provider", "store", "overwrite", "explorer", "key", "userSecretsId" })
                {
                    if (copy[name] is JsonNode value)
                    {
                        kept[name] = value.DeepClone();
                    }
                }

                if (copy["target"] is JsonObject target)
                {
                    kept["target"] = RedactNode(target);
                }

                if (copy.ContainsKey("connectionString"))
                {
                    kept["connectionString"] = Mask;
                }

                return kept.ToJsonString(LogJson);
            }

            return RedactNode(copy).ToJsonString(LogJson);
        }

        private static JsonNode RedactNode(JsonNode node)
        {
            switch (node)
            {
                case JsonObject obj:
                    var result = new JsonObject();
                    foreach (var property in obj)
                    {
                        if (SecretKeys.Contains(property.Key))
                        {
                            result[property.Key] = Mask;
                        }
                        else if (string.Equals(property.Key, "sql", StringComparison.OrdinalIgnoreCase) && property.Value is JsonValue sql && sql.TryGetValue(out string? text))
                        {
                            result[property.Key] = $"<{text?.Length ?? 0} chars>";
                        }
                        else
                        {
                            result[property.Key] = property.Value is null ? null : RedactNode(property.Value);
                        }
                    }

                    return result;
                case JsonArray array:
                    var items = new JsonArray();
                    foreach (var item in array)
                    {
                        items.Add(item is null ? null : RedactNode(item));
                    }

                    return items;
                default:
                    return node.DeepClone();
            }
        }
    }

    /// <summary>A read end that reports EOF for the helper's stdout (used by fakes and the real launcher alike).</summary>
    internal static class DataToolLines
    {
        public static async Task<string?> ReadLineOrNullAsync(TextReader reader)
        {
            try
            {
                return await reader.ReadLineAsync().ConfigureAwait(false);
            }
            catch (IOException)
            {
                return null;
            }
            catch (ObjectDisposedException)
            {
                return null;
            }
        }
    }
}
