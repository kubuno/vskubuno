using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Cargo.Metadata;
using Kubuno.Cargo.Processes;
using Kubuno.VisualStudio.Core.SolutionExplorer;
using Kubuno.VisualStudio.Logging;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;

namespace Kubuno.VisualStudio.SolutionExplorer
{
    /// <summary>
    /// The symbols attached under one <c>.rs</c>/<c>.kbview</c> file node (docs/RSPROJ.md lot 8).
    /// Lazy: nothing runs until Solution Explorer asks for <see cref="Items"/> (the node is expanded
    /// or searched); from then on the file is watched and re-queried, debounced, whenever it is
    /// saved - the tree is merged in place so expanded nodes stay expanded.
    /// </summary>
    internal sealed class FileSymbolsSource : IAttachedCollectionSource, INotifyPropertyChanged, IDisposable
    {
        private readonly ObservableCollection<SymbolTreeItem> _items = new ObservableCollection<SymbolTreeItem>();
        private readonly string _path;
        private readonly ISymbolQuery _query;
        private DebouncedFileWatcher? _watcher;
        private CancellationTokenSource? _pending;
        private bool _started;
        private bool _loaded;
        private bool _disposed;

        public FileSymbolsSource(object sourceItem, string path, ISymbolQuery query)
        {
            SourceItem = sourceItem;
            _path = path;
            _query = query;
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
            SymbolTreeMerger.Merge(_items, _path, symbols);
            if (hadItems != HasItems)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasItems)));
            }
        }
    }

    /// <summary>The "Dependencies" node attached under a <c>.rsproj</c> project node.</summary>
    internal sealed class ProjectDependenciesSource : IAttachedCollectionSource, IDisposable
    {
        private readonly IVsHierarchy _project;
        private readonly DependenciesTreeItem _node;
        private DebouncedFileWatcher? _watcher;
        private bool _started;

        public ProjectDependenciesSource(object sourceItem, IVsHierarchy project)
        {
            SourceItem = sourceItem;
            _project = project;
            _node = new DependenciesTreeItem(EnsureLoaded);
            Items = new[] { _node };
        }

        public object SourceItem { get; }

        public bool HasItems => true;

        public IEnumerable Items { get; }

        public void Dispose() => _watcher?.Dispose();

        private void EnsureLoaded()
        {
            if (_started)
            {
                return;
            }

            _started = true;
            _ = LoadAsync(watch: true);
        }

        private async Task LoadAsync(bool watch)
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                var manifest = GetProperty("CargoManifestPath");
                var packageName = GetProperty("CargoPackage");
                if (string.IsNullOrEmpty(manifest) && _project.GetCanonicalName((uint)VSConstants.VSITEMID.Root, out var projectFile) == VSConstants.S_OK)
                {
                    manifest = Path.Combine(Path.GetDirectoryName(projectFile) ?? string.Empty, "Cargo.toml");
                }

                if (string.IsNullOrEmpty(manifest) || !File.Exists(manifest))
                {
                    _node.SetGroups(Array.Empty<CargoDependencyGroup>());
                    return;
                }

                if (watch)
                {
                    _watcher = new DebouncedFileWatcher(manifest!, () => _ = LoadAsync(watch: false));
                }

                await TaskScheduler.Default;
                var metadata = await new CargoMetadataReader(new ProcessRunner())
                    .ReadAsync(Path.GetDirectoryName(manifest)!, manifest);
                var package = CargoDependencyGroups.FindPackage(metadata, manifest, packageName);
                var groups = package != null ? CargoDependencyGroups.Group(package) : Array.Empty<CargoDependencyGroup>();

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                _node.SetGroups(groups);
            }
            catch (Exception exception)
            {
                KubunoLog.WriteException("Solution Explorer: reading the Cargo dependencies", exception);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                _node.SetGroups(Array.Empty<CargoDependencyGroup>());
            }
        }

        private string? GetProperty(string name)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return _project is IVsBuildPropertyStorage storage
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
