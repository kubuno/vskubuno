using System;
using System.Collections.Generic;
using System.Linq;

namespace Kubuno.Desktop.Logic.Data
{
    /// <summary>The node kinds of the Data Explorer tree; each maps to one image (KnownMonikers in Visual Studio).</summary>
    public enum DataNodeKind
    {
        Connection,
        Schema,
        TablesFolder,
        ViewsFolder,
        FunctionsFolder,
        ProceduresFolder,
        Table,
        View,
        ColumnsFolder,
        KeysFolder,
        IndexesFolder,
        Column,
        PrimaryKeyColumn,
        PrimaryKey,
        ForeignKey,
        Index,
        Function,
        Procedure,
        Loading,
        Error,
        Message,
    }

    /// <summary>
    /// One node of the Data Explorer, UI-free: text, kind, and what commands need (connection, schema, object name).
    /// Everything below a connection is built at once from its <c>schema.load</c> snapshot
    /// (<see cref="DataExplorerTreeBuilder"/>); only the connection node itself loads lazily.
    /// </summary>
    public sealed class DataExplorerNode
    {
        public DataExplorerNode(DataNodeKind kind, string text, string connection, string? schema = null, string? objectName = null, IReadOnlyList<DataExplorerNode>? children = null)
        {
            Kind = kind;
            Text = text;
            Connection = connection;
            Schema = schema;
            ObjectName = objectName;
            Children = children ?? Array.Empty<DataExplorerNode>();
        }

        public DataNodeKind Kind { get; }

        public string Text { get; }

        public string? ToolTip { get; set; }

        /// <summary>The Data Explorer connection this node belongs to.</summary>
        public string Connection { get; }

        public string? Schema { get; }

        /// <summary>Table, view, function, column, key or index name.</summary>
        public string? ObjectName { get; }

        public IReadOnlyList<DataExplorerNode> Children { get; }

        /// <summary>Tables and views: "Show data", "Generate script".</summary>
        public bool IsTableOrView => Kind == DataNodeKind.Table || Kind == DataNodeKind.View;

        public override string ToString() => Text;
    }

    /// <summary>Builds the Data Explorer nodes from the tool's models (texts in the UI language, stable ordering).</summary>
    public static class DataExplorerTreeBuilder
    {
        /// <summary>
        /// The children of a connection node: one node per schema, or - for a database with a single schema that is
        /// implicit (SQLite's <c>main</c>) - that schema's folders directly.
        /// </summary>
        public static IReadOnlyList<DataExplorerNode> BuildConnectionChildren(string connection, DatabaseSchemaInfo database)
        {
            var schemas = database.Schemas.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
            if (schemas.Count == 1 && (database.ProviderKind == DataProviderKind.Sqlite || schemas[0].Name.Length == 0))
            {
                return BuildSchemaChildren(connection, schemas[0], database.ProviderKind);
            }

            return schemas
                .Select(s => new DataExplorerNode(DataNodeKind.Schema, s.Name, connection, s.Name, s.Name, BuildSchemaChildren(connection, s, database.ProviderKind)))
                .ToList();
        }

        public static IReadOnlyList<DataExplorerNode> BuildSchemaChildren(string connection, SchemaInfo schema, DataProviderKind? provider)
        {
            var tables = schema.Tables.Where(t => !t.IsView).OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).Select(t => BuildTable(connection, schema.Name, t)).ToList();
            var views = schema.Tables.Where(t => t.IsView).OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).Select(t => BuildTable(connection, schema.Name, t)).ToList();
            var nodes = new List<DataExplorerNode>
            {
                Folder(DataNodeKind.TablesFolder, DataText.Tables, connection, schema.Name, tables),
                Folder(DataNodeKind.ViewsFolder, DataText.Views, connection, schema.Name, views),
            };

            // SQLite has no functions: no empty folder for it.
            if (provider != DataProviderKind.Sqlite || schema.Functions.Count > 0)
            {
                var functions = schema.Functions.Where(f => !f.IsProcedure).OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).Select(f => BuildFunction(connection, schema.Name, f)).ToList();
                nodes.Add(Folder(DataNodeKind.FunctionsFolder, DataText.Functions, connection, schema.Name, functions));
                var procedures = schema.Functions.Where(f => f.IsProcedure).OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).Select(f => BuildFunction(connection, schema.Name, f)).ToList();
                if (procedures.Count > 0 || provider == DataProviderKind.SqlServer || provider == DataProviderKind.MySql || provider == DataProviderKind.Postgres)
                {
                    nodes.Add(Folder(DataNodeKind.ProceduresFolder, DataText.Procedures, connection, schema.Name, procedures));
                }
            }

            return nodes;
        }

        public static DataExplorerNode BuildTable(string connection, string schema, TableInfo table)
        {
            var columns = table.Columns.Select(c => new DataExplorerNode(
                c.PrimaryKey || table.PrimaryKey.Contains(c.Name) ? DataNodeKind.PrimaryKeyColumn : DataNodeKind.Column,
                ColumnText(table, c),
                connection,
                schema,
                c.Name) { ToolTip = ColumnToolTip(c) }).ToList();

            var keys = new List<DataExplorerNode>();
            if (table.PrimaryKey.Count > 0)
            {
                keys.Add(new DataExplorerNode(DataNodeKind.PrimaryKey, $"PK ({string.Join(", ", table.PrimaryKey)})", connection, schema, table.Name));
            }

            foreach (var fk in table.ForeignKeys.OrderBy(f => f.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            {
                string reference = string.IsNullOrEmpty(fk.RefSchema) || string.Equals(fk.RefSchema, schema, StringComparison.Ordinal) ? fk.RefTable : $"{fk.RefSchema}.{fk.RefTable}";
                string name = string.IsNullOrEmpty(fk.Name) ? "FK" : fk.Name!;
                keys.Add(new DataExplorerNode(DataNodeKind.ForeignKey, $"{name} ({string.Join(", ", fk.Columns)}) → {reference} ({string.Join(", ", fk.RefColumns)})", connection, schema, fk.Name));
            }

            var indexes = table.Indexes
                .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .Select(i => new DataExplorerNode(DataNodeKind.Index, $"{i.Name} ({string.Join(", ", i.Columns)})" + (i.Unique ? ", " + DataText.Unique : string.Empty), connection, schema, i.Name))
                .ToList();

            var children = new List<DataExplorerNode>
            {
                Folder(DataNodeKind.ColumnsFolder, DataText.Columns, connection, schema, columns, table.Name),
                Folder(DataNodeKind.KeysFolder, DataText.Keys, connection, schema, keys, table.Name),
                Folder(DataNodeKind.IndexesFolder, DataText.Indexes, connection, schema, indexes, table.Name),
            };
            return new DataExplorerNode(table.IsView ? DataNodeKind.View : DataNodeKind.Table, table.Name, connection, schema, table.Name, children)
            {
                ToolTip = string.IsNullOrEmpty(schema) ? table.Name : $"{schema}.{table.Name}",
            };
        }

        /// <summary><c>id (INTEGER, PK, auto-incrément, non NULL)</c> / <c>customer_id (INTEGER, FK, NULL)</c>.</summary>
        public static string ColumnText(TableInfo table, ColumnInfo column)
        {
            var parts = new List<string>();
            parts.Add(string.IsNullOrEmpty(column.DbType) ? "?" : column.DbType + (column.MaxLength > 0 && column.DbType.IndexOf('(') < 0 ? $"({column.MaxLength})" : string.Empty));
            if (column.PrimaryKey || table.PrimaryKey.Contains(column.Name))
            {
                parts.Add("PK");
            }

            if (table.ForeignKeys.Any(f => f.Columns.Contains(column.Name)))
            {
                parts.Add("FK");
            }

            if (column.AutoIncrement)
            {
                parts.Add(DataText.AutoIncrement);
            }

            parts.Add(column.Nullable ? DataText.Null : DataText.NotNull);
            return $"{column.Name} ({string.Join(", ", parts)})";
        }

        private static string? ColumnToolTip(ColumnInfo column)
        {
            var lines = new List<string>();
            if (!string.IsNullOrEmpty(column.RustType))
            {
                lines.Add("Rust: " + column.RustType);
            }

            if (column.DefaultText is { } value)
            {
                lines.Add(DataText.T("Default: ", "Valeur par défaut : ") + value);
            }

            return lines.Count == 0 ? null : string.Join("\n", lines);
        }

        private static DataExplorerNode BuildFunction(string connection, string schema, FunctionInfo function)
        {
            string text = $"{function.Name}({function.Arguments ?? string.Empty})" + (string.IsNullOrEmpty(function.ReturnType) ? string.Empty : " → " + function.ReturnType);
            return new DataExplorerNode(function.IsProcedure ? DataNodeKind.Procedure : DataNodeKind.Function, text, connection, schema, function.Name);
        }

        private static DataExplorerNode Folder(DataNodeKind kind, string text, string connection, string schema, List<DataExplorerNode> children, string? objectName = null) =>
            new DataExplorerNode(kind, text, connection, schema, objectName, children.Count == 0 ? new[] { new DataExplorerNode(DataNodeKind.Message, DataText.Empty, connection, schema) } : children);
    }
}
