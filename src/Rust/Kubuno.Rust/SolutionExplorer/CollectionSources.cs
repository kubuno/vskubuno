using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Rust.Cargo.Metadata;
using Kubuno.Rust.Cargo.Processes;
using Kubuno.Rust.Logic.SolutionExplorer;
using Kubuno.Core.Logging;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;

namespace Kubuno.Rust.SolutionExplorer
{
    /// <summary>
    /// The symbols attached under one <c>.rs</c> file node, or a file of an <see cref="Extensibility.ISolutionSymbolProvider"/>
    /// (docs/RSPROJ.md lot 8).
    /// Lazy: nothing runs until Solution Explorer asks for <see cref="Items"/> (the node is expanded
    /// or searched); from then on the file is watched and re-queried, debounced, whenever it is
    /// saved - the tree is merged in place so expanded nodes stay expanded.
    /// </summary>
    internal sealed class FileSymbolsSource : IAttachedCollectionSource, INotifyPropertyChanged, IDisposable
    {
        private readonly ObservableCollection<SymbolTreeItem> _items = new ObservableCollection<SymbolTreeItem>();
        private readonly string _path;
        private readonly ISymbolQuery _query;
        private readonly Extensibility.ISolutionSymbolProvider? _provider;
        private DebouncedFileWatcher? _watcher;
        private CancellationTokenSource? _pending;
        private bool _started;
        private bool _loaded;
        private bool _disposed;

        public FileSymbolsSource(object sourceItem, string path, ISymbolQuery query, Extensibility.ISolutionSymbolProvider? provider)
        {
            SourceItem = sourceItem;
            _path = path;
            _query = query;
            _provider = provider;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public object SourceItem { get; }

        public bool HasItems => !_loaded || _items.Count > 0;

        public IEnumerable Items
        {
            get
            {
                if (!_started && !_disposed)
                {
                    _started = true;
                    _watcher = new DebouncedFileWatcher(_path, () => _ = ReloadAsync());
                    _ = ReloadAsync();
                }

                return _items;
            }
        }

        public void Dispose()
        {
            _disposed = true;
            _watcher?.Dispose();
            _pending?.Cancel();
        }

        private async Task ReloadAsync()
        {
            var cancellation = new CancellationTokenSource();
            Interlocked.Exchange(ref _pending, cancellation)?.Cancel();

            IReadOnlyList<SolutionSymbol> symbols;
            try
            {
                symbols = File.Exists(_path)
                    ? await _query.QueryAsync(_path, cancellation.Token)
                    : Array.Empty<SolutionSymbol>();
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                KubunoLog.WriteException($"Solution Explorer symbols for {_path}", exception);
                symbols = Array.Empty<SolutionSymbol>();
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (cancellation.IsCancellationRequested || _disposed)
            {
                return;
            }

            bool hadItems = HasItems;
            _loaded = true;
            SymbolTreeMerger.Merge(_items, _path, symbols, _provider);
            if (hadItems != HasItems)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasItems)));
            }
        }
    }

    /// <summary>
    /// The "Dependencies" node attached under a <c>.rsproj</c> project node, and the loading pipeline
    /// behind it. Lazy (nothing runs before the node is expanded), never on the UI thread:
    /// <list type="number">
    /// <item><c>cargo metadata --no-deps</c> - the declarations, shown at once on a first load;</item>
    /// <item><c>cargo metadata</c> with the resolve graph (offline first) and <c>rustc -vV</c>, in
    /// parallel - resolved versions, categories, transitive dependencies, the toolchain;</item>
    /// <item>crates.io's sparse index for direct registry crates - update/yanked markers.</item>
    /// </list>
    /// Reloaded (debounced) whenever <c>Cargo.toml</c> or the workspace's <c>Cargo.lock</c> changes;
    /// the tree is merged in place, so expanded nodes stay expanded.
    /// </summary>
    internal sealed class ProjectDependenciesSource : IAttachedCollectionSource, IDisposable
    {
        private static readonly List<WeakReference<ProjectDependenciesSource>> Live = new List<WeakReference<ProjectDependenciesSource>>();

        private readonly DependenciesTreeItem _node;
        private DebouncedFileWatcher? _manifestWatcher;
        private DebouncedFileWatcher? _lockWatcher;
        private string? _lockPath;
        private CancellationTokenSource? _pending;
        private DateTime _ignoreLockUntilUtc;
        private bool _started;
        private bool _disposed;

        public ProjectDependenciesSource(object sourceItem, IVsHierarchy project)
        {
            SourceItem = sourceItem;
            Project = project;
            _node = new DependenciesTreeItem(EnsureLoaded, this);
            Items = new[] { _node };
            lock (Live)
            {
                Live.RemoveAll(w => !w.TryGetTarget(out _));
                Live.Add(new WeakReference<ProjectDependenciesSource>(this));
            }
        }

        public object SourceItem { get; }

        public bool HasItems => true;

        public IEnumerable Items { get; }

        public IVsHierarchy Project { get; }

        /// <summary>The latest model (declarations only until the resolve graph is known).</summary>
        public DependencyTreeModel Model { get; private set; } = DependencyTreeModel.Empty;

        /// <summary>The live source of <paramref name="project"/>'s Dependencies node, if Solution Explorer created one.</summary>
        public static ProjectDependenciesSource? For(IVsHierarchy project)
        {
            lock (Live)
            {
                foreach (var weak in Live)
                {
                    if (weak.TryGetTarget(out var source) && !source._disposed && ReferenceEquals(source.Project, project))
                    {
                        return source;
                    }
                }
            }

            return null;
        }

        /// <summary>Re-reads everything now (after a cargo command this extension ran).</summary>
        public void Reload()
        {
            if (_started && !_disposed)
            {
                _ = LoadAsync();
            }
        }

        public void Dispose()
        {
            _disposed = true;
            _manifestWatcher?.Dispose();
            _lockWatcher?.Dispose();
            _pending?.Cancel();
        }

        private void EnsureLoaded()
        {
            if (_started)
            {
                return;
            }

            _started = true;
            _ = LoadAsync();
        }

        private async Task LoadAsync()
        {
            var cancellation = new CancellationTokenSource();
            Interlocked.Exchange(ref _pending, cancellation)?.Cancel();
            var token = cancellation.Token;
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                var manifest = GetProperty("CargoManifestPath");
                var packageName = GetProperty("CargoPackage");
                if (string.IsNullOrEmpty(manifest) && Project.GetCanonicalName((uint)VSConstants.VSITEMID.Root, out var projectFile) == VSConstants.S_OK)
                {
                    manifest = Path.Combine(Path.GetDirectoryName(projectFile) ?? string.Empty, "Cargo.toml");
                }

                if (string.IsNullOrEmpty(manifest) || !File.Exists(manifest))
                {
                    Publish(DependencyTreeModel.Empty, token);
                    return;
                }

                _manifestWatcher ??= new DebouncedFileWatcher(manifest!, () => _ = LoadAsync());

                await TaskScheduler.Default;
                var directory = Path.GetDirectoryName(manifest)!;
                var toolchainTask = DependencyDataLoader.GetToolchainAsync(directory, token);

                CargoMetadata declared;
                try
                {
                    declared = await DependencyDataLoader.ReadDeclaredAsync(manifest!, token);
                }
                catch (CargoMetadataException)
                {
                    // Possibly caught mid-write (cargo add/remove, an editor saving): try once more.
                    await Task.Delay(800, token);
                    try
                    {
                        declared = await DependencyDataLoader.ReadDeclaredAsync(manifest!, token);
                    }
                    catch (CargoMetadataException exception)
                    {
                        // Cargo.toml is broken: say so under the node, keeping the last good tree (and its expansion).
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(token);
                        Publish(Model.WithDiagnostics(new[] { DependenciesText.MetadataFailed(DependencyDataLoader.Summarize(exception.Message)) }), token);
                        return;
                    }
                }

                var package = CargoDependencyGroups.FindPackage(declared, manifest, packageName);
                if (package is null)
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(token);
                    Publish(DependencyTreeModel.Empty, token);
                    return;
                }

                WatchLockFile(Path.Combine(declared.WorkspaceRoot, "Cargo.lock"));
                if (!Model.IsResolved)
                {
                    // First load: show the declarations right away, resolved versions follow.
                    var pending = DependencyTreeBuilder.Build(package, null, null, null);
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(token);
                    Publish(pending, token);
                    await TaskScheduler.Default;
                }

                var toolchain = await toolchainTask;
                var (resolved, error) = await DependencyDataLoader.ReadResolvedAsync(manifest!, toolchain?.Host, token);
                var model = DependencyTreeBuilder.Build(package, resolved, toolchain, error);

                // cargo metadata may have just (re)written Cargo.lock: that is not a change to react to.
                _ignoreLockUntilUtc = DateTime.UtcNow.AddSeconds(3);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(token);
                Publish(model, token);

                await TaskScheduler.Default;
                if (await DependencyDataLoader.ApplyRegistryStatusAsync(model.AllItems, token))
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(token);
                    Publish(model, token);
                }
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer load.
            }
            catch (Exception exception)
            {
                KubunoLog.WriteException("Solution Explorer: reading the Cargo dependencies", exception);
            }
        }

        private void Publish(DependencyTreeModel model, CancellationToken token)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (token.IsCancellationRequested || _disposed)
            {
                return;
            }

            Model = model;
            _node.SetModel(model);
        }

        private void WatchLockFile(string lockPath)
        {
            if (string.Equals(_lockPath, lockPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _lockWatcher?.Dispose();
            _lockPath = lockPath;
            _lockWatcher = new DebouncedFileWatcher(lockPath, () =>
            {
                if (DateTime.UtcNow >= _ignoreLockUntilUtc)
                {
                    _ = LoadAsync();
                }
            });
        }

        private string? GetProperty(string name)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return Project is IVsBuildPropertyStorage storage
                && storage.GetPropertyValue(name, null, (uint)_PersistStorageType.PST_PROJECT_FILE, out var value) == VSConstants.S_OK
                && !string.IsNullOrWhiteSpace(value)
                ? value
                : null;
        }
    }

    /// <summary>Calls back (on a pool thread) once a file has stopped changing for half a second.</summary>
    internal sealed class DebouncedFileWatcher : IDisposable
    {
        private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(500);
        private readonly FileSystemWatcher? _watcher;
        private readonly Timer _timer;

        public DebouncedFileWatcher(string path, Action changed)
        {
            _timer = new Timer(_ => changed(), null, Timeout.Infinite, Timeout.Infinite);
            try
            {
                _watcher = new FileSystemWatcher(Path.GetDirectoryName(path)!, Path.GetFileName(path))
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                };
                _watcher.Changed += (_, _) => Poke();
                _watcher.Created += (_, _) => Poke();
                _watcher.Renamed += (_, _) => Poke();
                _watcher.EnableRaisingEvents = true;
            }
            catch (Exception exception)
            {
                // A folder that cannot be watched only loses the automatic refresh.
                KubunoLog.WriteException($"Solution Explorer: watching {path}", exception);
            }
        }

        public void Dispose()
        {
            _watcher?.Dispose();
            _timer.Dispose();
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
}
