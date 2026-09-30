using System;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Kubuno.VisualStudio.Core.Data;
using Kubuno.VisualStudio.Logging;
using Kubuno.VisualStudio.Options;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace Kubuno.VisualStudio.DataExplorer
{
    /// <summary>
    /// View &gt; "Explorateur de données" / "Data Explorer" (docs/DATA.md §9, DATA-5): the Server Explorer / SQL Server Object
    /// Explorer of Kubuno applications, backed by <c>kubuno-data-tool</c> (<see cref="DataToolHost"/>). Its toolbar and
    /// context menus are <c>KubunoCommands.vsct</c> menus whose commands this pane answers (its own command service), so
    /// they act on the tree's selection.
    /// </summary>
    [Guid(PackageGuidStrings.DataExplorerToolWindow)]
    public sealed class DataExplorerToolWindow : ToolWindowPane
    {
        private readonly DataExplorerControl _control = new DataExplorerControl();
        private OleMenuCommandService? _commands;
        private bool _loaded;

        public DataExplorerToolWindow()
            : base(null)
        {
            Caption = DataText.DataExplorer;
            BitmapImageMoniker = KnownMonikers.Database;
            Content = _control;
            ToolBar = new CommandID(PackageGuids.KubunoCommandSet, PackageIds.KubunoDataExplorerToolbar);
            _control.SelectionChanged += (_, _) => UpdateCommandUi();
            _control.ContextMenuRequested += (_, e) => ShowContextMenu(e);
            _control.ActivateRequested += (_, node) => DataUi.RunUi(() => ShowDataAsync(node), "ShowData");
            _control.DeleteRequested += (_, node) => DataUi.RunUi(() => DeleteAsync(node), "Delete");
            _control.Loaded += (_, _) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                if (!_loaded)
                {
                    _loaded = true;
                    DataToolHost.RefreshOptions();
                    DataUi.RunUi(() => _control.RefreshAllAsync(), "List");
                }
            };
        }

        internal DataExplorerControl Control => _control;

        protected override void Initialize()
        {
            base.Initialize();
            _commands = GetService(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (_commands is null)
            {
                KubunoLog.WriteLine("Kubuno: the Data Explorer has no command service; its toolbar is inactive.");
                return;
            }

            Add(PackageIds.DataAddConnectionCommand, () => DataText.AddConnectionCommand, _ => true, _ => true, _ => DataUi.RunUi(AddConnectionAsync, "AddConnection"));
            Add(PackageIds.DataRefreshCommand, () => DataText.RefreshCommand, _ => true, _ => true, node => DataUi.RunUi(() => RefreshAsync(node), "Refresh"));
            Add(PackageIds.DataNewQueryCommand, () => DataText.NewQueryCommand, _ => true, node => QueryConnection(node) != null, node => DataUi.RunUi(() => NewQueryAsync(node), "NewQuery"));
            Add(PackageIds.DataDeleteCommand, () => DataText.DeleteConnectionCommand, _ => true, node => node?.Kind == DataNodeKind.Connection, node => DataUi.RunUi(() => DeleteAsync(node!), "Delete"));
            Add(PackageIds.DataShowDataCommand, () => DataText.ShowDataCommand, node => node?.IsTableOrView == true, node => node?.IsTableOrView == true, node => DataUi.RunUi(() => ShowDataAsync(node!), "ShowData"));
            Add(PackageIds.DataCopyNameCommand, () => DataText.CopyNameCommand, node => node?.ObjectName != null || node?.Kind == DataNodeKind.Connection, _ => true, CopyName);
            foreach (ScriptKind kind in Enum.GetValues(typeof(ScriptKind)))
            {
                var scriptKind = kind;
                Add(PackageIds.DataScriptSelectCommand + (int)kind, () => DataText.ScriptCommand(scriptKind), node => node?.IsTableOrView == true, node => node?.IsTableOrView == true && (scriptKind == ScriptKind.Select || scriptKind == ScriptKind.Create || node.Kind == DataNodeKind.Table), node => DataUi.RunUi(() => GenerateScriptAsync(node!, scriptKind), "Script"));
            }
        }

        private void Add(int id, Func<string> text, Func<DataExplorerNode?, bool> visible, Func<DataExplorerNode?, bool> enabled, Action<DataExplorerNode?> run)
        {
#pragma warning disable VSTHRD010 // OleMenuCommand handlers run on the UI thread.
            var command = new OleMenuCommand((_, _) =>
            {
                try
                {
                    run(_control.SelectedNode);
                }
                catch (Exception exception)
                {
                    KubunoLog.WriteException("Kubuno: Data Explorer command failed", exception);
                }
            }, new CommandID(PackageGuids.KubunoCommandSet, id));
            command.BeforeQueryStatus += (s, _) =>
            {
                var menuCommand = (OleMenuCommand)s!;
                var node = _control.SelectedNode;
                menuCommand.Text = text();
                menuCommand.Visible = visible(node);
                menuCommand.Enabled = menuCommand.Visible && enabled(node);
            };
#pragma warning restore VSTHRD010
            _commands!.AddCommand(command);
        }

        private void UpdateCommandUi()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (ServiceProvider.GlobalProvider.GetService(typeof(SVsUIShell)) is IVsUIShell shell)
            {
                shell.UpdateCommandUI(0);
            }
        }

        private void ShowContextMenu(DataContextMenuEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (e.Node is null || _commands is null || ServiceProvider.GlobalProvider.GetService(typeof(SVsUIShell)) is not IVsUIShell shell)
            {
                return;
            }

            int menu = e.Node.Kind == DataNodeKind.Connection ? PackageIds.KubunoDataConnectionContextMenu
                : e.Node.IsTableOrView ? PackageIds.KubunoDataTableContextMenu
                : PackageIds.KubunoDataNodeContextMenu;
            var group = PackageGuids.KubunoCommandSet;
            var points = new[] { new POINTS { x = (short)e.ScreenPoint.X, y = (short)e.ScreenPoint.Y } };
            shell.ShowContextMenu(0, ref group, menu, points, _commands);
        }

        /// <summary>The connection a "New Query" on <paramref name="node"/> opens (the only one when nothing is selected).</summary>
        private string? QueryConnection(DataExplorerNode? node)
        {
            if (node != null)
            {
                return node.Connection;
            }

            var names = _control.ConnectionNames;
            return names.Count == 1 ? System.Linq.Enumerable.First(names) : null;
        }

        private async Task AddConnectionAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var service = DataToolHost.Service;
            var dialog = new AddConnectionDialog(
                _control.ConnectionNames,
                DataOptionsPage.Current.DefaultCredentialStore,
                (target, token) => service.TestConnectionAsync(target, token),
                (name, provider, connectionString, store, token) => service.AddConnectionAsync(name, provider, connectionString, store, overwrite: false, token));
            if (dialog.ShowModal() == true && dialog.SavedConnection is { } saved)
            {
                KubunoLog.WriteLine($"Kubuno: Data Explorer connection '{saved.Name}' added ({saved.Provider}, {DataText.StoreName(saved.Store)}).");
                await _control.RefreshAllAsync(select: saved.Name);
            }
        }

        private async Task RefreshAsync(DataExplorerNode? node)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            DataToolHost.RefreshOptions();
            if (node is null)
            {
                await _control.RefreshAllAsync();
            }
            else
            {
                await _control.RefreshConnectionAsync(node.Connection);
            }
        }

        private async Task NewQueryAsync(DataExplorerNode? node)
        {
            string? connection = QueryConnection(node);
            if (connection is null)
            {
                return;
            }

            await QueryToolWindow.ShowAsync(connection, _control.ConnectionInfo(connection)?.ProviderKind, string.Empty);
        }

        private async Task ShowDataAsync(DataExplorerNode node)
        {
            if (!node.IsTableOrView || node.ObjectName is null)
            {
                return;
            }

            string schema = node.Schema ?? string.Empty;
            string table = node.ObjectName;
            await QueryToolWindow.ShowAsync(
                node.Connection,
                _control.ConnectionInfo(node.Connection)?.ProviderKind,
                string.Empty,
                caption: DataText.DataCaption(table) + " (" + node.Connection + ")",
                afterShow: control => control.ShowDataAsync(schema, table));
        }

        private async Task GenerateScriptAsync(DataExplorerNode node, ScriptKind kind)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (node.ObjectName is null)
            {
                return;
            }

            string sql;
            try
            {
                sql = await DataToolHost.Service.GenerateScriptAsync(DataConnectionTarget.Explorer(node.Connection), node.Schema ?? string.Empty, node.ObjectName, kind, CancellationToken.None);
            }
            catch (Exception exception)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                DataUi.ShowError(exception.Message);
                return;
            }

            await QueryToolWindow.ShowAsync(node.Connection, _control.ConnectionInfo(node.Connection)?.ProviderKind, sql);
        }

        private async Task DeleteAsync(DataExplorerNode node)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (node.Kind != DataNodeKind.Connection || !DataUi.Confirm(DataText.ConfirmDelete(node.Connection)))
            {
                return;
            }

            try
            {
                await DataToolHost.Service.RemoveConnectionAsync(node.Connection, CancellationToken.None);
                KubunoLog.WriteLine($"Kubuno: Data Explorer connection '{node.Connection}' removed.");
            }
            catch (Exception exception)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                DataUi.ShowError(exception.Message);
            }

            await _control.RefreshAllAsync();
        }

        private static void CopyName(DataExplorerNode? node)
        {
            string? name = node?.Kind == DataNodeKind.Connection ? node.Connection : node?.ObjectName;
            if (!string.IsNullOrEmpty(name))
            {
                Clipboard.SetText(name);
            }
        }

        /// <summary>Shows (creating if needed) the Data Explorer.</summary>
        internal static async Task<DataExplorerToolWindow?> ShowAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var package = KubunoPackage.Instance;
            if (package is null)
            {
                return null;
            }

            try
            {
                if (await package.FindToolWindowAsync(typeof(DataExplorerToolWindow), 0, create: true, package.DisposalToken) is DataExplorerToolWindow window && window.Frame is IVsWindowFrame frame)
                {
                    ErrorHandler.ThrowOnFailure(frame.Show());
                    return window;
                }
            }
            catch (Exception exception)
            {
                KubunoLog.WriteException("Kubuno: failed to show the Data Explorer", exception);
            }

            return null;
        }
    }
}
