using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Kubuno.Desktop.Logic.Data
{
    /// <summary>
    /// The typed API of <c>kubuno-data-tool</c> (docs/DATA.md §9) shared by every data feature of the extension: the Data
    /// Explorer (DATA-5), and later the Data Sources wizard (<c>kbdata.*</c>), the migration commands (<c>migrate.*</c>,
    /// <c>sqlx.*</c>) and SQL IntelliSense (<see cref="LoadSchemaAsync"/>). Each method is one request; errors surface
    /// as <see cref="DataToolException"/>, cancellation as <see cref="OperationCanceledException"/>. Methods without a
    /// typed wrapper yet go through <see cref="SendAsync"/>.
    /// </summary>
    public sealed class DataToolService
    {
        private readonly IDataToolClient _client;

        public DataToolService(IDataToolClient client)
        {
            _client = client;
        }

        /// <summary>Any method, raw (<c>kbdata.build</c>, <c>migrate.add</c>, ...).</summary>
        public Task<JsonElement> SendAsync(string method, JsonObject? parameters, CancellationToken cancellationToken) =>
            _client.SendAsync(method, parameters, cancellationToken);

        public async Task<DataToolPingResult> PingAsync(CancellationToken cancellationToken) =>
            DataToolJson.Parse<DataToolPingResult>(await _client.SendAsync("ping", null, cancellationToken).ConfigureAwait(false));

        public async Task<ConnectionTestResult> TestConnectionAsync(DataConnectionTarget target, CancellationToken cancellationToken) =>
            DataToolJson.Parse<ConnectionTestResult>(await _client.SendAsync("connection.test", new JsonObject { ["target"] = target.ToJson() }, cancellationToken).ConfigureAwait(false));

        public async Task<IReadOnlyList<ExplorerConnectionInfo>> ListConnectionsAsync(CancellationToken cancellationToken)
        {
            var result = await _client.SendAsync("explorer.list", null, cancellationToken).ConfigureAwait(false);
            return DataToolJson.Parse<ConnectionList>(result).Connections;
        }

        /// <summary>
        /// Adds (or with <paramref name="overwrite"/> replaces) a Data Explorer connection: the list entry goes to
        /// %APPDATA%\Kubuno\DataExplorer\connections.json, <paramref name="connectionString"/> only to <paramref name="store"/>.
        /// </summary>
        public async Task<ExplorerConnectionInfo> AddConnectionAsync(string name, DataProviderKind provider, string connectionString, CredentialStoreKind store, bool overwrite, CancellationToken cancellationToken)
        {
            var parameters = new JsonObject
            {
                ["name"] = name,
                ["provider"] = DataToolNames.Of(provider),
                ["connectionString"] = connectionString,
                ["store"] = DataToolNames.Of(store),
                ["overwrite"] = overwrite,
            };
            var result = await _client.SendAsync("explorer.add", parameters, cancellationToken).ConfigureAwait(false);
            return DataToolJson.Parse<ConnectionEnvelope>(result).Connection ?? new ExplorerConnectionInfo { Name = name, Provider = DataToolNames.Of(provider), Store = DataToolNames.Of(store) };
        }

        /// <summary>Removes a Data Explorer connection (its list entry and its secret).</summary>
        public Task RemoveConnectionAsync(string name, CancellationToken cancellationToken) =>
            _client.SendAsync("explorer.remove", new JsonObject { ["name"] = name }, cancellationToken);

        /// <summary>Copies a Data Explorer connection's string into a project's secret store (<paramref name="key"/> e.g. <c>ConnectionStrings:Shop</c>).</summary>
        public Task CopySecretToProjectAsync(string explorerConnection, string userSecretsId, string key, CredentialStoreKind store, CancellationToken cancellationToken) =>
            _client.SendAsync("secrets.copyToProject", new JsonObject
            {
                ["explorer"] = explorerConnection,
                ["userSecretsId"] = userSecretsId,
                ["key"] = key,
                ["store"] = DataToolNames.Of(store),
            }, cancellationToken);

        public async Task<DatabaseSchemaInfo> LoadSchemaAsync(DataConnectionTarget target, bool includeSystem, CancellationToken cancellationToken)
        {
            var parameters = new JsonObject { ["target"] = target.ToJson(), ["includeSystem"] = includeSystem };
            return DataToolJson.Parse<DatabaseSchemaInfo>(await _client.SendAsync("schema.load", parameters, cancellationToken).ConfigureAwait(false));
        }

        /// <summary>The first <paramref name="limit"/> rows of a table or view.</summary>
        public async Task<ResultSetInfo> DataTopAsync(DataConnectionTarget target, string schema, string table, int limit, CancellationToken cancellationToken)
        {
            var parameters = new JsonObject { ["target"] = target.ToJson(), ["schema"] = schema, ["table"] = table, ["limit"] = limit };
            return DataToolJson.ParseResultSet(await _client.SendAsync("data.top", parameters, cancellationToken).ConfigureAwait(false));
        }

        public async Task<QueryResultInfo> ExecuteQueryAsync(DataConnectionTarget target, string sql, int maxRows, int timeoutSeconds, CancellationToken cancellationToken)
        {
            var parameters = new JsonObject { ["target"] = target.ToJson(), ["sql"] = sql, ["maxRows"] = maxRows, ["timeoutSeconds"] = timeoutSeconds };
            return DataToolJson.ParseQueryResult(await _client.SendAsync("query.execute", parameters, cancellationToken).ConfigureAwait(false));
        }

        public async Task<string> GenerateScriptAsync(DataConnectionTarget target, string schema, string table, ScriptKind kind, CancellationToken cancellationToken)
        {
            var parameters = new JsonObject { ["target"] = target.ToJson(), ["schema"] = schema, ["table"] = table, ["kind"] = DataToolNames.Of(kind) };
            var result = await _client.SendAsync("script.generate", parameters, cancellationToken).ConfigureAwait(false);
            return result.ValueKind == JsonValueKind.Object && result.TryGetProperty("sql", out var sql) && sql.ValueKind == JsonValueKind.String ? sql.GetString()! : string.Empty;
        }

        private sealed class ConnectionList
        {
            public List<ExplorerConnectionInfo> Connections { get; set; } = new List<ExplorerConnectionInfo>();
        }

        private sealed class ConnectionEnvelope
        {
            public ExplorerConnectionInfo? Connection { get; set; }
        }
    }
}
