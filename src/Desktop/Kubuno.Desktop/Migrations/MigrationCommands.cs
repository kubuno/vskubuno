using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.VisualStudio.Commands;
using Kubuno.VisualStudio.Core.Data;
using Kubuno.VisualStudio.Core.Migrations;
using Kubuno.VisualStudio.DataExplorer;
using Kubuno.VisualStudio.Logging;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace Kubuno.VisualStudio.Migrations
{
    /// <summary>
    /// The "Database" submenu of a <c>.rsproj</c> project node (docs/DATA.md DATA-7) - Add Migration..., Apply Migrations,
    /// Revert Last Migration, Update SQLx Cache, Refresh Status - and the operations behind it, shared with the context
    /// menus of the <c>Migrations</c> node (<see cref="MigrationsTreeProvider"/>). Everything goes through
    /// <c>kubuno-data-tool</c> (<c>migrate.*</c>, <c>sqlx.*</c>) with the crate's project connection
    /// (<c>{"project": {"manifestDir", "connection"}}</c>): the connection string is resolved by the helper from the crate's
    /// secrets chain and never reaches Visual Studio. Results go to the "Kubuno" Output pane and the status bar, errors
    /// also to a message box.
    /// </summary>
    internal static class MigrationCommands
    {
        /// <summary>The connection chosen for a crate in this session (crate directory → name), when its data sources do not say.</summary>
        private static readonly ConcurrentDictionary<string, string> Remembered = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static MigrationService Service => new MigrationService(DataToolHost.Service);

        public static void Initialize(OleMenuCommandService commandService)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            AddMenu(commandService, PackageIds.KubunoDatabaseProjectMenu, () => MigrationText.DatabaseMenu);
            Add(commandService, PackageIds.MigrationAddCommand, () => MigrationText.AddMigrationCommand, AddMigrationAsync);
            Add(commandService, PackageIds.MigrationRunCommand, () => MigrationText.RunMigrationsCommand, RunAsync);
            Add(commandService, PackageIds.MigrationRevertCommand, () => MigrationText.RevertMigrationCommand, RevertAsync);
            Add(commandService, PackageIds.SqlxPrepareCommand, () => MigrationText.PrepareSqlxCommand, PrepareAsync);
            Add(commandService, PackageIds.MigrationRefreshCommand, () => MigrationText.RefreshStatusCommand, RefreshAsync);
            SqlxStaleInfoBar.Initialize();
        }

        /// <summary>The connection of a crate without asking (the remembered one, or its data sources' only one); else why not.</summary>
        public static string? ConnectionWithoutAsking(CrateDataInfo info, out string? whyNot)
        {
            Remembered.TryGetValue(info.CrateDirectory, out var remembered);
            var choice = info.Choose(remembered);
            whyNot = choice.Kind switch
            {
                ConnectionChoiceKind.Several => MigrationText.SeveralConnections(string.Join(", ", choice.Candidates)),
                ConnectionChoiceKind.None => MigrationText.NoConnection,
                _ => null,
            };
            return choice.Connection;
        }

        public static Task AddMigrationAsync(string crateDirectory) => GuardAsync(MigrationText.AddMigrationCommand, async () =>
        {
            var info = await Task.Run(() => CrateDataInfo.Read(crateDirectory));
            var connection = ConnectionWithoutAsking(info, out _);
            var schema = info.SchemaFor(connection);
            bool first = !Directory.Exists(info.MigrationsDirectory) || !Directory.EnumerateFiles(info.MigrationsDirectory, "*.sql").Any();

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var dialog = new AddMigrationDialog(info.DisplayName, first ? schema : null);
            if (dialog.ShowModal() != true)
            {
                return;
            }

            var files = await Service.AddAsync(info.MigrationsDirectory, dialog.Description, dialog.Reversible, schema, CancellationToken.None);
            // rustc cannot track a folder: a NEW migration file does not re-expand `data_source!`, whose
            // stale-cache warning compares the migrations with the .sqlx cache. Touching the crate's
            // .kbdata files (their content is unchanged) makes the next build re-expand it.
            await Task.Run(() => TouchKbdataFiles(crateDirectory));
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            foreach (var file in files)
            {
                Log(info, MigrationText.Added(file));
            }

            if (files.Count > 0)
            {
                VsShellUtilities.OpenDocument(ServiceProvider.GlobalProvider, files[0]);
                StatusBar(MigrationText.Added(Path.GetFileName(files[0])));
            }

            MigrationsSource.ReloadFor(info.CrateDirectory);
        });

        private static void TouchKbdataFiles(string crateDirectory)
        {
            foreach (var file in CrateDataInfo.KbdataFiles(crateDirectory))
            {
                try
                {
                    File.SetLastWriteTimeUtc(file, DateTime.UtcNow);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Best effort: the warning then shows at the next build that re-expands the macro anyway.
                }
            }
        }

        public static Task RunAsync(string crateDirectory) => GuardAsync(MigrationText.RunMigrationsCommand, async () =>
        {
            var (info, connection) = await ResolveAsync(crateDirectory);
            if (connection is null)
            {
                return;
            }

            if (!Directory.Exists(info.MigrationsDirectory))
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                Report(info, MigrationText.NoMigrations);
                return;
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            StatusBar(MigrationText.RunMigrationsCommand + "...");
            try
            {
                var applied = await Service.RunAsync(info.TargetFor(connection), info.MigrationsDirectory, CancellationToken.None);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                Report(info, applied.Count == 0 ? MigrationText.AppliedNone : MigrationText.AppliedSome(applied.Count, string.Join(", ", applied)));
            }
            finally
            {
                MigrationsSource.ReloadFor(info.CrateDirectory);
            }
        });

        public static Task RevertAsync(string crateDirectory) => GuardAsync(MigrationText.RevertMigrationCommand, async () =>
        {
            var (info, connection) = await ResolveAsync(crateDirectory);
            if (connection is null)
            {
                return;
            }

            if (!Directory.Exists(info.MigrationsDirectory))
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                Report(info, MigrationText.RevertedNone);
                return;
            }

            var target = info.TargetFor(connection);
            MigrationInfo? last = null;
            try
            {
                last = (await Service.StatusAsync(target, info.MigrationsDirectory, CancellationToken.None)).LastApplied;
            }
            catch (DataToolException)
            {
                // The revert itself reports the problem.
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var question = last != null ? MigrationText.ConfirmRevert(last.Label, connection) : MigrationText.ConfirmRevertUnknown(connection);
            if (VsShellUtilities.ShowMessageBox(ServiceProvider.GlobalProvider, question, MigrationText.RevertMigrationCommand, OLEMSGICON.OLEMSGICON_QUERY, OLEMSGBUTTON.OLEMSGBUTTON_YESNO, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_SECOND) != 6)
            {
                return;
            }

            try
            {
                var reverted = await Service.RevertAsync(target, info.MigrationsDirectory, CancellationToken.None);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                Report(info, reverted is { } version
                    ? MigrationText.Reverted(last != null && last.Version == version ? last.Label : version.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    : MigrationText.RevertedNone);
            }
            finally
            {
                MigrationsSource.ReloadFor(info.CrateDirectory);
            }
        });

        public static Task PrepareAsync(string crateDirectory) => GuardAsync(MigrationText.PrepareSqlxCommand, async () =>
        {
            var (info, connection) = await ResolveAsync(crateDirectory);
            if (connection is null)
            {
                return;
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var factory = ServiceProvider.GlobalProvider.GetService(typeof(SVsThreadedWaitDialogFactory)) as IVsThreadedWaitDialogFactory;
            var message = MigrationText.PrepareMessage(info.DisplayName);
            Log(info, message);
            KubunoLog.Activate();
            using var session = factory?.StartWaitDialog(MigrationText.PrepareCaption, new ThreadedWaitDialogProgressData(message, progressText: null, statusBarText: message, isCancelable: true), TimeSpan.FromMilliseconds(300));
            var token = session?.UserCancellationToken ?? CancellationToken.None;
            try
            {
                var result = await Service.SqlxPrepareAsync(info.CrateDirectory, info.TargetFor(connection), token, RsprojTargetDirectory.Find(info.CrateDirectory));
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (!string.IsNullOrWhiteSpace(result.Output))
                {
                    KubunoLog.WriteLine(result.Output.TrimEnd());
                }

                Report(info, MigrationText.Prepared(result.QueryFiles));
            }
            catch (OperationCanceledException)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                Report(info, MigrationText.PrepareCancelled);
            }
            finally
            {
                MigrationsSource.ReloadFor(info.CrateDirectory);
            }
        });

        /// <summary>Refresh Status: asks for the connection when the crate does not say which one, then re-reads the state.</summary>
        public static Task RefreshAsync(string crateDirectory) => GuardAsync(MigrationText.RefreshStatusCommand, async () =>
        {
            var (info, _) = await ResolveAsync(crateDirectory);
            MigrationsSource.ReloadFor(info.CrateDirectory);
        });

        /// <summary>The crate and its migration connection, asking the developer when needed (null: cancelled).</summary>
        private static async Task<(CrateDataInfo Info, string? Connection)> ResolveAsync(string crateDirectory)
        {
            var info = await Task.Run(() => CrateDataInfo.Read(crateDirectory));
            Remembered.TryGetValue(info.CrateDirectory, out var remembered);
            var choice = info.Choose(remembered);
            if (choice.Kind == ConnectionChoiceKind.Single)
            {
                return (info, choice.Connection);
            }

            // Offer the names the crate's user secrets define too (key names only).
            IReadOnlyList<string> secretNames = Array.Empty<string>();
            if (info.UserSecretsId != null)
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                secretNames = await Task.Run(() => CrateDataInfo.UserSecretsConnectionNames(CrateDataInfo.UserSecretsPath(appData, info.UserSecretsId)));
            }

            var candidates = choice.Candidates.Concat(secretNames.Where(n => !choice.Candidates.Contains(n, StringComparer.OrdinalIgnoreCase))).ToList();
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var intro = choice.Kind == ConnectionChoiceKind.Several ? MigrationText.ChooseConnectionIntro(info.DisplayName) : MigrationText.ConnectionDialogIntro(info.DisplayName);
            var service = Service;
            var dialog = new ConnectionNameDialog(
                intro,
                candidates,
                candidates.FirstOrDefault(),
                info.UserSecretsId,
                service.ListExplorerConnectionsAsync,
                (explorer, connection, token) => service.CopyToUserSecretsAsync(explorer, info.UserSecretsId!, connection, token));
            if (dialog.ShowModal() != true)
            {
                return (info, null);
            }

            Remembered[info.CrateDirectory] = dialog.ConnectionName;
            Log(info, MigrationText.T($"connection: {dialog.ConnectionName}", $"connexion : {dialog.ConnectionName}"));
            return (info, dialog.ConnectionName);
        }

        /// <summary>Runs an operation; a helper error becomes a message box (and a line in the Output pane).</summary>
        private static async Task GuardAsync(string what, Func<Task> work)
        {
            try
            {
                await work();
            }
            catch (DataToolException exception)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                var message = exception.Kind == DataToolErrorKinds.Config && exception.Message.Contains("sqlx") ? MigrationText.NotAMigrationsTarget + " " + exception.Message : exception.Message;
                KubunoLog.WriteLine("Kubuno: " + MigrationText.Failed(what, message));
                StatusBar(MigrationText.Failed(what, FirstLine(message)));
                VsShellUtilities.ShowMessageBox(ServiceProvider.GlobalProvider, MigrationText.Failed(what, message), what, OLEMSGICON.OLEMSGICON_CRITICAL, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            }
            catch (OperationCanceledException)
            {
                // Cancelled by the developer.
            }
            catch (Exception exception)
            {
                KubunoLog.WriteException("Kubuno: " + what, exception);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                VsShellUtilities.ShowMessageBox(ServiceProvider.GlobalProvider, MigrationText.Failed(what, exception.Message) + " " + MigrationText.SeeOutput, what, OLEMSGICON.OLEMSGICON_CRITICAL, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            }
        }

        private static void Report(CrateDataInfo info, string message)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Log(info, message);
            StatusBar(message);
        }

        private static void Log(CrateDataInfo info, string message) => KubunoLog.WriteLine($"Kubuno migrations ({info.DisplayName}): {message}");

        private static void StatusBar(string text)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (ServiceProvider.GlobalProvider.GetService(typeof(SVsStatusbar)) is IVsStatusbar statusBar)
            {
                statusBar.SetText(text);
            }
        }

        private static string FirstLine(string text)
        {
            var line = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? text;
            return line.Length > 160 ? line.Substring(0, 160) + "..." : line;
        }

        /// <summary>Runs an operation from a command handler (faults are reported, never thrown into the shell).</summary>
        public static void Start(Func<Task> work, string name)
        {
#pragma warning disable VSSDK007 // fire-and-forget from a command handler; FileAndForget reports faults.
            ThreadHelper.JoinableTaskFactory.RunAsync(work).FileAndForget("Kubuno/Migrations/" + name);
#pragma warning restore VSSDK007
        }

        private static void AddMenu(OleMenuCommandService commandService, int id, Func<string> text)
        {
            var command = new OleMenuCommand((_, _) => { }, new CommandID(PackageGuids.KubunoCommandSet, id));
#pragma warning disable VSTHRD010 // OleMenuCommand query events fire on the UI thread.
            command.BeforeQueryStatus += (sender, _) =>
            {
                var menu = (OleMenuCommand)sender!;
                menu.Text = text();
                menu.Visible = menu.Enabled = RsprojSelection.TryGetCurrent() is not null;
            };
#pragma warning restore VSTHRD010
            commandService.AddCommand(command);
        }

        private static void Add(OleMenuCommandService commandService, int id, Func<string> text, Func<string, Task> run)
        {
#pragma warning disable VSTHRD010 // OleMenuCommand invoke/query events fire on the UI thread.
            var command = new OleMenuCommand((_, _) =>
            {
                var context = RsprojSelection.TryGetCurrent();
                if (context != null)
                {
                    var directory = context.ProjectDirectory;
                    Start(() => run(directory), id.ToString("x4", System.Globalization.CultureInfo.InvariantCulture));
                }
            }, new CommandID(PackageGuids.KubunoCommandSet, id));
            command.BeforeQueryStatus += (sender, _) =>
            {
                var menuCommand = (OleMenuCommand)sender!;
                menuCommand.Text = text();
                menuCommand.Visible = menuCommand.Enabled = RsprojSelection.TryGetCurrent() is not null;
            };
#pragma warning restore VSTHRD010
            commandService.AddCommand(command);
        }
    }
}
