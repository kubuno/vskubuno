using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Desktop.Logic.Data;
using Kubuno.Desktop.Logic.DataSources;
using Kubuno.Desktop.Logic.Sql;
using Kubuno.Desktop.DataExplorer;
using Kubuno.Shared.Logging;
using Kubuno.Desktop.Options;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.TaskStatusCenter;

namespace Kubuno.Desktop.DataSources
{
    /// <summary>What the last page of the wizard says Finish will change (read from the crate's files).</summary>
    internal sealed class DataSourceSummaryFacts
    {
        public DataSourceSummaryFacts(bool hasDataModule, bool modExists, bool needsFeature, bool needsSecretsId)
        {
            HasDataModule = hasDataModule;
            ModExists = modExists;
            NeedsFeature = needsFeature;
            NeedsSecretsId = needsSecretsId;
        }

        public bool HasDataModule { get; }

        public bool ModExists { get; }

        public bool NeedsFeature { get; }

        public bool NeedsSecretsId { get; }
    }

    /// <summary>What the wizard needs from the outside world (the real one: <see cref="DataSourceGenerator"/>; the dialog gallery: samples).</summary>
    internal interface IDataSourceWizardBackend
    {
        /// <summary>Shows the Data Explorer's "Add Connection" dialog; the new connection's name, or null.</summary>
        Task<string?> NewConnectionAsync(IReadOnlyCollection<string> existing);

        Task<IReadOnlyList<ExplorerConnectionInfo>> ListConnectionsAsync();

        Task<DatabaseSchemaInfo> LoadSchemaAsync(string explorerConnection, CancellationToken cancellationToken);

        DataSourceSummaryFacts Facts(string sourceName);

        /// <summary>Writes the data source; null when done, else the error to show.</summary>
        Task<string?> FinishAsync(DataSourceWizardModel model);
    }

    /// <summary>
    /// Finish of the "Add Data Source" wizard (docs/DATA.md §9, DATA-6), in order: <c>kbdata.build</c> (the tool reads the live schema),
    /// <c>Cargo.toml</c> (the <c>data</c> feature, a <c>user-secrets-id</c>), <c>secrets.copyToProject</c> (<c>ConnectionStrings:&lt;name&gt;</c> into the
    /// project's store - never a project file), <c>src/data/&lt;name&gt;.kbdata</c>, <c>src/data/&lt;name&gt;.rs</c> (new sources only),
    /// <c>src/data/mod.rs</c>, <c>mod data;</c> in <c>main.rs</c>, the schema snapshot of SQL IntelliSense, then <c>sqlx.prepare</c> in the background
    /// (Task Status Center, Kubuno Output pane, Error List on failure). Every file edit is surgical and idempotent
    /// (<see cref="DataSourceCodeWriter"/>); a file with unsaved changes in an editor stops Finish before anything is written.
    /// </summary>
    internal sealed class DataSourceGenerator : IDataSourceWizardBackend
    {
        private static ErrorListProvider? _errors;
        private readonly DataSourceCrate _crate;
        private readonly string _root;
        private readonly DataSourcesSettings? _settings;
        private readonly Dictionary<string, string> _schemaJson = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public DataSourceGenerator(DataSourceCrate crate, string root, DataSourcesSettings? settings = null)
        {
            _crate = crate;
            _root = root;
            _settings = settings;
        }

        private string MainFile => File.Exists(Path.Combine(_crate.ManifestDirectory, "src", "main.rs")) || !File.Exists(Path.Combine(_crate.ManifestDirectory, "src", "lib.rs"))
            ? Path.Combine(_crate.ManifestDirectory, "src", "main.rs")
            : Path.Combine(_crate.ManifestDirectory, "src", "lib.rs");

        public async Task<string?> NewConnectionAsync(IReadOnlyCollection<string> existing)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var service = DataToolHost.Service;
            var dialog = new AddConnectionDialog(
                existing,
                DataOptionsPage.Current.DefaultCredentialStore,
                (target, token) => service.TestConnectionAsync(target, token),
                (name, provider, connectionString, store, token) => service.AddConnectionAsync(name, provider, connectionString, store, overwrite: false, token));
            return dialog.ShowModal() == true ? dialog.SavedConnection?.Name : null;
        }

        public Task<IReadOnlyList<ExplorerConnectionInfo>> ListConnectionsAsync() => DataToolHost.Service.ListConnectionsAsync(CancellationToken.None);

        public async Task<DatabaseSchemaInfo> LoadSchemaAsync(string explorerConnection, CancellationToken cancellationToken)
        {
            var parameters = new JsonObject { ["target"] = DataConnectionTarget.Explorer(explorerConnection).ToJson(), ["includeSystem"] = false };
            var result = await DataToolHost.Service.SendAsync("schema.load", parameters, cancellationToken).ConfigureAwait(false);
            lock (_schemaJson)
            {
                _schemaJson[explorerConnection] = result.GetRawText();
            }

            return DataToolJson.Parse<DatabaseSchemaInfo>(result);
        }

        public DataSourceSummaryFacts Facts(string sourceName)
        {
            string mainText = ReadOrEmpty(MainFile);
            bool hasData = File.Exists(MainFile) && DataSourceCodeWriter.DeclaresModule(mainText, "data") && mainText.Contains("set_user_secrets_id");
            bool modExists = File.Exists(Path.Combine(_crate.DataDirectory, "mod.rs"));
            bool needsFeature = false;
            bool needsId = false;
            try
            {
                var update = DataSourceCodeWriter.EnsureCargoManifest(ReadOrEmpty(_crate.ManifestPath), () => "00000000-0000-0000-0000-000000000000");
                needsFeature = update.AddedDataFeature;
                needsId = update.AddedUserSecretsId;
            }
            catch (Exception exception) when (exception is Kubuno.Rust.Cargo.Toml.TomlParseException || exception is InvalidOperationException || exception is NotSupportedException)
            {
                // Reported by Finish.
            }

            return new DataSourceSummaryFacts(hasData, modExists, needsFeature, needsId);
        }

        public async Task<string?> FinishAsync(DataSourceWizardModel model)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            string name = model.SourceName;
            string dataDirectory = _crate.DataDirectory;
            string kbdataPath = Path.Combine(dataDirectory, name + ".kbdata");
            string userPath = Path.Combine(dataDirectory, name + ".rs");
            string modPath = Path.Combine(dataDirectory, "mod.rs");
            string mainPath = MainFile;
            string explorer = model.ExplorerConnection!;

            // Nothing is written over unsaved editor changes.
            foreach (var file in new[] { _crate.ManifestPath, kbdataPath, modPath, mainPath })
            {
                if (IsDirtyInEditor(file))
                {
                    return DataSourcesText.DirtyFile(Path.GetFileName(file));
                }
            }

            // 1. The .kbdata text, from the live schema.
            var objects = new JsonArray();
            foreach (var o in model.SelectedObjects)
            {
                objects.Add(new JsonObject { ["schema"] = o.Schema, ["name"] = o.Name });
            }

            var build = new JsonObject
            {
                ["target"] = DataConnectionTarget.Explorer(explorer).ToJson(),
                ["name"] = name,
                ["connection"] = model.ConnectionName,
                ["objects"] = objects,
            };
            if (model.SchemaParameter is { } schema)
            {
                build["schema"] = schema;
            }

            string kbdataText;
            try
            {
                var built = await DataToolHost.Service.SendAsync("kbdata.build", build, CancellationToken.None);
                kbdataText = built.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String ? text.GetString()! : string.Empty;
            }
            catch (DataToolException exception)
            {
                return exception.Message;
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (kbdataText.Length == 0)
            {
                return "kbdata.build returned no text.";
            }

            // 2. Cargo.toml.
            CargoManifestUpdate cargo;
            try
            {
                cargo = DataSourceCodeWriter.EnsureCargoManifest(ReadOrEmpty(_crate.ManifestPath), () => Guid.NewGuid().ToString());
            }
            catch (Exception exception) when (exception is Kubuno.Rust.Cargo.Toml.TomlParseException || exception is InvalidOperationException || exception is NotSupportedException)
            {
                return "Cargo.toml: " + exception.Message;
            }

            // 3. The secret, copied from the Data Explorer's store into the project's (never into a file of the project).
            try
            {
                await DataToolHost.Service.CopySecretToProjectAsync(explorer, cargo.UserSecretsId, "ConnectionStrings:" + model.ConnectionName, model.Store, CancellationToken.None);
            }
            catch (DataToolException exception)
            {
                return exception.Message;
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var written = new List<string>();
            try
            {
                if (cargo.Changed)
                {
                    WriteText(_crate.ManifestPath, cargo.Text);
                    written.Add("Cargo.toml");
                }

                Directory.CreateDirectory(dataDirectory);
                string? existingKbdata = File.Exists(kbdataPath) ? ReadOrEmpty(kbdataPath) : null;
                WriteText(kbdataPath, DataSourceCodeWriter.KbdataText(existingKbdata, kbdataText));
                written.Add("src/data/" + name + ".kbdata");

                if (!File.Exists(userPath))
                {
                    // The row struct names, as the macro derives them.
                    IReadOnlyList<KeyValuePair<string, string>> rows;
                    try
                    {
                        var read = await DataToolHost.Service.SendAsync("kbdata.read", new JsonObject { ["path"] = kbdataPath }, CancellationToken.None);
                        rows = KbdataSourceInfo.Parse(kbdataPath, read).Tables.Select(t => new KeyValuePair<string, string>(t.Name, t.RowName)).ToList();
                    }
                    catch (Exception exception) when (exception is DataToolException || exception is FormatException)
                    {
                        rows = model.SelectedObjects.Select(o => new KeyValuePair<string, string>(o.Name, DataSourceNames.RowName(o.Name))).ToList();
                    }

                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    WriteText(userPath, DataSourceCodeWriter.UserSourceFile(name, name + ".kbdata", rows, cargo.DataCrate));
                    written.Add("src/data/" + name + ".rs");
                }

                string? mod = File.Exists(modPath) ? ReadOrEmpty(modPath) : null;
                string newMod = DataSourceCodeWriter.EnsureModDeclaration(mod, name);
                if (!string.Equals(mod, newMod, StringComparison.Ordinal))
                {
                    WriteText(modPath, newMod);
                    written.Add("src/data/mod.rs");
                }

                if (File.Exists(mainPath))
                {
                    string main = ReadOrEmpty(mainPath);
                    string newMain = DataSourceCodeWriter.EnsureUserSecretsRegistration(DataSourceCodeWriter.EnsureDataModule(main), cargo.DataCrate);
                    if (!string.Equals(main, newMain, StringComparison.Ordinal))
                    {
                        WriteText(mainPath, newMain);
                        written.Add("src/" + Path.GetFileName(mainPath));
                    }
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return exception.Message;
            }

            // 4. The schema snapshot of SQL IntelliSense (keyed by the application's connection name, which the .kbdata names).
            string? json;
            lock (_schemaJson)
            {
                _schemaJson.TryGetValue(explorer, out json);
            }

            if (json != null)
            {
                try
                {
                    SchemaSnapshotStore.Write(_root, model.ConnectionName, json);
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
                {
                    KubunoLog.WriteLine("Kubuno: the schema snapshot could not be written: " + exception.Message);
                }
            }

            // "Configure..." proposes the same Data Explorer connection next time.
            if (_settings != null)
            {
                try
                {
                    _settings.SetExplorerConnection(kbdataPath, explorer);
                    _settings.Save();
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                {
                    KubunoLog.WriteLine("Kubuno: could not save the Data Sources choices: " + exception.Message);
                }
            }

            KubunoLog.WriteLine("Kubuno: data source '" + name + "' (" + _crate.PackageName + "): " + string.Join(", ", written) + "; connection string ConnectionStrings:" + model.ConnectionName + " copied to " + DataText.StoreName(DataToolNames.Of(model.Store)) + ".");
            if (!cargo.HasKubunoDependency)
            {
                KubunoLog.WriteLine("Kubuno: " + DataSourcesText.NoKubunoDependency);
            }

            // 5. The offline query cache, in the background.
            StartSqlxPrepare(_crate, model.ConnectionName, DataToolNames.ParseProvider(model.Provider), kbdataPath);
            return null;
        }

        /// <summary>Runs <c>sqlx.prepare</c> for <paramref name="crate"/> in the background (never blocks the UI): Task Status Center, Kubuno Output pane, Error List on failure.</summary>
        internal static void StartSqlxPrepare(DataSourceCrate crate, string connection, DataProviderKind? provider, string kbdataPath)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var errors = ErrorList();
            errors.Tasks.Clear();
            if (provider == DataProviderKind.SqlServer)
            {
                KubunoLog.WriteLine("Kubuno: " + DataSourcesText.SqlServerNoSqlx);
                return;
            }

            string title = DataSourcesText.SqlxStarted(crate.PackageName);
            KubunoLog.WriteLine("Kubuno: " + title);
            KubunoLog.Activate();
            ITaskHandler? handler = null;
            if (ServiceProvider.GlobalProvider.GetService(typeof(SVsTaskStatusCenterService)) is IVsTaskStatusCenterService center)
            {
                var options = default(TaskHandlerOptions);
                options.Title = title;
                options.ActionsAfterCompletion = CompletionActions.None;
                var data = default(TaskProgressData);
                data.CanBeCanceled = true;
                handler = center.PreRegister(options, data);
            }

            var token = handler?.UserCancellation ?? CancellationToken.None;
            string? targetDirectory = Kubuno.Desktop.Migrations.RsprojTargetDirectory.Find(crate.ManifestDirectory);
            var work = Task.Run(async () =>
            {
                var parameters = new JsonObject
                {
                    ["manifestDir"] = crate.ManifestDirectory,
                    ["target"] = DataConnectionTarget.Project(crate.ManifestDirectory, connection, provider).ToJson(),
                };
                if (!string.IsNullOrWhiteSpace(targetDirectory))
                {
                    // The .rsproj's own CargoTargetDir (never the desktop workspace's).
                    parameters["targetDir"] = targetDirectory;
                }
                try
                {
                    var result = await DataToolHost.Service.SendAsync("sqlx.prepare", parameters, token).ConfigureAwait(false);
                    int files = result.TryGetProperty("queryFiles", out var q) && q.TryGetInt32(out var n) ? n : 0;
                    string output = result.TryGetProperty("output", out var o) && o.ValueKind == JsonValueKind.String ? o.GetString()! : string.Empty;
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    if (output.Length > 0)
                    {
                        KubunoLog.WriteLine(output.TrimEnd());
                    }

                    KubunoLog.WriteLine("Kubuno: " + DataSourcesText.SqlxDone(files));
                }
                catch (OperationCanceledException)
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    KubunoLog.WriteLine("Kubuno: sqlx.prepare cancelled.");
                }
                catch (DataToolException exception)
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    KubunoLog.WriteLine("Kubuno: " + DataSourcesText.SqlxFailed(exception.Message));
                    string first = exception.Message.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("error", StringComparison.OrdinalIgnoreCase)) ?? exception.Message.Split('\n')[0];
                    errors.Tasks.Add(new ErrorTask
                    {
                        Category = TaskCategory.BuildCompile,
                        ErrorCategory = TaskErrorCategory.Error,
                        Text = DataSourcesText.SqlxFailed(first),
                        Document = kbdataPath,
                        Line = 0,
                    });
                    errors.Show();
                }
            });
            handler?.RegisterTask(work);
            work.FileAndForget("Kubuno/DataSources/SqlxPrepare");
        }

        private static ErrorListProvider ErrorList()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return _errors ??= new ErrorListProvider(ServiceProvider.GlobalProvider) { ProviderName = "Kubuno Data Sources", ProviderGuid = new Guid("5d3f6a61-5c9e-4b7c-9b2e-2f1d6c7a9e41") };
        }

        private static bool IsDirtyInEditor(string path)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                if (ServiceProvider.GlobalProvider.GetService(typeof(EnvDTE.DTE)) is EnvDTE.DTE dte)
                {
                    foreach (EnvDTE.Document document in dte.Documents)
                    {
                        if (string.Equals(document.FullName, path, StringComparison.OrdinalIgnoreCase) && !document.Saved)
                        {
                            return true;
                        }
                    }
                }
            }
            catch (COMException)
            {
                return false;
            }

            return false;
        }

        private static string ReadOrEmpty(string path)
        {
            try
            {
                return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return string.Empty;
            }
        }

        /// <summary>Writes UTF-8, keeping a byte order mark the file already had.</summary>
        private static void WriteText(string path, string text)
        {
            bool bom = false;
            if (File.Exists(path))
            {
                var head = new byte[3];
                using (var stream = File.OpenRead(path))
                {
                    bom = stream.Read(head, 0, 3) == 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF;
                }
            }

            File.WriteAllText(path, text.TrimStart('﻿'), new UTF8Encoding(bom));
        }
    }
}
