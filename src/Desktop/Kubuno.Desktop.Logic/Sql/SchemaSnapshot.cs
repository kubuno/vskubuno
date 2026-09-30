using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Kubuno.Desktop.Logic.Sql
{
    /// <summary>
    /// The schema of one connection as <c>kubuno-data-tool</c>'s <c>schema.load</c> returns it (the snapshot file
    /// <c>.vs\kubuno\schema\&lt;connection&gt;.json</c> holds exactly that JSON).
    /// </summary>
    public sealed class SchemaSnapshot
    {
        public SchemaSnapshot(string connection, string? provider, string? database, IReadOnlyList<SqlSchemaInfo> schemas)
        {
            Connection = connection;
            Provider = provider;
            Database = database;
            Schemas = schemas;
        }

        public string Connection { get; }

        public string? Provider { get; }

        public string? Database { get; }

        public IReadOnlyList<SqlSchemaInfo> Schemas { get; }

        /// <summary>Parses a <c>schema.load</c> result; throws <see cref="JsonException"/> when it is not one.</summary>
        public static SchemaSnapshot Parse(string json, string connection)
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("A schema snapshot is a JSON object.");
            }

            var schemas = new List<SqlSchemaInfo>();
            foreach (var schema in Array(root, "schemas"))
            {
                var schemaName = String(schema, "name") ?? string.Empty;
                var tables = new List<SqlTableInfo>();
                foreach (var table in Array(schema, "tables"))
                {
                    var name = String(table, "name");
                    if (string.IsNullOrEmpty(name))
                    {
                        continue;
                    }

                    var columns = new List<SqlColumnInfo>();
                    foreach (var column in Array(table, "columns"))
                    {
                        var columnName = String(column, "name");
                        if (string.IsNullOrEmpty(columnName))
                        {
                            continue;
                        }

                        columns.Add(new SqlColumnInfo(
                            columnName!,
                            String(column, "dbType"),
                            String(column, "rustType"),
                            Bool(column, "nullable") ?? true,
                            Bool(column, "primaryKey") ?? false,
                            Bool(column, "autoIncrement") ?? false));
                    }

                    tables.Add(new SqlTableInfo(connection, schemaName, name!, string.Equals(String(table, "kind"), "view", StringComparison.OrdinalIgnoreCase), columns));
                }

                var functions = new List<SqlFunctionInfo>();
                foreach (var function in Array(schema, "functions"))
                {
                    var name = String(function, "name");
                    if (!string.IsNullOrEmpty(name))
                    {
                        functions.Add(new SqlFunctionInfo(connection, schemaName, name!, String(function, "kind") ?? "function", String(function, "returnType"), String(function, "arguments")));
                    }
                }

                schemas.Add(new SqlSchemaInfo(schemaName, tables, functions));
            }

            return new SchemaSnapshot(connection, String(root, "provider"), String(root, "database"), schemas);
        }

        private static IEnumerable<JsonElement> Array(JsonElement element, string name) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
                ? value.EnumerateArray().ToList()
                : Enumerable.Empty<JsonElement>();

        private static string? String(JsonElement element, string name) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        private static bool? Bool(JsonElement element, string name) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
                ? value.GetBoolean()
                : (bool?)null;
    }

    public sealed class SqlSchemaInfo
    {
        public SqlSchemaInfo(string name, IReadOnlyList<SqlTableInfo> tables, IReadOnlyList<SqlFunctionInfo> functions)
        {
            Name = name;
            Tables = tables;
            Functions = functions;
        }

        public string Name { get; }

        public IReadOnlyList<SqlTableInfo> Tables { get; }

        public IReadOnlyList<SqlFunctionInfo> Functions { get; }
    }

    public sealed class SqlTableInfo
    {
        private readonly Dictionary<string, SqlColumnInfo> _byName;

        public SqlTableInfo(string connection, string schema, string name, bool isView, IReadOnlyList<SqlColumnInfo> columns)
        {
            Connection = connection;
            Schema = schema;
            Name = name;
            IsView = isView;
            Columns = columns;
            _byName = new Dictionary<string, SqlColumnInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var column in columns)
            {
                if (!_byName.ContainsKey(column.Name))
                {
                    _byName[column.Name] = column;
                }
            }
        }

        /// <summary>The connection whose snapshot lists the table.</summary>
        public string Connection { get; }

        public string Schema { get; }

        public string Name { get; }

        public bool IsView { get; }

        public IReadOnlyList<SqlColumnInfo> Columns { get; }

        /// <summary>The column named <paramref name="name"/> (case-insensitive), or null.</summary>
        public SqlColumnInfo? FindColumn(string name) => _byName.TryGetValue(name, out var column) ? column : null;
    }

    public sealed class SqlColumnInfo
    {
        public SqlColumnInfo(string name, string? dbType, string? rustType, bool nullable, bool primaryKey, bool autoIncrement)
        {
            Name = name;
            DbType = dbType;
            RustType = rustType;
            Nullable = nullable;
            PrimaryKey = primaryKey;
            AutoIncrement = autoIncrement;
        }

        public string Name { get; }

        public string? DbType { get; }

        public string? RustType { get; }

        public bool Nullable { get; }

        public bool PrimaryKey { get; }

        public bool AutoIncrement { get; }
    }

    public sealed class SqlFunctionInfo
    {
        public SqlFunctionInfo(string connection, string schema, string name, string kind, string? returnType, string? arguments)
        {
            Connection = connection;
            Schema = schema;
            Name = name;
            Kind = kind;
            ReturnType = returnType;
            Arguments = arguments;
        }

        public string Connection { get; }

        public string Schema { get; }

        public string Name { get; }

        /// <summary><c>function</c> or <c>procedure</c>.</summary>
        public string Kind { get; }

        public string? ReturnType { get; }

        public string? Arguments { get; }
    }

    /// <summary>
    /// The schemas a Rust crate's SQL can see: the snapshots of every connection its <c>.kbdata</c> files name, merged.
    /// Name lookups are case-insensitive (unquoted SQL names), which errs on the side of no warning.
    /// </summary>
    public sealed class SqlSchemaIndex
    {
        public static readonly SqlSchemaIndex Empty = new SqlSchemaIndex(new SchemaSnapshot[0]);

        private static readonly HashSet<string> DefaultSchemas = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "public", "main", "dbo", string.Empty };

        private readonly Dictionary<string, List<SqlTableInfo>> _tables = new Dictionary<string, List<SqlTableInfo>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, SqlSchemaInfo> _schemas = new Dictionary<string, SqlSchemaInfo>(StringComparer.OrdinalIgnoreCase);

        public SqlSchemaIndex(IReadOnlyList<SchemaSnapshot> snapshots)
        {
            Snapshots = snapshots ?? throw new ArgumentNullException(nameof(snapshots));
            foreach (var snapshot in snapshots)
            {
                foreach (var schema in snapshot.Schemas)
                {
                    if (!_schemas.ContainsKey(schema.Name))
                    {
                        _schemas[schema.Name] = schema;
                    }

                    foreach (var table in schema.Tables)
                    {
                        if (!_tables.TryGetValue(table.Name, out var list))
                        {
                            list = new List<SqlTableInfo>();
                            _tables[table.Name] = list;
                        }

                        list.Add(table);
                    }
                }
            }

            ConnectionLabel = string.Join(", ", snapshots.Select(s => s.Connection).Distinct(StringComparer.Ordinal));
        }

        public IReadOnlyList<SchemaSnapshot> Snapshots { get; }

        /// <summary>True when no snapshot is loaded: only keywords are offered and nothing is flagged.</summary>
        public bool IsEmpty => Snapshots.Count == 0;

        /// <summary>The connection names, for messages ("Shop" or "Shop, Sales").</summary>
        public string ConnectionLabel { get; }

        public IEnumerable<SqlTableInfo> Tables => Snapshots.SelectMany(s => s.Schemas).SelectMany(s => s.Tables);

        public IEnumerable<SqlFunctionInfo> Functions => Snapshots.SelectMany(s => s.Schemas).SelectMany(s => s.Functions);

        public IEnumerable<SqlSchemaInfo> Schemas => _schemas.Values;

        /// <summary>True when the schema's tables can be named without their schema (public, main, dbo, or the only schema).</summary>
        public bool IsDefaultSchema(string schema) => DefaultSchemas.Contains(schema) || _schemas.Count == 1;

        public SqlSchemaInfo? FindSchema(string name) => _schemas.TryGetValue(name, out var schema) ? schema : null;

        /// <summary>The tables and views named <paramref name="name"/> in any schema.</summary>
        public IReadOnlyList<SqlTableInfo> FindTables(string name) =>
            _tables.TryGetValue(name, out var list) ? list : (IReadOnlyList<SqlTableInfo>)new SqlTableInfo[0];

        /// <summary>The table <paramref name="name"/> of <paramref name="schema"/> (any schema when null); when several match, the default schema's.</summary>
        public SqlTableInfo? FindTable(string? schema, string name)
        {
            var candidates = FindTables(name);
            if (schema != null)
            {
                return candidates.FirstOrDefault(t => string.Equals(t.Schema, schema, StringComparison.OrdinalIgnoreCase));
            }

            return candidates.Count == 1 ? candidates[0] : candidates.FirstOrDefault(t => DefaultSchemas.Contains(t.Schema)) ?? candidates.FirstOrDefault();
        }
    }
}
