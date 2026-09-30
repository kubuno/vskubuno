using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Desktop.Logic.Data;

namespace Kubuno.Desktop.Logic.Migrations
{
    /// <summary>One migration as <c>migrate.status</c> reports it (docs/DATA.md DATA-7).</summary>
    public sealed class MigrationInfo
    {
        /// <summary>The <c>yyyyMMddHHmmss</c> version.</summary>
        public long Version { get; set; }

        /// <summary>sqlx's description (the file name's words, e.g. <c>create customers</c>).</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>Applied successfully.</summary>
        public bool Applied { get; set; }

        /// <summary>ISO 8601 UTC (<c>2026-09-30T12:00:01Z</c>), null when not applied.</summary>
        public string? AppliedAt { get; set; }

        /// <summary>False when the file changed after it was applied (or it is missing).</summary>
        public bool ChecksumMatches { get; set; } = true;

        /// <summary>Has a <c>.down.sql</c>.</summary>
        public bool Reversible { get; set; }

        /// <summary>The up file (null when missing from the directory).</summary>
        public string? File { get; set; }

        /// <summary>A row with <c>success = false</c> (the migration failed part-way).</summary>
        public bool Dirty { get; set; }

        /// <summary>Applied in the database, but no longer in the directory.</summary>
        public bool Missing { get; set; }

        /// <summary><c>20260930120000 create customers</c>.</summary>
        public string Label => Version.ToString(CultureInfo.InvariantCulture) + " " + Description;
    }

    /// <summary>The answer of <c>migrate.status</c>.</summary>
    public sealed class MigrationStatusResult
    {
        public List<MigrationInfo> Migrations { get; set; } = new List<MigrationInfo>();

        public int PendingCount { get; set; }

        /// <summary>The last applied migration (highest version), or null.</summary>
        public MigrationInfo? LastApplied => Migrations.Where(m => m.Applied).OrderBy(m => m.Version).LastOrDefault();
    }

    /// <summary>The answer of <c>sqlx.status</c>.</summary>
    public sealed class SqlxCacheStatus
    {
        public bool Stale { get; set; }

        /// <summary>Why it is stale (English, from the helper), null when up to date.</summary>
        public string? Reason { get; set; }

        public int QueryFiles { get; set; }
    }

    /// <summary>The answer of <c>sqlx.prepare</c>.</summary>
    public sealed class SqlxPrepareResult
    {
        public int QueryFiles { get; set; }

        /// <summary>The tail of cargo's output (the helper removes the database URL from it).</summary>
        public string Output { get; set; } = string.Empty;

        /// <summary><c>cargo-sqlx</c> or <c>cargo-check</c>.</summary>
        public string? Mechanism { get; set; }
    }

    /// <summary>
    /// The typed <c>migrate.*</c> and <c>sqlx.*</c> requests of <c>kubuno-data-tool</c> (docs/DATA.md DATA-7), on top of the
    /// shared <see cref="DataToolService"/>.
    /// </summary>
    public sealed class MigrationService
    {
        private readonly DataToolService _service;

        public MigrationService(DataToolService service)
        {
            _service = service;
        }

        /// <summary><c>migrate.add</c>: creates the files and returns their paths (up first).</summary>
        public async Task<IReadOnlyList<string>> AddAsync(string migrationsDirectory, string description, bool reversible, string? schema, CancellationToken cancellationToken)
        {
            var parameters = new JsonObject
            {
                ["migrationsDir"] = migrationsDirectory,
                ["description"] = description,
                ["reversible"] = reversible,
            };
            if (!string.IsNullOrWhiteSpace(schema))
            {
                parameters["schema"] = schema;
            }

            var result = await _service.SendAsync("migrate.add", parameters, cancellationToken).ConfigureAwait(false);
            var files = new List<string>();
            if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty("files", out var array) && array.ValueKind == JsonValueKind.Array)
            {
                foreach (var file in array.EnumerateArray())
                {
                    if (file.ValueKind == JsonValueKind.String)
                    {
                        files.Add(file.GetString()!);
                    }
                }
            }

            return files;
        }

        public async Task<MigrationStatusResult> StatusAsync(DataConnectionTarget target, string migrationsDirectory, CancellationToken cancellationToken) =>
            DataToolJson.Parse<MigrationStatusResult>(await _service.SendAsync("migrate.status", Request(target, migrationsDirectory), cancellationToken).ConfigureAwait(false));

        /// <summary><c>migrate.run</c>: the versions it applied (empty when nothing was pending).</summary>
        public async Task<IReadOnlyList<long>> RunAsync(DataConnectionTarget target, string migrationsDirectory, CancellationToken cancellationToken)
        {
            var result = await _service.SendAsync("migrate.run", Request(target, migrationsDirectory), cancellationToken).ConfigureAwait(false);
            var applied = new List<long>();
            if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty("applied", out var array) && array.ValueKind == JsonValueKind.Array)
            {
                foreach (var version in array.EnumerateArray())
                {
                    if (version.ValueKind == JsonValueKind.Number && version.TryGetInt64(out var v))
                    {
                        applied.Add(v);
                    }
                }
            }

            return applied;
        }

        /// <summary><c>migrate.revert</c>: the version it reverted, or null when nothing was applied.</summary>
        public async Task<long?> RevertAsync(DataConnectionTarget target, string migrationsDirectory, CancellationToken cancellationToken)
        {
            var result = await _service.SendAsync("migrate.revert", Request(target, migrationsDirectory), cancellationToken).ConfigureAwait(false);
            return result.ValueKind == JsonValueKind.Object && result.TryGetProperty("reverted", out var reverted) && reverted.ValueKind == JsonValueKind.Number && reverted.TryGetInt64(out var v) ? v : (long?)null;
        }

        public async Task<SqlxCacheStatus> SqlxStatusAsync(string manifestDirectory, CancellationToken cancellationToken) =>
            DataToolJson.Parse<SqlxCacheStatus>(await _service.SendAsync("sqlx.status", new JsonObject { ["manifestDir"] = manifestDirectory }, cancellationToken).ConfigureAwait(false));

        /// <summary><c>sqlx.prepare</c> (long: builds the crate; cancel with <paramref name="cancellationToken"/>, which kills cargo).</summary>
        public async Task<SqlxPrepareResult> SqlxPrepareAsync(string manifestDirectory, DataConnectionTarget target, CancellationToken cancellationToken, string? targetDirectory = null)
        {
            var parameters = new JsonObject { ["manifestDir"] = manifestDirectory, ["target"] = target.ToJson() };
            if (!string.IsNullOrWhiteSpace(targetDirectory))
            {
                // The .rsproj's own CargoTargetDir: cargo builds where the project builds.
                parameters["targetDir"] = targetDirectory;
            }

            return DataToolJson.Parse<SqlxPrepareResult>(await _service.SendAsync("sqlx.prepare", parameters, cancellationToken).ConfigureAwait(false));
        }

        /// <summary>The Data Explorer connections (for "Set the connection string...").</summary>
        public Task<IReadOnlyList<ExplorerConnectionInfo>> ListExplorerConnectionsAsync(CancellationToken cancellationToken) =>
            _service.ListConnectionsAsync(cancellationToken);

        /// <summary>Copies a Data Explorer connection's string into the crate's user secrets as <c>ConnectionStrings:&lt;connection&gt;</c>.</summary>
        public Task CopyToUserSecretsAsync(string explorerConnection, string userSecretsId, string connection, CancellationToken cancellationToken) =>
            _service.CopySecretToProjectAsync(explorerConnection, userSecretsId, CrateDataInfo.SecretKey(connection), CredentialStoreKind.UserSecrets, cancellationToken);

        private static JsonObject Request(DataConnectionTarget target, string migrationsDirectory) =>
            new JsonObject { ["target"] = target.ToJson(), ["migrationsDir"] = migrationsDirectory };
    }
}
