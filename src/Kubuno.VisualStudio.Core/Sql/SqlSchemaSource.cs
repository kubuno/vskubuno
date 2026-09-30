using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Kubuno.VisualStudio.Core.Sql
{
    /// <summary>
    /// The live schema of one crate: the snapshots (<see cref="SchemaSnapshotStore"/>) of the connections its
    /// <c>.kbdata</c> files name, merged into a <see cref="SqlSchemaIndex"/>. Loaded in the background, reloaded when a
    /// snapshot or a <c>.kbdata</c> changes, and <see cref="Changed"/> raised after each reload. One instance per
    /// (crate, root), shared by every open file of the crate.
    /// </summary>
    public sealed class SqlSchemaSource : IDisposable
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, SqlSchemaSource> Sources = new Dictionary<string, SqlSchemaSource>(StringComparer.OrdinalIgnoreCase);

        private readonly IDisposable _snapshotWatch;
        private readonly FileSystemWatcher? _kbdataWatcher;
        private readonly object _loadGate = new object();
        private volatile SqlSchemaIndex _index = SqlSchemaIndex.Empty;
        private IReadOnlyList<string> _connectionFiles = new string[0];
        private int _version;
        private bool _loadScheduled;
        private bool _loaded;

        private SqlSchemaSource(string crateDirectory, string root)
        {
            CrateDirectory = crateDirectory;
            Root = root;
            SchemaSnapshotStore.Changed += OnSnapshotChanged;
            _snapshotWatch = SchemaSnapshotStore.Watch(root);
            var source = Path.Combine(crateDirectory, "src");
            if (Directory.Exists(source))
            {
                try
                {
                    _kbdataWatcher = new FileSystemWatcher(source, "*.kbdata")
                    {
                        IncludeSubdirectories = true,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    };
                    _kbdataWatcher.Changed += (_, _) => ScheduleLoad();
                    _kbdataWatcher.Created += (_, _) => ScheduleLoad();
                    _kbdataWatcher.Deleted += (_, _) => ScheduleLoad();
                    _kbdataWatcher.Renamed += (_, _) => ScheduleLoad();
                    _kbdataWatcher.EnableRaisingEvents = true;
                }
                catch (Exception exception) when (exception is IOException or ArgumentException or UnauthorizedAccessException)
                {
                    _kbdataWatcher = null;
                }
            }
        }

        /// <summary>Raised (on a background thread) after the index was reloaded.</summary>
        public event EventHandler? Changed;

        public string CrateDirectory { get; }

        public string Root { get; }

        /// <summary>Incremented at each reload (a cache key for results computed from <see cref="Index"/>).</summary>
        public int Version => Volatile.Read(ref _version);

        /// <summary>The current index (empty until the first background load completes; that load starts on first use).</summary>
        public SqlSchemaIndex Index
        {
            get
            {
                if (!_loaded)
                {
                    ScheduleLoad();
                }

                return _index;
            }
        }

        /// <summary>The shared source of a crate, created on first use.</summary>
        public static SqlSchemaSource GetOrCreate(string crateDirectory, string root)
        {
            var key = Path.GetFullPath(crateDirectory) + "|" + Path.GetFullPath(root);
            lock (Gate)
            {
                if (!Sources.TryGetValue(key, out var source))
                {
                    source = new SqlSchemaSource(Path.GetFullPath(crateDirectory), Path.GetFullPath(root));
                    Sources[key] = source;
                }

                return source;
            }
        }

        /// <summary>The merged snapshots of the connections the crate's <c>.kbdata</c> files name (synchronous; tests and reload).</summary>
        public static SqlSchemaIndex Load(string crateDirectory, string root)
        {
            var snapshots = new List<SchemaSnapshot>();
            foreach (var connection in KbdataConnections.Read(crateDirectory))
            {
                var snapshot = SchemaSnapshotStore.TryRead(root, connection.Connection);
                if (snapshot != null)
                {
                    snapshots.Add(snapshot);
                }
            }

            return snapshots.Count == 0 ? SqlSchemaIndex.Empty : new SqlSchemaIndex(snapshots);
        }

        /// <summary>Loads now, on the calling thread (raises <see cref="Changed"/>).</summary>
        public void LoadNow()
        {
            var connections = KbdataConnections.Read(CrateDirectory);
            var snapshots = new List<SchemaSnapshot>();
            foreach (var connection in connections)
            {
                var snapshot = SchemaSnapshotStore.TryRead(Root, connection.Connection);
                if (snapshot != null)
                {
                    snapshots.Add(snapshot);
                }
            }

            lock (_loadGate)
            {
                _connectionFiles = connections.Select(c => SchemaSnapshotStore.ToFileName(c.Connection)).ToList();
                _index = snapshots.Count == 0 ? SqlSchemaIndex.Empty : new SqlSchemaIndex(snapshots);
                _loaded = true;
                Interlocked.Increment(ref _version);
            }

            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Stops watching and forgets the shared instance (the next <see cref="GetOrCreate"/> makes a new one).</summary>
        public void Dispose()
        {
            lock (Gate)
            {
                foreach (var pair in Sources.Where(p => ReferenceEquals(p.Value, this)).ToList())
                {
                    Sources.Remove(pair.Key);
                }
            }

            SchemaSnapshotStore.Changed -= OnSnapshotChanged;
            _snapshotWatch.Dispose();
            _kbdataWatcher?.Dispose();
        }

        private void OnSnapshotChanged(object? sender, SchemaSnapshotChangedEventArgs e)
        {
            if (!string.Equals(Path.GetFullPath(e.Root), Root, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            IReadOnlyList<string> files;
            lock (_loadGate)
            {
                files = _connectionFiles;
            }

            var name = e.Connection is null ? null : SchemaSnapshotStore.ToFileName(e.Connection);
            if (name is null || files.Contains(name, StringComparer.OrdinalIgnoreCase) || !_loaded)
            {
                ScheduleLoad();
            }
        }

        /// <summary>Loads in the background, coalescing the bursts of events a save produces.</summary>
        private void ScheduleLoad()
        {
            lock (_loadGate)
            {
                if (_loadScheduled)
                {
                    return;
                }

                _loadScheduled = true;
            }

            Task.Run(async () =>
            {
                await Task.Delay(150).ConfigureAwait(false);
                lock (_loadGate)
                {
                    _loadScheduled = false;
                }

                try
                {
                    LoadNow();
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    // The next change retries; the previous index stays.
                }
            });
        }
    }
}
