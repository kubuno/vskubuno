using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Kubuno.Desktop.Logic.Data
{
    /// <summary>The database providers of kubuno-desktop-data (the tool's <c>provider</c> strings).</summary>
    public enum DataProviderKind
    {
        Postgres,
        Sqlite,
        MySql,
        SqlServer,
    }

    /// <summary>Where the Data Explorer keeps a connection string (the tool's <c>store</c> strings).</summary>
    public enum CredentialStoreKind
    {
        /// <summary>Windows Credential Manager (<c>credman</c>).</summary>
        CredentialManager,

        /// <summary>The user secrets file under %APPDATA%\Kubuno\UserSecrets (<c>usersecrets</c>).</summary>
        UserSecrets,
    }

    /// <summary>The kinds of <c>script.generate</c>.</summary>
    public enum ScriptKind
    {
        Select,
        Insert,
        Update,
        Delete,
        Create,
    }

    /// <summary>Protocol names of the enums above.</summary>
    public static class DataToolNames
    {
        public static string Of(DataProviderKind provider) => provider switch
        {
            DataProviderKind.Postgres => "postgres",
            DataProviderKind.Sqlite => "sqlite",
            DataProviderKind.MySql => "mysql",
            DataProviderKind.SqlServer => "sqlserver",
            _ => throw new ArgumentOutOfRangeException(nameof(provider)),
        };

        public static string Of(CredentialStoreKind store) => store == CredentialStoreKind.UserSecrets ? "usersecrets" : "credman";

        public static string Of(ScriptKind kind) => kind.ToString().ToLowerInvariant();

        public static DataProviderKind? ParseProvider(string? name) => (name ?? string.Empty).ToLowerInvariant() switch
        {
            "postgres" or "postgresql" or "pg" => DataProviderKind.Postgres,
            "sqlite" => DataProviderKind.Sqlite,
            "mysql" or "mariadb" => DataProviderKind.MySql,
            "sqlserver" or "mssql" => DataProviderKind.SqlServer,
            _ => null,
        };

        public static CredentialStoreKind ParseStore(string? name) =>
            string.Equals(name, "usersecrets", StringComparison.OrdinalIgnoreCase) ? CredentialStoreKind.UserSecrets : CredentialStoreKind.CredentialManager;
    }

    /// <summary>
    /// A <c>params.target</c>: a Data Explorer connection by name, a project's <c>ConnectionStrings:&lt;name&gt;</c>, or an
    /// inline connection string (only the Add Connection dialog's "Test connection" uses that one).
    /// </summary>
    public sealed class DataConnectionTarget
    {
        private readonly Func<JsonObject> _toJson;

        private DataConnectionTarget(string description, Func<JsonObject> toJson)
        {
            Description = description;
            _toJson = toJson;
        }

        /// <summary>A text safe to show or log (never the inline connection string).</summary>
        public string Description { get; }

        public static DataConnectionTarget Explorer(string name) =>
            new DataConnectionTarget(name, () => new JsonObject { ["explorer"] = name });

        public static DataConnectionTarget Project(string manifestDirectory, string connection, DataProviderKind? provider = null) =>
            new DataConnectionTarget(connection, () =>
            {
                var project = new JsonObject { ["manifestDir"] = manifestDirectory, ["connection"] = connection };
                if (provider is { } p)
                {
                    project["provider"] = DataToolNames.Of(p);
                }

                return new JsonObject { ["project"] = project };
            });

        /// <summary>An inline target. The string is held by this object until it is dropped: create it right before the request.</summary>
        public static DataConnectionTarget Inline(DataProviderKind provider, string connectionString) =>
            new DataConnectionTarget(DataToolNames.Of(provider), () => new JsonObject { ["provider"] = DataToolNames.Of(provider), ["connectionString"] = connectionString });

        public JsonObject ToJson() => _toJson();

        public override string ToString() => Description;
    }

    public sealed class DataToolPingResult
    {
        public string Version { get; set; } = string.Empty;

        public List<string> Providers { get; set; } = new List<string>();
    }

    public sealed class ConnectionTestResult
    {
        public string ServerVersion { get; set; } = string.Empty;

        public long ElapsedMs { get; set; }
    }

    /// <summary>One Data Explorer connection as <c>explorer.list</c> describes it (no secret: <see cref="Display"/> is redacted).</summary>
    public sealed class ExplorerConnectionInfo
    {
        public string Name { get; set; } = string.Empty;

        public string Provider { get; set; } = string.Empty;

        public string Store { get; set; } = string.Empty;

        public string Display { get; set; } = string.Empty;

        [JsonIgnore]
        public DataProviderKind? ProviderKind => DataToolNames.ParseProvider(Provider);
    }

    public sealed class DatabaseSchemaInfo
    {
        public string Provider { get; set; } = string.Empty;

        public string? ServerVersion { get; set; }

        public string? Database { get; set; }

        public List<SchemaInfo> Schemas { get; set; } = new List<SchemaInfo>();

        [JsonIgnore]
        public DataProviderKind? ProviderKind => DataToolNames.ParseProvider(Provider);
    }

    public sealed class SchemaInfo
    {
        public string Name { get; set; } = string.Empty;

        public List<TableInfo> Tables { get; set; } = new List<TableInfo>();

        public List<FunctionInfo> Functions { get; set; } = new List<FunctionInfo>();
    }

    public sealed class TableInfo
    {
        public string Name { get; set; } = string.Empty;

        /// <summary><c>table</c> or <c>view</c>.</summary>
        public string Kind { get; set; } = "table";

        public List<ColumnInfo> Columns { get; set; } = new List<ColumnInfo>();

        public List<string> PrimaryKey { get; set; } = new List<string>();

        public List<ForeignKeyInfo> ForeignKeys { get; set; } = new List<ForeignKeyInfo>();

        public List<IndexInfo> Indexes { get; set; } = new List<IndexInfo>();

        [JsonIgnore]
        public bool IsView => string.Equals(Kind, "view", StringComparison.OrdinalIgnoreCase);
    }

    public sealed class ColumnInfo
    {
        public string Name { get; set; } = string.Empty;

        public string DbType { get; set; } = string.Empty;

        public string? RustType { get; set; }

        public bool Nullable { get; set; } = true;

        public long MaxLength { get; set; }

        public bool PrimaryKey { get; set; }

        public bool AutoIncrement { get; set; }

        public bool ReadOnly { get; set; }

        /// <summary>The column's default expression as the tool reports it (any JSON; usually a string or null).</summary>
        public JsonElement? Default { get; set; }

        [JsonIgnore]
        public string? DefaultText => Default is { } value && value.ValueKind != JsonValueKind.Null && value.ValueKind != JsonValueKind.Undefined
            ? (value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText())
            : null;
    }

    public sealed class ForeignKeyInfo
    {
        public string? Name { get; set; }

        public List<string> Columns { get; set; } = new List<string>();

        public string? RefSchema { get; set; }

        public string RefTable { get; set; } = string.Empty;

        public List<string> RefColumns { get; set; } = new List<string>();
    }

    public sealed class IndexInfo
    {
        public string Name { get; set; } = string.Empty;

        public List<string> Columns { get; set; } = new List<string>();

        public bool Unique { get; set; }
    }

    public sealed class FunctionInfo
    {
        public string Name { get; set; } = string.Empty;

        /// <summary><c>function</c> or <c>procedure</c>.</summary>
        public string Kind { get; set; } = "function";

        public string? ReturnType { get; set; }

        public string? Arguments { get; set; }

        [JsonIgnore]
        public bool IsProcedure => string.Equals(Kind, "procedure", StringComparison.OrdinalIgnoreCase);
    }

    public sealed class ResultColumnInfo
    {
        public ResultColumnInfo(string name, string? dbType)
        {
            Name = name;
            DbType = dbType;
        }

        public string Name { get; }

        public string? DbType { get; }
    }

    /// <summary>One result set: display-text values, <see langword="null"/> for SQL NULL.</summary>
    public sealed class ResultSetInfo
    {
        public ResultSetInfo(IReadOnlyList<ResultColumnInfo> columns, IReadOnlyList<string?[]> rows, bool truncated)
        {
            Columns = columns;
            Rows = rows;
            Truncated = truncated;
        }

        public IReadOnlyList<ResultColumnInfo> Columns { get; }

        public IReadOnlyList<string?[]> Rows { get; }

        public bool Truncated { get; }
    }

    public sealed class QueryResultInfo
    {
        public QueryResultInfo(IReadOnlyList<ResultSetInfo> resultSets, IReadOnlyList<string> messages, long elapsedMs)
        {
            ResultSets = resultSets;
            Messages = messages;
            ElapsedMs = elapsedMs;
        }

        public IReadOnlyList<ResultSetInfo> ResultSets { get; }

        public IReadOnlyList<string> Messages { get; }

        public long ElapsedMs { get; }
    }

    /// <summary>Parses the tool's results into the models above (tolerant: unknown fields are ignored, missing ones defaulted).</summary>
    public static class DataToolJson
    {
        public static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
        };

        public static T Parse<T>(JsonElement element)
            where T : class, new()
        {
            try
            {
                return element.Deserialize<T>(Options) ?? new T();
            }
            catch (JsonException exception)
            {
                throw new DataToolException(DataToolErrorKinds.Protocol, $"Unexpected answer from kubuno-data-tool ({typeof(T).Name}): {exception.Message}", exception);
            }
        }

        public static ResultSetInfo ParseResultSet(JsonElement element)
        {
            var columns = new List<ResultColumnInfo>();
            if (element.TryGetProperty("columns", out var columnsElement) && columnsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var column in columnsElement.EnumerateArray())
                {
                    string name = column.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()! : string.Empty;
                    string? type = column.TryGetProperty("dbType", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
                    columns.Add(new ResultColumnInfo(name, type));
                }
            }

            var rows = new List<string?[]>();
            if (element.TryGetProperty("rows", out var rowsElement) && rowsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var row in rowsElement.EnumerateArray())
                {
                    var values = new string?[columns.Count];
                    int i = 0;
                    if (row.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var value in row.EnumerateArray())
                        {
                            if (i >= values.Length)
                            {
                                break;
                            }

                            values[i++] = ValueText(value);
                        }
                    }

                    rows.Add(values);
                }
            }

            bool truncated = element.TryGetProperty("truncated", out var tr) && tr.ValueKind == JsonValueKind.True;
            return new ResultSetInfo(columns, rows, truncated);
        }

        public static QueryResultInfo ParseQueryResult(JsonElement element)
        {
            var sets = new List<ResultSetInfo>();
            if (element.TryGetProperty("resultSets", out var setsElement) && setsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var set in setsElement.EnumerateArray())
                {
                    sets.Add(ParseResultSet(set));
                }
            }

            var messages = new List<string>();
            if (element.TryGetProperty("messages", out var messagesElement) && messagesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var message in messagesElement.EnumerateArray())
                {
                    if (message.ValueKind == JsonValueKind.String)
                    {
                        messages.Add(message.GetString()!);
                    }
                }
            }

            long elapsed = element.TryGetProperty("elapsedMs", out var e) && e.TryGetInt64(out var ms) ? ms : 0;
            return new QueryResultInfo(sets, messages, elapsed);
        }

        /// <summary>A cell's display text (the tool sends strings; numbers and booleans are accepted too), null for NULL.</summary>
        public static string? ValueText(JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.String => value.GetString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => value.GetRawText(),
            _ => value.GetRawText(),
        };

        internal static string Invariant(long value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
