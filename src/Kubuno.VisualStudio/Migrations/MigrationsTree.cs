using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Kubuno.VisualStudio.Commands;
using Kubuno.VisualStudio.Core.Data;
using Kubuno.VisualStudio.Core.Migrations;
using Kubuno.VisualStudio.DataExplorer;
using Kubuno.VisualStudio.Logging;
using Kubuno.VisualStudio.SolutionExplorer;
using Microsoft.Internal.VisualStudio.PlatformUI;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Utilities;

namespace Kubuno.VisualStudio.Migrations
{
    /// <summary>
    /// A <c>.rsproj</c> project node whose crate has a <c>migrations</c> folder or <c>.kbdata</c> data sources gets a
    /// <c>Migrations</c> node (docs/DATA.md DATA-7), next to "Dependencies".
    /// </summary>
    [Export(typeof(IAttachedCollectionSourceProvider))]
    [Name(nameof(MigrationsTreeProvider))]
    [Order(Before = HierarchyItemsProviderNames.Contains)]
    internal sealed class MigrationsTreeProvider : HierarchyItemSourceProviderBase
    {
        private const string RustProjectCapability = "RustProjectSystem";

        protected override bool OwnsTreeItems => false;

        protected override IAttachedCollectionSource? CreateForHierarchyItem(IVsHierarchyItem item)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var identity = item.HierarchyIdentity;
            if (!(identity.IsRoot || (identity.IsNestedItem && identity.NestedItemID == (uint)VSConstants.VSITEMID.Root)))
            {
                return null;
            }

            var project = identity.NestedHierarchy ?? identity.Hierarchy;
            return project != null && project.IsCapabilityMatch(RustProjectCapability) ? new MigrationsSource(item, project) : null;
        }
    }

    /// <summary>
    /// The <c>Migrations</c> node of one crate and its loading pipeline. Never blocks Solution Explorer: the node appears
    /// (off the UI thread) only when the crate has migrations or data sources, and nothing talks to the database before
    /// it is expanded; from then on <c>migrate.status</c> and <c>sqlx.status</c> are re-read after each migration command
    /// and, debounced, whenever the <c>migrations</c> or <c>.sqlx</c> folders or a <c>.kbdata</c> file change.
    /// </summary>
    internal sealed class MigrationsSource : IAttachedCollectionSource, INotifyPropertyChanged, IDisposable
    {
        private static readonly List<WeakReference<MigrationsSource>> Live = new List<WeakReference<MigrationsSource>>();

        private readonly ObservableCollection<KubunoTreeItem> _items = new ObservableCollection<KubunoTreeItem>();
        private readonly MigrationsRootTreeItem _root;
        private CrateFolderWatcher? _watcher;
        private CancellationTokenSource? _pending;
        private bool _expanded;
        private bool _disposed;

        public MigrationsSource(object sourceItem, IVsHierarchy project)
        {
            SourceItem = sourceItem;
            Project = project;
            _root = new MigrationsRootTreeItem(this);
            lock (Live)
            {
                Live.RemoveAll(w => !w.TryGetTarget(out _));
                Live.Add(new WeakReference<MigrationsSource>(this));
            }

            MigrationCommands.Start(InitializeAsync, "Tree/Initialize");
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public object SourceItem { get; }

        public IVsHierarchy Project { get; }

        /// <summary>The crate's directory (null until resolved, or when the project has no Cargo.toml).</summary>
        public string? CrateDirectory { get; private set; }

        public bool HasItems => _items.Count > 0;

        public IEnumerable Items => _items;

        /// <summary>The latest model (null before the node was expanded).</summary>
        public MigrationsTreeModel? Model { get; private set; }

        /// <summary>Raised on the UI thread after each published model.</summary>
        public event Action<MigrationsSource>? ModelChanged;

        /// <summary>Re-reads the node of <paramref name="crateDirectory"/>, if Solution Explorer created one.</summary>
        public static void ReloadFor(string crateDirectory)
        {
            foreach (var source in All())
            {
                if (string.Equals(source.CrateDirectory, crateDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    source.Reload();
                }
            }
        }

        /// <summary>The live sources (the stale-cache info bar looks at them after a build).</summary>
        public static IReadOnlyList<MigrationsSource> All()
        {
            lock (Live)
            {
                var sources = new List<MigrationsSource>();
                foreach (var weak in Live)
                {
                    if (weak.TryGetTarget(out var source) && !source._disposed)
                    {
                        sources.Add(source);
                    }
                }

                return sources;
            }
        }

        public void Reload() => MigrationCommands.Start(LoadAsync, "Tree/Reload");

        /// <summary>Re-reads everything and waits for it (the stale-cache info bar after a build).</summary>
        public Task ReloadAndWaitAsync() => LoadAsync();

        /// <summary>The root node's children were asked for: from now on the state is read.</summary>
        public void EnsureExpanded()
        {
            if (_expanded)
            {
                return;
            }

            _expanded = true;
            Reload();
        }

        public void Dispose()
        {
            _disposed = true;
            _watcher?.Dispose();
            _pending?.Cancel();
        }

        private async Task InitializeAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var context = RsprojProjectContext.TryCreate(Project);
            if (context is null || _disposed)
            {
                return;
            }

            CrateDirectory = context.ProjectDirectory;
            await LoadAsync();
        }

        private async Task LoadAsync()
        {
            var directory = CrateDirectory;
            if (directory is null || _disposed)
            {
                return;
            }

            var cancellation = new CancellationTokenSource();
            Interlocked.Exchange(ref _pending, cancellation)?.Cancel();
            var token = cancellation.Token;
            try
            {
                await TaskScheduler.Default;
                if (_watcher is null)
                {
                    var watcher = new CrateFolderWatcher(directory, Reload);
                    if (Interlocked.CompareExchange(ref _watcher, watcher, null) != null)
                    {
                        watcher.Dispose();
                    }
                }

                _watcher?.EnsureWatching();
                var info = CrateDataInfo.Read(directory);
                bool visible = Directory.Exists(info.MigrationsDirectory) || info.DataSources.Count > 0 || CrateDataInfo.KbdataFiles(directory).Count > 0;
                MigrationsTreeModel? model = null;
                if (visible && _expanded)
                {
                    model = await ReadModelAsync(info, token);
                }

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(token);
                if (token.IsCancellationRequested || _disposed)
                {
                    return;
                }

                SetVisible(visible);
                if (model != null)
                {
                    Model = model;
                    _root.SetModel(model);

                    // Idempotent: subscribes to the build events if the package could not at load (no DTE yet, e.g. a
                    // first start of the shell).
                    SqlxStaleInfoBar.Initialize();
                    ModelChanged?.Invoke(this);
                }
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer load.
            }
            catch (Exception exception)
            {
                KubunoLog.WriteException("Solution Explorer: reading the migrations", exception);
            }
        }

        private static async Task<MigrationsTreeModel> ReadModelAsync(CrateDataInfo info, CancellationToken token)
        {
            var service = new MigrationService(DataToolHost.Service);
            SqlxCacheStatus? cache = null;
            try
            {
                cache = await service.SqlxStatusAsync(info.CrateDirectory, token).ConfigureAwait(false);
            }
            catch (DataToolException exception)
            {
                KubunoLog.WriteLine($"Kubuno migrations ({info.DisplayName}): sqlx.status: {exception.Message}");
            }

            if (!Directory.Exists(info.MigrationsDirectory))
            {
                return MigrationsTreeModel.Build(new MigrationStatusResult(), null, cache);
            }

            var connection = MigrationCommands.ConnectionWithoutAsking(info, out var whyNot);
            if (connection is null)
            {
                return MigrationsTreeModel.Build(null, whyNot, cache);
            }

            try
            {
                var status = await service.StatusAsync(info.TargetFor(connection), info.MigrationsDirectory, token).ConfigureAwait(false);
                return MigrationsTreeModel.Build(status, null, cache);
            }
            catch (DataToolException exception)
            {
                return MigrationsTreeModel.Build(null, exception.Message, cache);
            }
        }

        private void SetVisible(bool visible)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            bool had = HasItems;
            if (visible && _items.Count == 0)
            {
                _items.Add(_root);
            }
            else if (!visible && _items.Count > 0)
            {
                _items.Clear();
            }

            if (had != HasItems)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasItems)));
            }
        }
    }

    /// <summary>
    /// Watches what the <c>Migrations</c> node shows - the crate folder itself (the <c>migrations</c> and <c>.sqlx</c>
    /// folders appearing or going), <c>migrations\*.sql</c>, <c>.sqlx\*</c> and <c>src\**\*.kbdata</c> - never the whole
    /// crate (its <c>target</c> folder churns during builds); calls back once things have been quiet for 700 ms.
    /// </summary>
    internal sealed class CrateFolderWatcher : IDisposable
    {
        private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(700);
        private readonly string _directory;
        private readonly Timer _timer;
        private readonly Dictionary<string, FileSystemWatcher> _watchers = new Dictionary<string, FileSystemWatcher>(StringComparer.OrdinalIgnoreCase);
        private bool _disposed;

        public CrateFolderWatcher(string directory, Action changed)
        {
            _directory = directory;
            _timer = new Timer(_ => changed(), null, Timeout.Infinite, Timeout.Infinite);
        }

        /// <summary>Starts watching the folders that exist now (called on every load: a new <c>migrations</c> folder gets watched).</summary>
        public void EnsureWatching()
        {
            lock (_watchers)
            {
                if (_disposed)
                {
                    return;
                }

                Watch("crate", _directory, "*", recursive: false, NotifyFilters.DirectoryName | NotifyFilters.FileName);
                Watch("migrations", Path.Combine(_directory, "migrations"), "*.sql", recursive: false, NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size);
                Watch("sqlx", Path.Combine(_directory, ".sqlx"), "*", recursive: false, NotifyFilters.FileName | NotifyFilters.LastWrite);
                Watch("src", Path.Combine(_directory, "src"), "*.kbdata", recursive: true, NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size);
            }
        }

        public void Dispose()
        {
            lock (_watchers)
            {
                _disposed = true;
                foreach (var watcher in _watchers.Values)
                {
                    watcher.Dispose();
                }

                _watchers.Clear();
            }

            _timer.Dispose();
        }

        private void Watch(string key, string directory, string filter, bool recursive, NotifyFilters notify)
        {
            if (_watchers.TryGetValue(key, out var existing))
            {
                if (Directory.Exists(directory))
                {
                    return;
                }

                // The folder went away: watch it again when it comes back.
                existing.Dispose();
                _watchers.Remove(key);
                return;
            }

            if (!Directory.Exists(directory))
            {
                return;
            }

            try
            {
                var watcher = new FileSystemWatcher(directory, filter) { IncludeSubdirectories = recursive, NotifyFilter = notify };
                watcher.Changed += (_, _) => Poke();
                watcher.Created += (_, _) => Poke();
                watcher.Deleted += (_, _) => Poke();
                watcher.Renamed += (_, _) => Poke();
                watcher.EnableRaisingEvents = true;
                _watchers[key] = watcher;
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or PlatformNotSupportedException)
            {
                // A folder that cannot be watched only loses the automatic refresh.
                KubunoLog.WriteException($"Solution Explorer: watching {directory}", exception);
            }
        }

        private void Poke()
        {
            try
            {
                _timer.Change(Delay, Timeout.InfiniteTimeSpan);
            }
            catch (ObjectDisposedException)
            {
                // Disposed while an event was in flight.
            }
        }
    }

    /// <summary>The <c>Migrations</c> node: its children are read when it is expanded.</summary>
    internal sealed class MigrationsRootTreeItem : KubunoTreeItem, IContextMenuPattern, IRefreshPattern
    {
        private readonly ObservableCollection<KubunoTreeItem> _children = new ObservableCollection<KubunoTreeItem>();
        private readonly MigrationsSource _owner;
        private MigrationsTreeModel _model = MigrationsTreeModel.Loading();
        private bool _loaded;

        public MigrationsRootTreeItem(MigrationsSource owner)
            : base(order: 0)
        {
            _owner = owner;
            Merge(_model);
        }

        public override string Text => _model.RootText;

        public override string ToolTipText => MigrationText.MigrationsToolTip;

        public override ImageMoniker IconMoniker => Moniker(_model.RootMonikerName);

        public override bool HasItems => !_loaded || _children.Count > 0;

        public override IEnumerable Items
        {
            get
            {
                _owner.EnsureExpanded();
                return _children;
            }
        }

        public override int Priority => -1;

        public IContextMenuController ContextMenuController => MigrationsContextMenu.For(_owner, null);

        public Task RefreshAsync()
        {
            _owner.Reload();
            return Task.CompletedTask;
        }

        public void CancelLoad()
        {
        }

        public void SetModel(MigrationsTreeModel model)
        {
            bool hadItems = HasItems;
            _loaded = true;
            _model = model;
            Merge(model);
            RaiseDisplayChanged();
            if (hadItems != HasItems)
            {
                RaisePropertyChanged(nameof(HasItems));
            }
        }

        private void Merge(MigrationsTreeModel model)
        {
            var fresh = new List<(string Key, Func<KubunoTreeItem> Create, Action<KubunoTreeItem> Update)>();
            for (int i = 0; i < model.Children.Count; i++)
            {
                var node = model.Children[i];
                var order = i;
                fresh.Add((node.Key, () => new MigrationsChildTreeItem(node, _owner, order), item => ((MigrationsChildTreeItem)item).Update(node, order)));
            }

            TreeMerger.Merge(_children, fresh);
        }
    }

    /// <summary>A migration, the SQLx cache state, or a message; double-click on a migration opens its up file.</summary>
    internal sealed class MigrationsChildTreeItem : KubunoTreeItem, IInvocationPattern, IContextMenuPattern
    {
        private readonly MigrationsSource _owner;

        public MigrationsChildTreeItem(MigrationsNode node, MigrationsSource owner, int order)
            : base(order)
        {
            Node = node;
            _owner = owner;
        }

        public MigrationsNode Node { get; private set; }

        public override string Text => Node.Text;

        public override string ToolTipText => Node.ToolTip;

        public override ImageMoniker IconMoniker => Moniker(Node.MonikerName);

        public IInvocationController InvocationController => MigrationInvocationController.Instance;

        public bool CanPreview => false;

        public IContextMenuController ContextMenuController => MigrationsContextMenu.For(_owner, Node);

        public void Update(MigrationsNode node, int order)
        {
            Node = node;
            Order = order;
            RaiseDisplayChanged();
        }
    }

    /// <summary>Double-click / Enter on a migration: open its up file.</summary>
    internal sealed class MigrationInvocationController : IInvocationController
    {
        public static readonly MigrationInvocationController Instance = new MigrationInvocationController();

        public bool Invoke(IEnumerable<object> items, InputSource inputSource, bool preview)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            foreach (var item in items)
            {
                if (item is MigrationsChildTreeItem { Node.FilePath: { } path } && File.Exists(path))
                {
                    VsShellUtilities.OpenDocument(ServiceProvider.GlobalProvider, path);
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// The floating context menu of the <c>Migrations</c> node and its children (<c>KubunoMigrationsContextMenu</c> in
    /// <c>KubunoCommands.vsct</c>), shown through <see cref="IVsUIShell.ShowContextMenu"/> like the Dependencies menus.
    /// </summary>
    internal static class MigrationsContextMenu
    {
        public static IContextMenuController For(MigrationsSource owner, MigrationsNode? node) => new Controller(owner, node);

        private sealed class Controller : IContextMenuController
        {
            private readonly MigrationsSource _owner;
            private readonly MigrationsNode? _node;

            public Controller(MigrationsSource owner, MigrationsNode? node)
            {
                _owner = owner;
                _node = node;
            }

            public bool ShowContextMenu(IEnumerable<object> items, Point point)
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                if (ServiceProvider.GlobalProvider.GetService(typeof(SVsUIShell)) is not IVsUIShell uiShell)
                {
                    return false;
                }

                var groupGuid = PackageGuids.KubunoCommandSet;
                var points = new[] { new POINTS { x = (short)point.X, y = (short)point.Y } };
                return ErrorHandler.Succeeded(uiShell.ShowContextMenu(0, ref groupGuid, PackageIds.KubunoMigrationsContextMenu, points, new CommandTarget(_owner, _node)));
            }
        }

        /// <summary>Which commands show on which node, their texts, and running them.</summary>
        private sealed class CommandTarget : IOleCommandTarget
        {
            private readonly MigrationsSource _owner;
            private readonly MigrationsNode? _node;

            public CommandTarget(MigrationsSource owner, MigrationsNode? node)
            {
                _owner = owner;
                _node = node;
            }

            public int QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OLECMD[] prgCmds, IntPtr pCmdText)
            {
                if (pguidCmdGroup != PackageGuids.KubunoCommandSet || prgCmds is null || cCmds == 0)
                {
                    return (int)Microsoft.VisualStudio.OLE.Interop.Constants.OLECMDERR_E_NOTSUPPORTED;
                }

                for (var i = 0; i < cCmds; i++)
                {
                    var (visible, text) = Status((int)prgCmds[i].cmdID);
                    prgCmds[i].cmdf = visible
                        ? (uint)(OLECMDF.OLECMDF_SUPPORTED | OLECMDF.OLECMDF_ENABLED)
                        : (uint)(OLECMDF.OLECMDF_SUPPORTED | OLECMDF.OLECMDF_INVISIBLE);
                    if (i == 0 && text != null)
                    {
                        SetCommandText(pCmdText, text);
                    }
                }

                return VSConstants.S_OK;
            }

            public int Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut)
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                if (pguidCmdGroup != PackageGuids.KubunoCommandSet || !Status((int)nCmdID).Visible || _owner.CrateDirectory is not { } crate)
                {
                    return (int)Microsoft.VisualStudio.OLE.Interop.Constants.OLECMDERR_E_NOTSUPPORTED;
                }

                switch ((int)nCmdID)
                {
                    case PackageIds.MigrationOpenCommand:
                        if (_node?.FilePath is { } path)
                        {
                            VsShellUtilities.OpenDocument(ServiceProvider.GlobalProvider, path);
                        }

                        break;
                    case PackageIds.MigrationAddCommand:
                        MigrationCommands.Start(() => MigrationCommands.AddMigrationAsync(crate), "Node/Add");
                        break;
                    case PackageIds.MigrationRunCommand:
                        MigrationCommands.Start(() => MigrationCommands.RunAsync(crate), "Node/Run");
                        break;
                    case PackageIds.MigrationRevertCommand:
                        MigrationCommands.Start(() => MigrationCommands.RevertAsync(crate), "Node/Revert");
                        break;
                    case PackageIds.SqlxPrepareCommand:
                        MigrationCommands.Start(() => MigrationCommands.PrepareAsync(crate), "Node/Prepare");
                        break;
                    case PackageIds.MigrationRefreshCommand:
                        MigrationCommands.Start(() => MigrationCommands.RefreshAsync(crate), "Node/Refresh");
                        break;
                    default:
                        return (int)Microsoft.VisualStudio.OLE.Interop.Constants.OLECMDERR_E_NOTSUPPORTED;
                }

                return VSConstants.S_OK;
            }

            private (bool Visible, string? Text) Status(int id)
            {
                bool root = _node is null;
                bool migration = _node?.Kind == MigrationsNodeKind.Migration;
                bool sqlx = _node?.Kind == MigrationsNodeKind.SqlxCache;
                return id switch
                {
                    PackageIds.MigrationOpenCommand => (migration && _node!.FilePath != null, MigrationText.OpenMigrationCommand),
                    PackageIds.MigrationAddCommand => (root, MigrationText.AddMigrationCommand),
                    PackageIds.MigrationRunCommand => (root || migration, MigrationText.RunMigrationsCommand),
                    PackageIds.MigrationRevertCommand => (root || migration, MigrationText.RevertMigrationCommand),
                    PackageIds.SqlxPrepareCommand => (root || sqlx, MigrationText.PrepareSqlxCommand),
                    PackageIds.MigrationRefreshCommand => (true, MigrationText.RefreshStatusCommand),
                    _ => (false, null),
                };
            }

            /// <summary>Writes <paramref name="text"/> into an <c>OLECMDTEXT</c> asking for the command's name.</summary>
            private static void SetCommandText(IntPtr pCmdText, string text)
            {
                if (pCmdText == IntPtr.Zero)
                {
                    return;
                }

                // OLECMDTEXT: DWORD cmdtextf; ULONG cwActual; ULONG cwBuf; WCHAR rgwz[cwBuf].
                var flags = (uint)Marshal.ReadInt32(pCmdText, 0);
                if ((flags & (uint)OLECMDTEXTF.OLECMDTEXTF_NAME) == 0)
                {
                    return;
                }

                var capacity = Marshal.ReadInt32(pCmdText, 8);
                if (capacity <= 0)
                {
                    return;
                }

                var count = Math.Min(text.Length, capacity - 1);
                var buffer = IntPtr.Add(pCmdText, 12);
                for (var i = 0; i < count; i++)
                {
                    Marshal.WriteInt16(buffer, i * 2, text[i]);
                }

                Marshal.WriteInt16(buffer, count * 2, 0);
                Marshal.WriteInt32(pCmdText, 4, count + 1);
            }
        }
    }
}
