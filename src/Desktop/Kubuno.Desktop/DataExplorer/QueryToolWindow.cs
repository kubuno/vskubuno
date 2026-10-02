using System;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Desktop.Logic.Data;
using Kubuno.Shared.Logging;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace Kubuno.Desktop.DataExplorer
{
    /// <summary>
    /// A query window of the Data Explorer ("Requête – Shop"): a multi-instance, transient tool window in the document
    /// well, like SQL Server Object Explorer's query editor. Its toolbar (<c>KubunoQueryToolbar</c>) has Execute and
    /// Cancel; F5 and Ctrl+Shift+E execute (the pane answers the standard Debug.Start command while it is active, and
    /// Ctrl+Shift+E is bound in the query window's own key binding scope).
    /// </summary>
    [Guid(PackageGuidStrings.QueryToolWindow)]
    public sealed class QueryToolWindow : ToolWindowPane
    {
        private static int _nextId;
        private readonly QueryControl _control = new QueryControl();
        private OleMenuCommand? _execute;
        private OleMenuCommand? _cancel;

        public QueryToolWindow()
            : base(null)
        {
            Caption = DataText.QueryCaption(string.Empty);
            Content = _control;
            ToolBar = new CommandID(PackageGuids.KubunoCommandSet, PackageIds.KubunoQueryToolbar);
            _control.RunningChanged += (_, _) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                RefreshCommands();
            };
        }

        internal QueryControl Control => _control;

        protected override void Initialize()
        {
            base.Initialize();
            if (GetService(typeof(IMenuCommandService)) is OleMenuCommandService commands)
            {
#pragma warning disable VSTHRD010 // OleMenuCommand handlers run on the UI thread.
                _execute = new OleMenuCommand((_, _) => DataUi.RunUi(_control.ExecuteAsync, "Execute"), new CommandID(PackageGuids.KubunoCommandSet, PackageIds.QueryExecuteCommand));
                _execute.BeforeQueryStatus += (s, _) =>
                {
                    var command = (OleMenuCommand)s!;
                    command.Text = DataText.ExecuteCommand;
                    command.Enabled = !_control.IsRunning;
                };
                commands.AddCommand(_execute);

                _cancel = new OleMenuCommand((_, _) => _control.Cancel(), new CommandID(PackageGuids.KubunoCommandSet, PackageIds.QueryCancelCommand));
                _cancel.BeforeQueryStatus += (s, _) =>
                {
                    var command = (OleMenuCommand)s!;
                    command.Text = DataText.CancelQueryCommand;
                    command.Enabled = _control.IsRunning;
                };
                commands.AddCommand(_cancel);

                // F5 (Debug.Start) while the query window is active: execute, like SQL Server's query editor.
                var start = new OleMenuCommand((_, _) => DataUi.RunUi(_control.ExecuteAsync, "Execute"), new CommandID(VSConstants.GUID_VSStandardCommandSet97, (int)VSConstants.VSStd97CmdID.Start));
                start.BeforeQueryStatus += (s, _) => ((OleMenuCommand)s!).Enabled = !_control.IsRunning;
                commands.AddCommand(start);
#pragma warning restore VSTHRD010
            }
        }

        /// <summary>
        /// Opens a new query window on <paramref name="connection"/> with <paramref name="sql"/> in its editor, then runs
        /// <paramref name="afterShow"/> (e.g. "Show Table Data" or nothing for a script that must not run).
        /// </summary>
        internal static async Task<QueryToolWindow?> ShowAsync(string connection, DataProviderKind? provider, string sql, string? caption = null, Func<QueryControl, Task>? afterShow = null)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var package = Kubuno.Shared.KubunoHost.Package;
            if (package is null)
            {
                return null;
            }

            try
            {
                int id = Interlocked.Increment(ref _nextId);
                if (await package.FindToolWindowAsync(typeof(QueryToolWindow), id, create: true, package.DisposalToken) is not QueryToolWindow window || window.Frame is not IVsWindowFrame frame)
                {
                    KubunoLog.WriteLine("Kubuno: could not create a query window.");
                    return null;
                }

                window.Caption = caption ?? DataText.QueryCaption(connection);
                window._control.Initialize(connection, provider, sql);
                ErrorHandler.ThrowOnFailure(frame.Show());
                window._control.FocusEditor();
                if (afterShow != null)
                {
                    await afterShow(window._control);
                }

                return window;
            }
            catch (Exception exception)
            {
                KubunoLog.WriteException("Kubuno: failed to open a query window", exception);
                return null;
            }
        }

        private void RefreshCommands()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (ServiceProvider.GlobalProvider.GetService(typeof(SVsUIShell)) is IVsUIShell shell)
            {
                shell.UpdateCommandUI(0);
            }
        }

        public override void OnToolWindowCreated()
        {
            base.OnToolWindowCreated();
            ThreadHelper.ThrowIfNotOnUIThread();

            // The key binding scope of the query windows (Ctrl+Shift+E, F5, Alt+Break: KubunoCommands.vsct KeyBindings).
            if (Frame is IVsWindowFrame frame)
            {
                var scope = new Guid(PackageGuidStrings.QueryToolWindow);
                frame.SetGuidProperty((int)__VSFPROPID.VSFPROPID_CmdUIGuid, ref scope);
            }
        }

        protected override void OnClose()
        {
            _control.Cancel();
            base.OnClose();
        }
    }
}
