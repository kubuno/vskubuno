using System;
using System.Collections.Generic;
using System.Linq;
using Kubuno.Desktop.Logic.Data;

namespace Kubuno.Desktop.Logic.DataSources
{
    /// <summary>The pages of the "Add Data Source" wizard.</summary>
    public enum DataSourceWizardStep
    {
        /// <summary>(a) The Data Explorer connection to read the schema from (or a new one).</summary>
        Connection,

        /// <summary>(b) The connection string's name in the application and the project's secret store.</summary>
        Application,

        /// <summary>(c) The tables and views.</summary>
        Objects,

        /// <summary>(d) The data source's name and a summary of what is written.</summary>
        Name,
    }

    /// <summary>A table or view offered by the wizard (a check box of the objects page).</summary>
    public sealed class DataSourceWizardObject
    {
        public DataSourceWizardObject(string schema, string name, bool isView, int columnCount)
        {
            Schema = schema;
            Name = name;
            IsView = isView;
            ColumnCount = columnCount;
        }

        public string Schema { get; }

        public string Name { get; }

        public bool IsView { get; }

        public int ColumnCount { get; }

        public bool Selected { get; set; }
    }

    /// <summary>What "Configure..." starts from: the existing data source.</summary>
    public sealed class DataSourceWizardExisting
    {
        public DataSourceWizardExisting(string name, string connection, string schema, IEnumerable<string> tables, string? explorerConnection = null)
        {
            ExplorerConnection = explorerConnection;
            Name = name;
            Connection = connection;
            Schema = schema;
            Tables = tables.ToList();
        }

        /// <summary>The module name (file stem).</summary>
        public string Name { get; }

        public string Connection { get; }

        public string Schema { get; }

        /// <summary>The table names as the <c>.kbdata</c> writes them (<c>customers</c>, <c>other.t</c>).</summary>
        public IReadOnlyList<string> Tables { get; }

        /// <summary>The Data Explorer connection the source was last built from (remembered by the Data Sources window), or null.</summary>
        public string? ExplorerConnection { get; }
    }

    /// <summary>
    /// The state and rules of the "Add Data Source" wizard (docs/DATA.md §9, DATA-6), without any UI: the steps, what each one
    /// requires before "Next", the names it proposes (the data source's module name and the connection string's name, from the
    /// chosen Data Explorer connection until the developer types their own), the Kubuno module rule (a module's crate only
    /// offers - and the <c>.kbdata</c> uses - the module's own schema) and the summary of the last page. The dialog is a view of it.
    /// </summary>
    public sealed class DataSourceWizardModel
    {
        private readonly HashSet<string> _existingSources;
        private List<DataSourceWizardObject> _objects = new List<DataSourceWizardObject>();
        private bool _connectionNameEdited;
        private bool _sourceNameEdited;
        private string? _explorerConnection;
        private string _connectionName = string.Empty;
        private string _sourceName = string.Empty;

        public DataSourceWizardModel(IEnumerable<ExplorerConnectionInfo> connections, IEnumerable<string> existingSources, string? moduleSchema, DataSourceWizardExisting? existing = null)
        {
            Connections = connections.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
            _existingSources = new HashSet<string>(existingSources, StringComparer.OrdinalIgnoreCase);
            ModuleSchema = string.IsNullOrWhiteSpace(moduleSchema) ? null : moduleSchema;
            Existing = existing;
            if (existing != null)
            {
                _sourceName = existing.Name;
                _connectionName = existing.Connection;
                _sourceNameEdited = true;
                _connectionNameEdited = true;
                // The connection it was built from, else one named like its connection string, else the first.
                _explorerConnection = Connections.FirstOrDefault(c => string.Equals(c.Name, existing.ExplorerConnection, StringComparison.Ordinal))?.Name
                    ?? Connections.FirstOrDefault(c => string.Equals(c.Name, existing.Connection, StringComparison.OrdinalIgnoreCase))?.Name
                    ?? Connections.FirstOrDefault()?.Name;
            }
            else
            {
                ExplorerConnection = Connections.FirstOrDefault()?.Name;
            }
        }

        public IReadOnlyList<ExplorerConnectionInfo> Connections { get; private set; }

        public DataSourceWizardStep Step { get; private set; } = DataSourceWizardStep.Connection;

        /// <summary>"Configure..." of an existing source (its name is fixed, its tables preselected).</summary>
        public DataSourceWizardExisting? Existing { get; }

        public bool IsReconfigure => Existing != null;

        /// <summary>The Kubuno module schema this crate must use, or null.</summary>
        public string? ModuleSchema { get; }

        /// <summary>The chosen Data Explorer connection (its name).</summary>
        public string? ExplorerConnection
        {
            get => _explorerConnection;
            set
            {
                if (string.Equals(_explorerConnection, value, StringComparison.Ordinal))
                {
                    return;
                }

                _explorerConnection = value;
                _objects = new List<DataSourceWizardObject>();
                SchemaError = null;
                if (value != null)
                {
                    if (!_connectionNameEdited)
                    {
                        _connectionName = DataSourceNames.ToConnectionName(value);
                    }

                    if (!_sourceNameEdited)
                    {
                        _sourceName = DataSourceNames.UniqueSourceName(DataSourceNames.ToSnakeCase(value), _existingSources);
                    }
                }
            }
        }

        public ExplorerConnectionInfo? ExplorerConnectionInfo => Connections.FirstOrDefault(c => string.Equals(c.Name, _explorerConnection, StringComparison.Ordinal));

        /// <summary>The name of <c>ConnectionStrings:&lt;name&gt;</c> in the application.</summary>
        public string ConnectionName
        {
            get => _connectionName;
            set
            {
                _connectionName = (value ?? string.Empty).Trim();
                _connectionNameEdited = true;
            }
        }

        /// <summary>Where the connection string is copied for the project (user secrets by default).</summary>
        public CredentialStoreKind Store { get; set; } = CredentialStoreKind.UserSecrets;

        /// <summary>The data source's module name (<c>src/data/&lt;name&gt;.kbdata</c>).</summary>
        public string SourceName
        {
            get => _sourceName;
            set
            {
                if (IsReconfigure)
                {
                    return;
                }

                _sourceName = (value ?? string.Empty).Trim();
                _sourceNameEdited = true;
            }
        }

        /// <summary>The tables and views offered (after <see cref="LoadObjects"/>).</summary>
        public IReadOnlyList<DataSourceWizardObject> Objects => _objects;

        /// <summary>Why no object can be offered (the module's schema is missing...), or null.</summary>
        public string? SchemaError { get; private set; }

        /// <summary>The provider of the loaded schema.</summary>
        public string? Provider { get; private set; }

        public bool SchemaLoaded { get; private set; }

        public IReadOnlyList<DataSourceWizardObject> SelectedObjects => _objects.Where(o => o.Selected).ToList();

        /// <summary>The <c>schema</c> parameter of <c>kbdata.build</c>: the module's schema, else null (the tool infers it).</summary>
        public string? SchemaParameter => ModuleSchema;

        public bool CanGoBack => Step != DataSourceWizardStep.Connection;

        public bool IsLastStep => Step == DataSourceWizardStep.Name;

        /// <summary>Replaces the connection list (after "New connection...") and selects <paramref name="select"/>.</summary>
        public void SetConnections(IEnumerable<ExplorerConnectionInfo> connections, string? select)
        {
            Connections = connections.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
            if (select != null)
            {
                ExplorerConnection = select;
            }
            else if (_explorerConnection != null && ExplorerConnectionInfo is null)
            {
                ExplorerConnection = Connections.FirstOrDefault()?.Name;
            }
        }

        /// <summary>Offers the tables and views of <paramref name="schema"/> (only the module's schema for a Kubuno module), preselecting a "Configure..." source's tables.</summary>
        public void LoadObjects(DatabaseSchemaInfo schema)
        {
            Provider = schema.Provider;
            SchemaLoaded = true;
            SchemaError = null;
            var previous = new HashSet<string>(_objects.Where(o => o.Selected).Select(o => o.Schema + "." + o.Name), StringComparer.Ordinal);
            var objects = new List<DataSourceWizardObject>();
            foreach (var s in schema.Schemas)
            {
                if (ModuleSchema != null && !string.Equals(s.Name, ModuleSchema, StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (var table in s.Tables.OrderBy(t => t.IsView).ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
                {
                    var item = new DataSourceWizardObject(s.Name, table.Name, table.IsView, table.Columns.Count);
                    item.Selected = previous.Contains(s.Name + "." + table.Name) || (previous.Count == 0 && Existing != null && IsExistingTable(s.Name, table.Name));
                    objects.Add(item);
                }
            }

            if (ModuleSchema != null && !schema.Schemas.Any(s => string.Equals(s.Name, ModuleSchema, StringComparison.Ordinal)))
            {
                SchemaError = DataSourcesText.ModuleSchemaMissing(ModuleSchema);
            }
            else if (objects.Count == 0)
            {
                SchemaError = DataSourcesText.NoTables;
            }

            _objects = objects;
        }

        /// <summary>Null when <paramref name="step"/> is complete, else what is missing.</summary>
        public string? Validate(DataSourceWizardStep step)
        {
            switch (step)
            {
                case DataSourceWizardStep.Connection:
                    return ExplorerConnectionInfo is null ? DataSourcesText.ChooseConnection : null;
                case DataSourceWizardStep.Application:
                    return DataSourceNames.ValidateConnectionName(ConnectionName);
                case DataSourceWizardStep.Objects:
                    if (!SchemaLoaded)
                    {
                        return DataSourcesText.SchemaNotLoaded;
                    }

                    if (SchemaError != null && _objects.Count == 0)
                    {
                        return SchemaError;
                    }

                    return _objects.Any(o => o.Selected) ? null : DataSourcesText.ChooseObjects;
                default:
                    if (DataSourceNames.ValidateSourceName(SourceName) is { } error)
                    {
                        return error;
                    }

                    return !IsReconfigure && _existingSources.Contains(SourceName) ? DataSourcesText.SourceExists(SourceName) : null;
            }
        }

        /// <summary>Goes to the next page when the current one is complete; returns its error otherwise.</summary>
        public string? Next()
        {
            if (Validate(Step) is { } error)
            {
                return error;
            }

            if (Step != DataSourceWizardStep.Name)
            {
                Step++;
            }

            return null;
        }

        public void Back()
        {
            if (Step != DataSourceWizardStep.Connection)
            {
                Step--;
            }
        }

        /// <summary>Null when every page is complete (Finish), else the first error (and <see cref="Step"/> moves to its page).</summary>
        public string? ValidateAll()
        {
            foreach (DataSourceWizardStep step in Enum.GetValues(typeof(DataSourceWizardStep)))
            {
                if (Validate(step) is { } error)
                {
                    Step = step;
                    return error;
                }
            }

            return null;
        }

        /// <summary>The lines of the last page: what Finish writes and where the secret goes (never the secret itself).</summary>
        public IReadOnlyList<string> Summary(bool hasMainModule, bool modExists, bool cargoNeedsFeature, bool cargoNeedsSecretsId)
        {
            var lines = new List<string>();
            string name = SourceName;
            if (IsReconfigure)
            {
                lines.Add(DataSourcesText.SummaryRewriteKbdata(name));
            }
            else
            {
                lines.Add(DataSourcesText.SummaryWriteKbdata(name));
                lines.Add(DataSourcesText.SummaryWriteUserFile(name));
                lines.Add(modExists ? DataSourcesText.SummaryEditMod(name) : DataSourcesText.SummaryCreateMod(name));
                if (!hasMainModule)
                {
                    lines.Add(DataSourcesText.SummaryEditMain);
                }
            }

            if (cargoNeedsFeature)
            {
                lines.Add(DataSourcesText.SummaryCargoFeature);
            }

            if (cargoNeedsSecretsId)
            {
                lines.Add(DataSourcesText.SummaryCargoSecretsId);
            }

            lines.Add(DataSourcesText.SummarySecret(ConnectionName, Store));
            lines.Add(DataSourcesText.SummaryTables(SelectedObjects.Select(o => o.Name)));
            lines.Add(DataSourcesText.SummarySqlx);
            return lines;
        }

        private bool IsExistingTable(string schema, string table)
        {
            foreach (var name in Existing!.Tables)
            {
                int dot = name.LastIndexOf('.');
                if (dot < 0 ? string.Equals(name, table, StringComparison.Ordinal) : string.Equals(name.Substring(dot + 1), table, StringComparison.Ordinal) && string.Equals(name.Substring(0, dot), schema, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
