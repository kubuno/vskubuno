using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Kubuno.VisualStudio.Core.DataSources
{
    /// <summary>
    /// The drop choices of the Data Sources window (a table's Grid / Details mode, a column's control), kept per project in
    /// <c>&lt;solution or folder&gt;\.vs\kubuno\datasources.json</c> - never in the <c>.kbdata</c>, which describes the database, not the
    /// designer's preferences. Keyed by the <c>.kbdata</c> path relative to that root. Not thread-safe (UI thread).
    /// </summary>
    public sealed class DataSourcesSettings
    {
        /// <summary>The file, relative to the workspace / solution directory.</summary>
        public const string RelativePath = @".vs\kubuno\datasources.json";

        private readonly string _root;
        private readonly JsonObject _data;

        private DataSourcesSettings(string root, JsonObject data)
        {
            _root = root;
            _data = data;
        }

        public string FilePath => Path.Combine(_root, RelativePath);

        /// <summary>Loads the settings of <paramref name="root"/> (empty when the file is missing or unreadable).</summary>
        public static DataSourcesSettings Load(string root)
        {
            JsonObject data = new JsonObject();
            try
            {
                string path = Path.Combine(root, RelativePath);
                if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonObject parsed)
                {
                    data = parsed;
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                // A corrupt or locked file only loses the preferences.
            }

            return new DataSourcesSettings(root, data);
        }

        public DataTableDropMode ModeOf(string kbdataPath, string table) =>
            TableNode(kbdataPath, table, create: false)?["mode"]?.GetValue<string>() == "details" ? DataTableDropMode.Details : DataTableDropMode.Grid;

        public void SetMode(string kbdataPath, string table, DataTableDropMode mode) =>
            TableNode(kbdataPath, table, create: true)!["mode"] = mode == DataTableDropMode.Details ? "details" : "grid";

        /// <summary>The control chosen for a column, or null (its default).</summary>
        public DataControlKind? ControlOf(string kbdataPath, string table, string column)
        {
            string? text = TableNode(kbdataPath, table, create: false)?["columns"]?[column]?.GetValue<string>();
            return text != null && Enum.TryParse(text, out DataControlKind kind) ? kind : (DataControlKind?)null;
        }

        public void SetControl(string kbdataPath, string table, string column, DataControlKind kind)
        {
            var node = TableNode(kbdataPath, table, create: true)!;
            if (node["columns"] is not JsonObject columns)
            {
                columns = new JsonObject();
                node["columns"] = columns;
            }

            columns[column] = kind.ToString();
        }

        /// <summary>Every column's chosen control of a table (for a drop).</summary>
        public IReadOnlyDictionary<string, DataControlKind> ControlsOf(string kbdataPath, string table)
        {
            var result = new Dictionary<string, DataControlKind>(StringComparer.Ordinal);
            if (TableNode(kbdataPath, table, create: false)?["columns"] is JsonObject columns)
            {
                foreach (var pair in columns)
                {
                    if (pair.Value is JsonValue value && value.TryGetValue(out string? text) && Enum.TryParse(text, out DataControlKind kind))
                    {
                        result[pair.Key] = kind;
                    }
                }
            }

            return result;
        }

        /// <summary>The Data Explorer connection a source was last built from (what "Configure..." proposes first), or null.</summary>
        public string? ExplorerConnectionOf(string kbdataPath) => SourceNode(kbdataPath, create: false)?["explorer"] is JsonValue value && value.TryGetValue(out string? name) ? name : null;

        public void SetExplorerConnection(string kbdataPath, string explorerConnection) => SourceNode(kbdataPath, create: true)!["explorer"] = explorerConnection;

        /// <summary>Writes the file (creating <c>.vs\kubuno</c>). IO errors are the caller's to report.</summary>
        public void Save()
        {
            string path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            _data["version"] = 1;
            File.WriteAllText(path, _data.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        }

        /// <summary>The key of a <c>.kbdata</c>: its path relative to the root, with <c>/</c> (or the full path outside the root).</summary>
        public string KeyOf(string kbdataPath)
        {
            string full = Path.GetFullPath(kbdataPath);
            string root = Path.GetFullPath(_root).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            string relative = full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full.Substring(root.Length) : full;
            return relative.Replace('\\', '/');
        }

        private JsonObject? SourceNode(string kbdataPath, bool create)
        {
            if (_data["sources"] is not JsonObject sources)
            {
                if (!create)
                {
                    return null;
                }

                sources = new JsonObject();
                _data["sources"] = sources;
            }

            string key = KeyOf(kbdataPath);
            string? existingKey = sources.Select(p => p.Key).FirstOrDefault(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
            if (existingKey is null || sources[existingKey] is not JsonObject source)
            {
                if (!create)
                {
                    return null;
                }

                source = new JsonObject();
                sources[existingKey ?? key] = source;
            }

            return source;
        }

        private JsonObject? TableNode(string kbdataPath, string table, bool create)
        {
            if (SourceNode(kbdataPath, create) is not { } source)
            {
                return null;
            }

            if (source["tables"] is not JsonObject tables)
            {
                if (!create)
                {
                    return null;
                }

                tables = new JsonObject();
                source["tables"] = tables;
            }

            if (tables[table] is not JsonObject node)
            {
                if (!create)
                {
                    return null;
                }

                node = new JsonObject();
                tables[table] = node;
            }

            return node;
        }
    }
}
