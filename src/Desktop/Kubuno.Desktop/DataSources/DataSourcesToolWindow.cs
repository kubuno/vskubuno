using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Kubuno.Desktop.Logic.Data;
using Kubuno.Desktop.Logic.DataSources;
using Kubuno.Desktop.Logic.Sql;
using Kubuno.Desktop.DataExplorer;
using Kubuno.Views.Designer.DesignSurface;
using Kubuno.Shared.Logging;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Workspace.VSIntegration.Contracts;

namespace Kubuno.Desktop.DataSources
{
    /// <summary>
    /// View &gt; Other Windows &gt; "Sources de données" / "Data Sources" (docs/DATA.md §9, DATA-6): the typed data sources
    /// (<c>src/**/*.kbdata</c>) of the Rust crate of the active document or of the Solution Explorer selection, read with
    /// <c>kbdata.read</c>. Its toolbar adds a source (the wizard), reconfigures one, refreshes and opens the <c>.kbdata</c>; each
    /// table / column carries its drop choice (grid or details, the control of a column), kept in <c>.vs\kubuno\datasources.json</c>.
    /// Tables and columns are dragged onto an open <c>.kbview</c> designer (<see cref="ExternalDesignerDrop"/>).
    /// </summary>
    [Guid(PackageGuidStrings.DataSourcesToolWindow)]
    public sealed class DataSourcesToolWindow : ToolWindowPane, IVsSelectionEvents
    {
        /// <summary>The OLE clipboard format of a dragged table / column (JSON: kbdata path, table, column) - informative for other targets.</summary>
        public const string DragFormat = "Kubuno.DataSources.Item";

        private readonly DataSourcesControl _control = new DataSourcesControl();
        private OleMenuCommandService? _commands;
        private uint _selectionCookie;
        private DataSourceCrate? _crate;
        private string? _root;
        private DataSourcesSettings? _settings;
        private FileSystemWatcher? _watcher;
        private System.Windows.Threading.DispatcherTimer? _reloadTimer;
        private int _loadVersion;
        private IReadOnlyList<KbdataSourceInfo> _sources = Array.Empty<KbdataSourceInfo>();

        public DataSourcesToolWindow()
            : base(null)
        {
            Caption = DataSourcesText.DataSources;
            BitmapImageMoniker = KnownMonikers.DataSourceView;
            Content = _control;
            ToolBar = new CommandID(PackageGuids.KubunoCommandSet, PackageIds.KubunoDataSourcesToolbar);
            _control.SelectionChanged += (_, _) => UpdateCommandUi();
            _control.ContextMenuRequested += (_, e) => ShowContextMenu(e);
            _control.ActivateRequested += (_, node) => InsertIntoActiveView(node);
            _control.DragRequested += (_, node) => StartDrag(node);
            _control.Loaded += (_, _) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                DataUi.RunUi(() => ResolveAndLoadAsync(force: _crate is null), "DataSources/Load");
            };
        }

        /// <summary>The window's tree (automation, tests).</summary>
        internal DataSourcesControl Control => _control;

        /// <summary>The crate shown (null when none).</summary>
        internal DataSourceCrate? Crate => _crate;

        protected override void Initialize()
        {
            base.Initialize();
            ThreadHelper.ThrowIfNotOnUIThread();
            _commands = GetService(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (_commands is null)
            {
                KubunoLog.WriteLine("Kubuno: the Data Sources window has no command service; its toolbar is inactive.");
            }
            else
            {
                Add(PackageIds.DataSourcesAddCommand, () => DataSourcesText.AddDataSourceCommand, _ => true, _ => _crate != null, _ => DataUi.RunUi(() => RunWizardAsync(null), "DataSources/Add"));
                Add(PackageIds.DataSourcesConfigureCommand, () => DataSourcesText.ConfigureCommand, _ => true, n => n?.Source != null, n => DataUi.RunUi(() => RunWizardAsync(n!.Source), "DataSources/Configure"));
                Add(PackageIds.DataSourcesRefreshCommand, () => DataSourcesText.RefreshCommand, _ => true, _ => true, _ => DataUi.RunUi(() => ResolveAndLoadAsync(force: true), "DataSources/Refresh"));
                Add(PackageIds.DataSourcesEditFileCommand, () => DataSourcesText.EditFileCommand, _ => true, n => n != null, n => OpenKbdata(n!));
                Add(PackageIds.DataSourcesInsertCommand, () => DataSourcesText.InsertCommand, n => n?.IsDraggable == true, n => n?.IsDraggable == true, n => InsertIntoActiveView(n!));
                Add(PackageIds.DataSourcesModeGridCommand, () => DataSourcesText.GridMode, n => n?.Kind == DataSourceNodeKind.Table, _ => true, n => SetMode(n!, DataTableDropMode.Grid), n => ModeOf(n) == DataTableDropMode.Grid);
                Add(PackageIds.DataSourcesModeDetailsCommand, () => DataSourcesText.DetailsMode, n => n?.Kind == DataSourceNodeKind.Table, _ => true, n => SetMode(n!, DataTableDropMode.Details), n => ModeOf(n) == DataTableDropMode.Details);
                foreach (DataControlKind kind in Enum.GetValues(typeof(DataControlKind)))
                {
                    var control = kind;
                    Add(
                        PackageIds.DataSourcesControlTextFieldCommand + (int)control,
                        () => DataSourcesText.ControlName(control),
                        n => n?.Kind == DataSourceNodeKind.Column && DataColumnKinds.ControlsFor(n.Column!.Kind).Contains(control),
                        _ => true,
                        n => SetControl(n!, control),
                        n => n?.Kind == DataSourceNodeKind.Column && _control.ControlOf(n) == control);
                }
            }

            if (GetService(typeof(SVsShellMonitorSelection)) is IVsMonitorSelection monitor)
            {
                monitor.AdviseSelectionEvents(this, out _selectionCookie);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Dispose(true) comes from the shell on the UI thread.
#pragma warning disable VSTHRD010
                if (_selectionCookie != 0 && GetService(typeof(SVsShellMonitorSelection)) is IVsMonitorSelection monitor)
                {
                    monitor.UnadviseSelectionEvents(_selectionCookie);
                    _selectionCookie = 0;
                }

                _watcher?.Dispose();
                _watcher = null;
                _reloadTimer?.Stop();
#pragma warning restore VSTHRD010
            }

            base.Dispose(disposing);
        }

        // ---- commands ----

        private void Add(int id, Func<string> text, Func<DataSourceNode?, bool> visible, Func<DataSourceNode?, bool> enabled, Action<DataSourceNode?> run, Func<DataSourceNode?, bool>? isChecked = null)
        {
#pragma warning disable VSTHRD010 // OleMenuCommand handlers run on the UI thread.
            var command = new OleMenuCommand((_, _) =>
            {
                try
                {
                    run(_menuNode ?? _control.SelectedNode);
                }
                catch (Exception exception)
                {
                    KubunoLog.WriteException("Kubuno: Data Sources command failed", exception);
                }
            }, new CommandID(PackageGuids.KubunoCommandSet, id));
            command.BeforeQueryStatus += (s, _) =>
            {
                var menuCommand = (OleMenuCommand)s!;
                var node = _menuNode ?? _control.SelectedNode;
                menuCommand.Text = text();
                menuCommand.Visible = visible(node);
                menuCommand.Enabled = menuCommand.Visible && enabled(node);
                menuCommand.Checked = isChecked?.Invoke(node) == true;
            };
#pragma warning restore VSTHRD010
            _commands!.AddCommand(command);
        }

        private DataSourceNode? _menuNode;

        private void ShowContextMenu(DataSourcesMenuEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_commands is null || ServiceProvider.GlobalProvider.GetService(typeof(SVsUIShell)) is not IVsUIShell shell)
            {
                return;
            }

            var group = PackageGuids.KubunoCommandSet;
            var points = new[] { new POINTS { x = (short)e.ScreenPoint.X, y = (short)e.ScreenPoint.Y } };
            _menuNode = e.Node;
            try
            {
                shell.ShowContextMenu(0, ref group, PackageIds.KubunoDataSourcesContextMenu, points, _commands);
            }
            finally
            {
                _menuNode = null;
            }
        }

        private void UpdateCommandUi()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (ServiceProvider.GlobalProvider.GetService(typeof(SVsUIShell)) is IVsUIShell shell)
            {
                shell.UpdateCommandUI(0);
            }
        }

        private DataTableDropMode? ModeOf(DataSourceNode? node) =>
            node?.Kind == DataSourceNodeKind.Table && _settings != null ? _settings.ModeOf(node.KbdataPath, node.Table!.Name) : (DataTableDropMode?)null;

        private void SetMode(DataSourceNode node, DataTableDropMode mode)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_settings is null || node.Table is null)
            {
                return;
            }

            _settings.SetMode(node.KbdataPath, node.Table.Name, mode);
            SaveSettings();
        }

        private void SetControl(DataSourceNode node, DataControlKind kind)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_settings is null || node.Table is null || node.Column is null)
            {
                return;
            }

            _settings.SetControl(node.KbdataPath, node.Table.Name, node.Column.Name, kind);
            SaveSettings();
        }

        private void SaveSettings()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                _settings?.Save();
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                KubunoLog.WriteLine("Kubuno: could not save the Data Sources choices: " + exception.Message);
            }

            _control.RefreshIcons();
        }

        private void OpenKbdata(DataSourceNode node)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (File.Exists(node.KbdataPath))
            {
                VsShellUtilities.OpenDocument(ServiceProvider.GlobalProvider, node.KbdataPath);
            }
        }

        // ---- the crate and its sources ----

        /// <summary>Finds the crate of the active document / Solution Explorer selection and (re)loads its sources when it changed or <paramref name="force"/>.</summary>
        internal async Task ResolveAndLoadAsync(bool force)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var crate = ResolveCrate() ?? _crate;
            if (crate is null)
            {
                _crate = null;
                _control.ShowMessage(string.Empty, DataSourcesText.NoProject);
                return;
            }

            if (!force && _crate != null && string.Equals(_crate.ManifestDirectory, crate.ManifestDirectory, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            bool changed = _crate is null || !string.Equals(_crate.ManifestDirectory, crate.ManifestDirectory, StringComparison.OrdinalIgnoreCase);
            _crate = crate;
            _root = RootOf(crate);
            _settings = DataSourcesSettings.Load(_root);
            _control.Settings = _settings;
            if (changed)
            {
                Watch(crate);
            }

            await LoadAsync();
        }

        private async Task LoadAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var crate = _crate;
            if (crate is null)
            {
                return;
            }

            int version = ++_loadVersion;
            string header = DataSourcesText.ProjectHeader(crate.PackageName);
            var files = crate.KbdataFiles();
            if (files.Count == 0)
            {
                _sources = Array.Empty<KbdataSourceInfo>();
                _control.Show(header, _sources, Array.Empty<KeyValuePair<string, string>>());
                UpdateCommandUi();
                await RefreshViewModelAsync();
                return;
            }

            var sources = new List<KbdataSourceInfo>();
            var errors = new List<KeyValuePair<string, string>>();
            foreach (var file in files)
            {
                try
                {
                    var result = await DataToolHost.Service.SendAsync("kbdata.read", new JsonObject { ["path"] = file }, CancellationToken.None);
                    sources.Add(KbdataSourceInfo.Parse(file, result));
                }
                catch (Exception exception) when (exception is DataToolException || exception is FormatException)
                {
                    errors.Add(new KeyValuePair<string, string>(file, exception.Message));
                }
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (version != _loadVersion)
            {
                return;
            }

            _sources = sources;
            _control.Show(header, sources, errors);
            UpdateCommandUi();
            await RefreshViewModelAsync();
        }

        /// <summary>
        /// The view-model node (docs/DESIGNER.md "Data bindings"): the data context of the active view's designer (else of the most
        /// recently opened one), asked to its language server.
        /// </summary>
        internal async Task RefreshViewModelAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            string? active = null;
            try
            {
                if (ServiceProvider.GlobalProvider.GetService(typeof(EnvDTE.DTE)) is EnvDTE.DTE dte)
                {
                    active = dte.ActiveDocument?.FullName;
                }
            }
            catch (COMException)
            {
                active = null;
            }

            var open = ExternalDesignerDrop.OpenDocuments;
            var target = active != null && open.Any(p => string.Equals(p, active, StringComparison.OrdinalIgnoreCase)) ? active : open.FirstOrDefault();
            var schema = target is null ? null : ExternalDesignerDrop.BindingSourcesOf(target);
            _control.ShowViewModel(schema?.Context.Label ?? string.Empty, schema?.Context.Members ?? Array.Empty<Kubuno.Views.Designer.Bindings.BindingMember>());
        }

        /// <summary>The crate of the active document, else of the Solution Explorer selection.</summary>
        private static DataSourceCrate? ResolveCrate()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                if (ServiceProvider.GlobalProvider.GetService(typeof(EnvDTE.DTE)) is EnvDTE.DTE dte && dte.ActiveDocument?.FullName is { Length: > 0 } active &&
                    DataSourceCrate.Find(active) is { } fromDocument)
                {
                    return fromDocument;
                }
            }
            catch (Exception exception) when (exception is COMException || exception is ArgumentException || exception is InvalidOperationException)
            {
                // No active document.
            }

            if (Kubuno.Rust.Commands.RsprojSelection.TryGetCurrent() is { } project && DataSourceCrate.Find(project.ManifestPath) is { } fromProject)
            {
                return fromProject;
            }

            return SelectedItemPath() is { } path ? DataSourceCrate.Find(path) : null;
        }

        /// <summary>The file or folder selected in Solution Explorer (Open Folder included), or null.</summary>
        private static string? SelectedItemPath()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (ServiceProvider.GlobalProvider.GetService(typeof(SVsShellMonitorSelection)) is not IVsMonitorSelection monitor)
            {
                return null;
            }

            IntPtr hierarchyPointer = IntPtr.Zero;
            IntPtr containerPointer = IntPtr.Zero;
            try
            {
                if (ErrorHandler.Failed(monitor.GetCurrentSelection(out hierarchyPointer, out uint itemId, out _, out containerPointer)) || hierarchyPointer == IntPtr.Zero)
                {
                    return null;
                }

                if (Marshal.GetObjectForIUnknown(hierarchyPointer) is IVsHierarchy hierarchy && ErrorHandler.Succeeded(hierarchy.GetCanonicalName(itemId, out string name)) && !string.IsNullOrEmpty(name) && Path.IsPathRooted(name))
                {
                    return name;
                }
            }
            catch (Exception exception) when (exception is COMException || exception is ArgumentException)
            {
                return null;
            }
            finally
            {
                if (hierarchyPointer != IntPtr.Zero)
                {
                    Marshal.Release(hierarchyPointer);
                }

                if (containerPointer != IntPtr.Zero)
                {
                    Marshal.Release(containerPointer);
                }
            }

            return null;
        }

        /// <summary>Where <c>.vs\kubuno</c> lives for <paramref name="crate"/>: the Open Folder workspace or the solution directory containing it (the SQL IntelliSense rule), else the crate.</summary>
        internal static string RootOf(DataSourceCrate crate)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            string? folder = null;
            try
            {
                if (ServiceProvider.GlobalProvider.GetService(typeof(SComponentModel)) is IComponentModel model)
                {
                    folder = model.GetService<IVsFolderWorkspaceService>()?.CurrentWorkspace?.Location;
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is COMException)
            {
                folder = null;
            }

            string? solution = null;
            if (ServiceProvider.GlobalProvider.GetService(typeof(SVsSolution)) is IVsSolution vsSolution && vsSolution.GetSolutionInfo(out string directory, out _, out _) == VSConstants.S_OK && !string.IsNullOrEmpty(directory))
            {
                solution = directory;
            }

            return SchemaSnapshotStore.ResolveRoot(crate.ManifestPath, new[] { folder, solution }) ?? crate.ManifestDirectory;
        }

        private void Watch(DataSourceCrate crate)
        {
            _watcher?.Dispose();
            _watcher = null;
            string src = Path.Combine(crate.ManifestDirectory, "src");
            if (!Directory.Exists(src))
            {
                return;
            }

            try
            {
                _watcher = new FileSystemWatcher(src, "*.kbdata") { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite };
                FileSystemEventHandler changed = (_, _) => ScheduleReload();
                _watcher.Changed += changed;
                _watcher.Created += changed;
                _watcher.Deleted += changed;
                _watcher.Renamed += (_, _) => ScheduleReload();
                _watcher.EnableRaisingEvents = true;
            }
            catch (Exception exception) when (exception is IOException || exception is ArgumentException || exception is UnauthorizedAccessException)
            {
                KubunoLog.WriteLine("Kubuno: the Data Sources window does not follow " + src + ": " + exception.Message);
            }
        }

        private void ScheduleReload()
        {
#pragma warning disable VSTHRD001, VSTHRD110 // a file watcher thread: debounce on the UI dispatcher.
            _control.Dispatcher.BeginInvoke(new Action(() =>
            {
                _reloadTimer ??= new System.Windows.Threading.DispatcherTimer(TimeSpan.FromMilliseconds(400), System.Windows.Threading.DispatcherPriority.Background, (_, _) =>
                {
                    _reloadTimer!.Stop();
                    DataUi.RunUi(LoadAsync, "DataSources/Reload");
                }, _control.Dispatcher);
                _reloadTimer.Stop();
                _reloadTimer.Start();
            }));
#pragma warning restore VSTHRD001, VSTHRD110
        }

        // ---- IVsSelectionEvents: follow the active document / selection ----

        int IVsSelectionEvents.OnSelectionChanged(IVsHierarchy pHierOld, uint itemidOld, IVsMultiItemSelect pMISOld, ISelectionContainer pSCOld, IVsHierarchy pHierNew, uint itemidNew, IVsMultiItemSelect pMISNew, ISelectionContainer pSCNew)
        {
            if (_control.IsVisible)
            {
                DataUi.RunUi(() => ResolveAndLoadAsync(force: false), "DataSources/Follow");
            }

            return VSConstants.S_OK;
        }

        int IVsSelectionEvents.OnElementValueChanged(uint elementid, object varValueOld, object varValueNew)
        {
            if (elementid == (uint)VSConstants.VSSELELEMID.SEID_DocumentFrame && _control.IsVisible)
            {
                DataUi.RunUi(() => ResolveAndLoadAsync(force: false), "DataSources/Follow");
                DataUi.RunUi(RefreshViewModelAsync, "DataSources/ViewModel");
            }

            return VSConstants.S_OK;
        }

        int IVsSelectionEvents.OnCmdUIContextChanged(uint dwCmdUICookie, int fActive) => VSConstants.S_OK;

        // ---- drops ----

        private IExternalDropSource? DropSourceFor(DataSourceNode node)
        {
            if (node.Kind == DataSourceNodeKind.Member && node.Member is { } member)
            {
                return new ViewModelDropSource(member);
            }

            if (!node.IsDraggable || _settings is null)
            {
                return null;
            }

            var mode = _settings.ModeOf(node.KbdataPath, node.Table!.Name);
            var controls = _settings.ControlsOf(node.KbdataPath, node.Table.Name);
            return new DataSourceDropSource(node.Source!, node.Table, mode, controls, node.Kind == DataSourceNodeKind.Column ? node.Column!.Name : null);
        }

        private void StartDrag(DataSourceNode node)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (DropSourceFor(node) is not { } source)
            {
                return;
            }

            var data = new DataObject();
            // The design surface's own drop target understands Toolbox items only: the placeholder gives the drop feedback and point.
            data.SetData(Kubuno.Views.Designer.Toolbox.ToolboxItemFormat.FormatName, new MemoryStream(Kubuno.Views.Designer.Toolbox.ToolboxItemFormat.Encode(ExternalDesignerDrop.MarkerComponent)));
            var payload = node.Member is { } member
                ? new JsonObject { ["member"] = member.Path }
                : new JsonObject { ["kbdata"] = node.KbdataPath, ["table"] = node.Table!.Name, ["column"] = node.Column?.Name };
            data.SetData(DragFormat, new MemoryStream(Encoding.UTF8.GetBytes(payload.ToJsonString())));
            ExternalDesignerDrop.BeginDrag(source);
            DragDropEffects effect = DragDropEffects.None;
            try
            {
                effect = DragDrop.DoDragDrop(_control, data, DragDropEffects.Copy);
            }
            catch (Exception exception) when (exception is COMException || exception is InvalidOperationException)
            {
                KubunoLog.WriteLine("Kubuno: the Data Sources drag failed: " + exception.Message);
            }
            finally
            {
                ExternalDesignerDrop.EndDrag(effect != DragDropEffects.None);
            }
        }

        /// <summary>Enter / double-click / "Add to the Open View": the active view's designer, else the only open one.</summary>
        private void InsertIntoActiveView(DataSourceNode node)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (DropSourceFor(node) is not { } source)
            {
                return;
            }

            string? active = null;
            try
            {
                if (ServiceProvider.GlobalProvider.GetService(typeof(EnvDTE.DTE)) is EnvDTE.DTE dte)
                {
                    active = dte.ActiveDocument?.FullName;
                }
            }
            catch (COMException)
            {
                active = null;
            }

            var open = ExternalDesignerDrop.OpenDocuments;
            string? target = active != null && open.Any(p => string.Equals(p, active, StringComparison.OrdinalIgnoreCase)) ? active : open.FirstOrDefault();
            if (target is null || !ExternalDesignerDrop.InsertInto(target, source))
            {
                SetStatus(DataSourcesText.NoDesigner);
            }
        }

        /// <summary>A Visual Studio Yes/No question titled "Data Sources"; true for Yes.</summary>
        internal static bool Confirm(string message)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return VsShellUtilities.ShowMessageBox(ServiceProvider.GlobalProvider, message, DataSourcesText.DataSources, OLEMSGICON.OLEMSGICON_QUERY, OLEMSGBUTTON.OLEMSGBUTTON_YESNO, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_SECOND) == 6; // IDYES
        }

        /// <summary>A Visual Studio error message box titled "Data Sources".</summary>
        internal static void ShowError(string message)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            VsShellUtilities.ShowMessageBox(ServiceProvider.GlobalProvider, message, DataSourcesText.DataSources, OLEMSGICON.OLEMSGICON_CRITICAL, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }

        private static void SetStatus(string message)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            KubunoLog.WriteLine("Kubuno: " + message);
            if (ServiceProvider.GlobalProvider.GetService(typeof(SVsStatusbar)) is IVsStatusbar statusBar)
            {
                statusBar.SetText(message);
            }
        }

        // ---- the wizard ----

        private async Task RunWizardAsync(KbdataSourceInfo? existing)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var crate = _crate;
            if (crate is null)
            {
                SetStatus(DataSourcesText.NoProject);
                return;
            }

            var generator = new DataSourceGenerator(crate, _root ?? crate.ManifestDirectory, _settings);
            var existingNames = crate.KbdataFiles().Select(Path.GetFileNameWithoutExtension).Where(n => n != null).Cast<string>();
            IReadOnlyList<ExplorerConnectionInfo> connections;
            try
            {
                connections = await DataToolHost.Service.ListConnectionsAsync(CancellationToken.None);
            }
            catch (DataToolException exception)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                ShowError(exception.Message);
                return;
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var model = new DataSourceWizardModel(
                connections,
                existingNames,
                crate.ModuleSchema,
                existing is null ? null : new DataSourceWizardExisting(existing.ModuleName, existing.Connection, existing.Schema, existing.Tables.Select(t => t.Name), _settings?.ExplorerConnectionOf(existing.FilePath)));
            var dialog = new DataSourceWizardDialog(model, generator);
            if (dialog.ShowModal() == true)
            {
                await LoadAsync();
                if (existing is null && dialog.CreatedSource is { } name)
                {
                    _control.Select(name);
                }
            }
        }

        /// <summary>Shows (creating if needed) the Data Sources window.</summary>
        internal static async Task<DataSourcesToolWindow?> ShowAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var package = Kubuno.Shared.KubunoHost.Package;
            if (package is null)
            {
                return null;
            }

            try
            {
                if (await package.FindToolWindowAsync(typeof(DataSourcesToolWindow), 0, create: true, package.DisposalToken) is DataSourcesToolWindow window && window.Frame is IVsWindowFrame frame)
                {
                    ErrorHandler.ThrowOnFailure(frame.Show());
                    await window.ResolveAndLoadAsync(force: false);
                    return window;
                }
            }
            catch (Exception exception)
            {
                KubunoLog.WriteException("Kubuno: failed to show the Data Sources window", exception);
            }

            return null;
        }
    }

    /// <summary>A table or a column of the Data Sources window being dropped onto a view (<see cref="DataSourceDropPlanner"/>).</summary>
    internal sealed class DataSourceDropSource : IExternalDropSource
    {
        private readonly KbdataSourceInfo _source;
        private readonly KbdataTableInfo _table;
        private readonly DataTableDropMode _mode;
        private readonly IReadOnlyDictionary<string, DataControlKind> _controls;
        private readonly string? _column;

        public DataSourceDropSource(KbdataSourceInfo source, KbdataTableInfo table, DataTableDropMode mode, IReadOnlyDictionary<string, DataControlKind> controls, string? column)
        {
            _source = source;
            _table = table;
            _mode = mode;
            _controls = controls;
            _column = column;
        }

        public ExternalDropResult? Plan(ExternalDropRequest request, out string? error)
        {
            // The components name the source's connection string: only the source's own crate resolves it.
            if (request.DocumentPath.Length > 0 && DataSourceCrate.Find(request.DocumentPath) is { } viewCrate && DataSourceCrate.Find(_source.FilePath) is { } sourceCrate &&
                !string.Equals(viewCrate.ManifestDirectory, sourceCrate.ManifestDirectory, StringComparison.OrdinalIgnoreCase))
            {
                error = DataSourcesText.OtherProject(sourceCrate.PackageName, viewCrate.PackageName);
                return null;
            }

            // A column dropped onto a control binds it (Windows Forms), when the view already has the table's binding source.
            if (_column is not null && request.Registry is { } registry && Designer.Bindings.BindingDropPlanner.HitTarget(request.DocumentText, request.ParentId, request.X, request.Y) is { } target
                && registry.Find(target.Name) is { } component && KbviewOutline.TryParse(request.DocumentText) is { } outline
                && DataSourceDropPlanner.FindConnection(outline, _source.Connection) is { } connection
                && DataSourceDropPlanner.FindAdapter(outline, connection, _table.Name) is { } adapter
                && DataSourceDropPlanner.FindBindingSource(outline, adapter) is { } bindingSource
                && Designer.Bindings.BindingDropPlanner.TargetProperty(component, Kubuno.Views.Designer.Bindings.BindingShape.Text) is { } property)
            {
                var expression = "{Binding Source=" + bindingSource + ", Path=" + _column + (property is "Text" or "Value" or "Checked" or "Date" or "SelectedValue" ? ", Mode=TwoWay" : string.Empty) + "}";
                error = null;
                return new ExternalDropResult(Array.Empty<ExternalDropInsertion>(), Kubuno.Views.Designer.Bindings.BindingStrings.DroppedOn(_table.Name + "." + _column, property, target.XName ?? target.Name), target.Id)
                {
                    AttributeEdits = new[] { (target.Id, property, expression) },
                };
            }

            var plan = DataSourceDropPlanner.Plan(request.DocumentText, new DataDropRequest(_source, _table, _mode, _controls, _column), request.ParentId, request.Index, request.X, request.Y, out error);
            if (plan is null)
            {
                return null;
            }

            KubunoLog.WriteLine("Kubuno: Data Sources drop of " + _table.Name + (_column is null ? string.Empty : "." + _column) + " into " + Path.GetFileName(request.DocumentPath) + ": " + string.Join(", ", plan.CreatedNames));
            return new ExternalDropResult(plan.Insertions.Select(i => new ExternalDropInsertion(i.ParentId, i.Index, i.Xml)).ToList(), plan.Description, plan.SelectElementId);
        }
    }

    /// <summary>
    /// A member of the active view's data context dropped from the Data Sources window (docs/DESIGNER.md "Data bindings"): onto a
    /// control it binds the control's default binding property, onto empty space it adds a label and a bound control
    /// (<see cref="Designer.Bindings.BindingDropPlanner"/>).
    /// </summary>
    internal sealed class ViewModelDropSource : IExternalDropSource
    {
        private readonly Kubuno.Views.Designer.Bindings.BindingMember _member;

        public ViewModelDropSource(Kubuno.Views.Designer.Bindings.BindingMember member)
        {
            _member = member;
        }

        public ExternalDropResult? Plan(ExternalDropRequest request, out string? error)
        {
            var plan = Designer.Bindings.BindingDropPlanner.Plan(request.DocumentText, request.Registry ?? Kubuno.Views.Designer.Registry.ComponentRegistry.Empty, _member, request.ParentId, request.X, request.Y, out error);
            if (plan is null)
            {
                return null;
            }

            KubunoLog.WriteLine("Kubuno: Data Sources drop of " + _member.Path + " into " + Path.GetFileName(request.DocumentPath) + ": " + plan.Description);
            return new ExternalDropResult(plan.Insertions.Select(i => new ExternalDropInsertion(i.ParentId, i.Index, i.Xml)).ToList(), plan.Description, plan.SelectElementId)
            {
                AttributeEdits = plan.Edits.Select(e => (e.ElementId, e.Attribute, e.Value)).ToList(),
            };
        }
    }
}
