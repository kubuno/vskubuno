using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Kubuno.VisualStudio.Core.DataSources
{
    /// <summary>
    /// A typed data source (<c>src/**/*.kbdata</c>, docs/DATA.md DATA-4) as <c>kubuno-data-tool</c>'s <c>kbdata.read</c> returns it:
    /// the model crate's serde names (<c>db_type</c>, <c>rust_type</c>...) plus <c>rowNames</c>. Never holds a connection string:
    /// <see cref="Connection"/> is only the <em>name</em> of <c>ConnectionStrings:&lt;name&gt;</c>.
    /// </summary>
    public sealed class KbdataSourceInfo
    {
        public KbdataSourceInfo(string filePath, string name, string connection, string provider, string schema, IReadOnlyList<KbdataTableInfo> tables)
        {
            FilePath = filePath;
            Name = name;
            Connection = connection;
            Provider = provider;
            Schema = schema;
            Tables = tables;
        }

        /// <summary>The <c>.kbdata</c> file.</summary>
        public string FilePath { get; }

        public string Name { get; }

        /// <summary>The connection string's name (<c>ConnectionStrings:&lt;Connection&gt;</c>).</summary>
        public string Connection { get; }

        /// <summary><c>sqlite</c>, <c>postgres</c>, <c>mysql</c> or <c>sqlserver</c>.</summary>
        public string Provider { get; }

        /// <summary>The PostgreSQL schema / MySQL database of the source; empty = the connection's default.</summary>
        public string Schema { get; }

        public IReadOnlyList<KbdataTableInfo> Tables { get; }

        /// <summary>The Rust module name of the source: the file's stem (<c>shop.kbdata</c> → <c>shop</c>).</summary>
        public string ModuleName => System.IO.Path.GetFileNameWithoutExtension(FilePath);

        public KbdataTableInfo? Table(string name) => Tables.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.Ordinal));

        /// <summary>Parses a <c>kbdata.read</c> result. Tolerant: unknown fields are ignored, missing ones defaulted.</summary>
        public static KbdataSourceInfo Parse(string filePath, JsonElement root)
        {
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("kbdata.read did not return an object.");
            }

            var rowNames = new Dictionary<string, string>(StringComparer.Ordinal);
            if (root.TryGetProperty("rowNames", out var rows) && rows.ValueKind == JsonValueKind.Object)
            {
                foreach (var row in rows.EnumerateObject())
                {
                    if (row.Value.ValueKind == JsonValueKind.String)
                    {
                        rowNames[row.Name] = row.Value.GetString()!;
                    }
                }
            }

            var tables = new List<KbdataTableInfo>();
            if (root.TryGetProperty("tables", out var tablesElement) && tablesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var t in tablesElement.EnumerateArray())
                {
                    string tableName = Str(t, "name");
                    var columns = new List<KbdataColumnInfo>();
                    if (t.TryGetProperty("columns", out var columnsElement) && columnsElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var c in columnsElement.EnumerateArray())
                        {
                            columns.Add(new KbdataColumnInfo(
                                Str(c, "name"),
                                Str(c, "db_type"),
                                Str(c, "rust_type"),
                                Bool(c, "nullable", false),
                                Bool(c, "auto_increment", false),
                                Bool(c, "read_only", false),
                                c.TryGetProperty("max_length", out var ml) && ml.TryGetInt64(out var length) ? length : 0));
                        }
                    }

                    var key = new List<string>();
                    if (t.TryGetProperty("key", out var keyElement) && keyElement.ValueKind == JsonValueKind.Array)
                    {
                        key.AddRange(keyElement.EnumerateArray().Where(k => k.ValueKind == JsonValueKind.String).Select(k => k.GetString()!));
                    }

                    string row = Str(t, "row");
                    if (row.Length == 0 && !rowNames.TryGetValue(tableName, out row!))
                    {
                        row = DataSourceNames.RowName(tableName);
                    }

                    tables.Add(new KbdataTableInfo(tableName, string.Equals(Str(t, "kind"), "view", StringComparison.OrdinalIgnoreCase), row, key, columns));
                }
            }

            return new KbdataSourceInfo(filePath, Str(root, "name"), Str(root, "connection"), Str(root, "provider"), Str(root, "schema"), tables);
        }

        private static string Str(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

        private static bool Bool(JsonElement element, string name, bool fallback) =>
            element.TryGetProperty(name, out var value) ? value.ValueKind == JsonValueKind.True || (value.ValueKind != JsonValueKind.False && fallback) : fallback;
    }

    /// <summary>A table or a view of a data source.</summary>
    public sealed class KbdataTableInfo
    {
        public KbdataTableInfo(string name, bool isView, string rowName, IReadOnlyList<string> key, IReadOnlyList<KbdataColumnInfo> columns)
        {
            Name = name;
            IsView = isView;
            RowName = rowName;
            Key = key;
            Columns = columns;
        }

        /// <summary>The table's name as the source names it (<c>customers</c>, or <c>other_schema.t</c> outside the source's schema).</summary>
        public string Name { get; }

        public bool IsView { get; }

        /// <summary>The generated row struct (<c>Customer</c>).</summary>
        public string RowName { get; }

        public IReadOnlyList<string> Key { get; }

        public IReadOnlyList<KbdataColumnInfo> Columns { get; }

        /// <summary>The name without its schema qualification (<c>other.t</c> → <c>t</c>).</summary>
        public string BareName => Name.Contains('.') ? Name.Substring(Name.LastIndexOf('.') + 1) : Name;

        public KbdataColumnInfo? Column(string name) => Columns.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.Ordinal));
    }

    /// <summary>A column of a data source table.</summary>
    public sealed class KbdataColumnInfo
    {
        public KbdataColumnInfo(string name, string dbType, string rustType, bool nullable, bool autoIncrement, bool readOnly, long maxLength)
        {
            Name = name;
            DbType = dbType;
            RustType = rustType;
            Nullable = nullable;
            AutoIncrement = autoIncrement;
            ReadOnly = readOnly;
            MaxLength = maxLength;
        }

        public string Name { get; }

        public string DbType { get; }

        public string RustType { get; }

        public bool Nullable { get; }

        public bool AutoIncrement { get; }

        public bool ReadOnly { get; }

        public long MaxLength { get; }

        /// <summary>What kind of value the column holds (drives the default control, the format and the alignment).</summary>
        public DataColumnKind Kind => DataColumnKinds.Classify(DbType, RustType);
    }
}
