using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Kubuno.Rust.Cargo.Toml;
using Kubuno.Desktop.Logic.Data;

namespace Kubuno.Desktop.Logic.Migrations
{
    /// <summary>A <c>.kbdata</c> data source of a crate, as far as migrations care: its connection, provider and schema.</summary>
    public sealed class CrateDataSource
    {
        public CrateDataSource(string connection, string? provider, string? schema, string filePath)
        {
            Connection = connection;
            Provider = provider;
            Schema = schema;
            FilePath = filePath;
        }

        public string Connection { get; }

        public string? Provider { get; }

        /// <summary>The PostgreSQL schema / MySQL database of the data source (the Kubuno module's schema), or null.</summary>
        public string? Schema { get; }

        public string FilePath { get; }

        public DataProviderKind? ProviderKind => DataToolNames.ParseProvider(Provider);
    }

    /// <summary>How the migration commands pick the connection of a crate.</summary>
    public enum ConnectionChoiceKind
    {
        /// <summary>One connection: <see cref="ConnectionChoice.Connection"/>.</summary>
        Single,

        /// <summary>Several distinct connections in the crate's data sources: ask which one.</summary>
        Several,

        /// <summary>No data source names a connection: ask for a name.</summary>
        None,
    }

    public sealed class ConnectionChoice
    {
        public ConnectionChoice(ConnectionChoiceKind kind, string? connection, IReadOnlyList<string> candidates)
        {
            Kind = kind;
            Connection = connection;
            Candidates = candidates;
        }

        public ConnectionChoiceKind Kind { get; }

        public string? Connection { get; }

        public IReadOnlyList<string> Candidates { get; }
    }

    /// <summary>
    /// What the migration tooling (docs/DATA.md DATA-7) needs to know about a crate, read from its files only: the
    /// connections its <c>src\**\*.kbdata</c> data sources name (<c>connection = "Shop"</c>), their provider and schema,
    /// the <c>[package.metadata.kubuno] user-secrets-id</c> of its <c>Cargo.toml</c>, and its <c>migrations</c> folder.
    /// Never reads a secret value.
    /// </summary>
    public sealed class CrateDataInfo
    {
        private CrateDataInfo(string crateDirectory, string? packageName, string? userSecretsId, IReadOnlyList<CrateDataSource> dataSources)
        {
            CrateDirectory = crateDirectory;
            PackageName = packageName;
            UserSecretsId = userSecretsId;
            DataSources = dataSources;
        }

        /// <summary>The directory of the crate's <c>Cargo.toml</c> (the helper's <c>manifestDir</c>).</summary>
        public string CrateDirectory { get; }

        public string? PackageName { get; }

        public string? UserSecretsId { get; }

        /// <summary>The <c>.kbdata</c> files that name a connection, in path order.</summary>
        public IReadOnlyList<CrateDataSource> DataSources { get; }

        public string MigrationsDirectory => Path.Combine(CrateDirectory, "migrations");

        /// <summary>A display name: the package name, else the folder name.</summary>
        public string DisplayName => string.IsNullOrEmpty(PackageName) ? Path.GetFileName(CrateDirectory.TrimEnd('\\', '/')) : PackageName!;

        /// <summary>The distinct connection names (first spelling wins), in path order.</summary>
        public IReadOnlyList<string> ConnectionNames
        {
            get
            {
                var names = new List<string>();
                foreach (var source in DataSources)
                {
                    if (!names.Any(n => string.Equals(n, source.Connection, StringComparison.OrdinalIgnoreCase)))
                    {
                        names.Add(source.Connection);
                    }
                }

                return names;
            }
        }

        public static CrateDataInfo Create(string crateDirectory, string? packageName, string? userSecretsId, IReadOnlyList<CrateDataSource> dataSources) =>
            new CrateDataInfo(crateDirectory, packageName, userSecretsId, dataSources);

        /// <summary>Reads the crate at <paramref name="crateDirectory"/> (missing or broken files are skipped, never thrown).</summary>
        public static CrateDataInfo Read(string crateDirectory)
        {
            string? packageName = null;
            string? userSecretsId = null;
            var manifest = Path.Combine(crateDirectory, "Cargo.toml");
            try
            {
                if (File.Exists(manifest))
                {
                    (packageName, userSecretsId) = ParseManifest(File.ReadAllText(manifest));
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Unreadable manifest: no package name, no user secrets id.
            }

            var sources = new List<CrateDataSource>();
            foreach (var file in KbdataFiles(crateDirectory))
            {
                try
                {
                    var source = ParseKbdata(File.ReadAllText(file), file);
                    if (source != null)
                    {
                        sources.Add(source);
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Skipped.
                }
            }

            return new CrateDataInfo(crateDirectory, packageName, userSecretsId, sources);
        }

        /// <summary>The <c>.kbdata</c> files under <c>src</c>, sorted by path.</summary>
        public static IReadOnlyList<string> KbdataFiles(string crateDirectory)
        {
            var source = Path.Combine(crateDirectory, "src");
            try
            {
                return Directory.Exists(source)
                    ? Directory.EnumerateFiles(source, "*.kbdata", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList()
                    : (IReadOnlyList<string>)Array.Empty<string>();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return Array.Empty<string>();
            }
        }

        /// <summary>The <c>connection</c>, <c>provider</c> and <c>schema</c> of a <c>.kbdata</c> text, or null (no connection, not TOML).</summary>
        public static CrateDataSource? ParseKbdata(string tomlText, string filePath)
        {
            try
            {
                var document = TomlDocument.Parse(tomlText);
                var connection = document.GetValue("connection")?.AsString();
                if (string.IsNullOrWhiteSpace(connection))
                {
                    return null;
                }

                var schema = document.GetValue("schema")?.AsString();
                return new CrateDataSource(connection!.Trim(), document.GetValue("provider")?.AsString(), string.IsNullOrWhiteSpace(schema) ? null : schema!.Trim(), filePath);
            }
            catch (TomlParseException)
            {
                return null;
            }
        }

        /// <summary>The <c>[package] name</c> and <c>[package.metadata.kubuno] user-secrets-id</c> of a <c>Cargo.toml</c> text.</summary>
        public static (string? PackageName, string? UserSecretsId) ParseManifest(string cargoTomlText)
        {
            try
            {
                var document = TomlDocument.Parse(cargoTomlText);
                var name = document.GetValue("package", "name")?.AsString();
                var id = document.GetValue("package", "metadata", "kubuno", "user-secrets-id")?.AsString();
                return (string.IsNullOrWhiteSpace(name) ? null : name, string.IsNullOrWhiteSpace(id) ? null : id!.Trim());
            }
            catch (TomlParseException)
            {
                return (null, null);
            }
        }

        /// <summary>
        /// The connection the migrations use: <paramref name="remembered"/> (the one the developer chose earlier in this
        /// session) when set, else the only connection of the data sources, else "ask" (several or none).
        /// </summary>
        public ConnectionChoice Choose(string? remembered)
        {
            var names = ConnectionNames;
            if (!string.IsNullOrWhiteSpace(remembered))
            {
                return new ConnectionChoice(ConnectionChoiceKind.Single, remembered!.Trim(), names);
            }

            return names.Count switch
            {
                1 => new ConnectionChoice(ConnectionChoiceKind.Single, names[0], names),
                0 => new ConnectionChoice(ConnectionChoiceKind.None, null, names),
                _ => new ConnectionChoice(ConnectionChoiceKind.Several, null, names),
            };
        }

        /// <summary>The data source of <paramref name="connection"/> (first in path order), or null.</summary>
        public CrateDataSource? SourceOf(string connection) =>
            DataSources.FirstOrDefault(s => string.Equals(s.Connection, connection, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// The schema a first migration creates (<c>CREATE SCHEMA IF NOT EXISTS</c>, the Kubuno "one schema per module"
        /// rule): the schema of the connection's data source, never for SQLite (which has no <c>CREATE SCHEMA</c>).
        /// </summary>
        public string? SchemaFor(string? connection)
        {
            if (string.IsNullOrEmpty(connection))
            {
                return null;
            }

            var source = SourceOf(connection!);
            if (source?.Schema is null || source.ProviderKind == DataProviderKind.Sqlite || source.ProviderKind == DataProviderKind.SqlServer)
            {
                return null;
            }

            return source.Schema;
        }

        /// <summary>The provider the connection's data source declares, if any (else the helper infers it from the string).</summary>
        public DataProviderKind? ProviderFor(string connection) => SourceOf(connection)?.ProviderKind;

        /// <summary>The <c>migrate.*</c> target of <paramref name="connection"/>.</summary>
        public DataConnectionTarget TargetFor(string connection) => DataConnectionTarget.Project(CrateDirectory, connection, ProviderFor(connection));

        /// <summary><c>ConnectionStrings:&lt;connection&gt;</c>.</summary>
        public static string SecretKey(string connection) => "ConnectionStrings:" + connection;

        /// <summary>The Data Explorer's rule for connection names (1-64 of <c>[A-Za-z0-9 _.-]</c>), reused for project connections.</summary>
        public static bool IsValidConnectionName(string? name)
        {
            var text = name?.Trim() ?? string.Empty;
            if (text.Length == 0 || text.Length > 64)
            {
                return false;
            }

            foreach (var c in text)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == ' ' || c == '_' || c == '.' || c == '-';
                if (!ok)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary><c>%APPDATA%\Kubuno\UserSecrets\&lt;id&gt;\secrets.json</c>.</summary>
        public static string UserSecretsPath(string appDataDirectory, string userSecretsId) =>
            Path.Combine(appDataDirectory, "Kubuno", "UserSecrets", userSecretsId, "secrets.json");

        /// <summary>
        /// The connection names a user secrets file defines (<c>ConnectionStrings:&lt;name&gt;</c> keys, flat or nested) - only
        /// the key names are read, never kept or returned values. Empty when the file is missing or not JSON.
        /// </summary>
        public static IReadOnlyList<string> UserSecretsConnectionNames(string secretsFilePath)
        {
            var names = new List<string>();
            string text;
            try
            {
                if (!File.Exists(secretsFilePath))
                {
                    return names;
                }

                text = File.ReadAllText(secretsFilePath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return names;
            }

            return ConnectionNamesFromSecretsJson(text);
        }

        /// <summary>See <see cref="UserSecretsConnectionNames"/>: the names of a secrets JSON text.</summary>
        public static IReadOnlyList<string> ConnectionNamesFromSecretsJson(string json)
        {
            var names = new List<string>();
            try
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return names;
                }

                foreach (var property in document.RootElement.EnumerateObject())
                {
                    const string Prefix = "ConnectionStrings:";
                    if (property.Name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        Add(names, property.Name.Substring(Prefix.Length));
                    }
                    else if (string.Equals(property.Name, "ConnectionStrings", StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var nested in property.Value.EnumerateObject())
                        {
                            Add(names, nested.Name);
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // Not JSON: no names.
            }

            return names;
        }

        private static void Add(List<string> names, string name)
        {
            if (name.Length > 0 && !names.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
            {
                names.Add(name);
            }
        }
    }
}
