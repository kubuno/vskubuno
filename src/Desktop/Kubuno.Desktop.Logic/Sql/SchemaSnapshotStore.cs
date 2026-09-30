using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace Kubuno.Desktop.Logic.Sql
{
    /// <summary>What changed in the schema snapshot store.</summary>
    public sealed class SchemaSnapshotChangedEventArgs : EventArgs
    {
        public SchemaSnapshotChangedEventArgs(string root, string? connection, string path)
        {
            Root = root;
            Connection = connection;
            Path = path;
        }

        /// <summary>The workspace/solution directory whose <c>.vs\kubuno\schema</c> changed.</summary>
        public string Root { get; }

        /// <summary>The connection whose snapshot changed (null when unknown, e.g. a rename seen by the watcher).</summary>
        public string? Connection { get; }

        /// <summary>The snapshot file.</summary>
        public string Path { get; }
    }

    /// <summary>
    /// The cached schema snapshots of the Data Explorer connections (docs/DATA.md §9, DATA-8):
    /// <c>&lt;workspace or solution dir&gt;\.vs\kubuno\schema\&lt;connection&gt;.json</c>, each holding exactly the JSON of
    /// <c>kubuno-data-tool</c>'s <c>schema.load</c>. The Data Explorer and the Data Sources wizard write them
    /// (<see cref="Write"/>); the SQL IntelliSense of Rust strings reads them and follows <see cref="Changed"/>.
    /// Nothing secret is ever stored there: a schema has no connection string.
    /// </summary>
    public static class SchemaSnapshotStore
    {
        /// <summary>The snapshot directory, relative to the workspace/solution directory.</summary>
        public const string RelativeDirectory = @".vs\kubuno\schema";

        private static readonly object Gate = new object();
        private static readonly Dictionary<string, WatchEntry> Watchers = new Dictionary<string, WatchEntry>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Raised after <see cref="Write"/> / <see cref="Delete"/> in this process, and when a watched directory
        /// (<see cref="Watch"/>) sees a snapshot created, changed, renamed or deleted by anyone. Raised on a background
        /// thread; handlers must be quick and thread-safe.
        /// </summary>
        public static event EventHandler<SchemaSnapshotChangedEventArgs>? Changed;

        /// <summary><c>&lt;root&gt;\.vs\kubuno\schema</c>.</summary>
        public static string GetSchemaDirectory(string root)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                throw new ArgumentException("A workspace or solution directory is required.", nameof(root));
            }

            return Path.Combine(Path.GetFullPath(root), RelativeDirectory);
        }

        /// <summary><c>&lt;root&gt;\.vs\kubuno\schema\&lt;connection&gt;.json</c> (the name made file-safe by <see cref="ToFileName"/>).</summary>
        public static string GetSnapshotPath(string root, string connection) => Path.Combine(GetSchemaDirectory(root), ToFileName(connection) + ".json");

        /// <summary>
        /// The file name (without extension) of a connection's snapshot: the name itself when it only uses the characters a
        /// Data Explorer name allows (<c>[A-Za-z0-9 _.-]</c>, 1-64), otherwise every other character replaced by <c>_</c>.
        /// </summary>
        public static string ToFileName(string connection)
        {
            if (string.IsNullOrWhiteSpace(connection))
            {
                throw new ArgumentException("A connection name is required.", nameof(connection));
            }

            var builder = new StringBuilder(connection.Length);
            foreach (char c in connection.Trim())
            {
                builder.Append(char.IsLetterOrDigit(c) && c < 128 || c == ' ' || c == '_' || c == '.' || c == '-' ? c : '_');
            }

            var name = builder.ToString().Trim('.', ' ');
            return name.Length == 0 ? "_" : name.Length > 64 ? name.Substring(0, 64) : name;
        }

        /// <summary>The snapshot of <paramref name="connection"/>, or null when there is none or it cannot be read.</summary>
        public static SchemaSnapshot? TryRead(string root, string connection) => TryReadFile(GetSnapshotPath(root, connection), connection);

        /// <summary>Reads a snapshot file, or null when it is missing, unreadable or not a snapshot.</summary>
        public static SchemaSnapshot? TryReadFile(string path, string connection)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    if (!File.Exists(path))
                    {
                        return null;
                    }

                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var reader = new StreamReader(stream, Encoding.UTF8, true);
                    return SchemaSnapshot.Parse(reader.ReadToEnd(), connection);
                }
                catch (IOException)
                {
                    // Being written (the writer replaces the file atomically, but a reader may still collide): retry briefly.
                    Thread.Sleep(30);
                }
                catch (Exception exception) when (exception is UnauthorizedAccessException or JsonException or ArgumentException)
                {
                    return null;
                }
            }

            return null;
        }

        /// <summary>The connections that have a snapshot under <paramref name="root"/> (from the file names).</summary>
        public static IReadOnlyList<string> List(string root)
        {
            var directory = GetSchemaDirectory(root);
            try
            {
                return Directory.Exists(directory)
                    ? Directory.EnumerateFiles(directory, "*.json").Select(Path.GetFileNameWithoutExtension).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList()
                    : (IReadOnlyList<string>)new string[0];
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return new string[0];
            }
        }

        /// <summary>
        /// Stores <paramref name="schemaLoadJson"/> (a <c>schema.load</c> result) as the snapshot of <paramref name="connection"/>:
        /// validated first (throws <see cref="JsonException"/> when it is not a snapshot), written to a temporary file then
        /// moved over the old one, so readers never see half a file. Raises <see cref="Changed"/>. Returns the path.
        /// </summary>
        public static string Write(string root, string connection, string schemaLoadJson)
        {
            if (schemaLoadJson is null)
            {
                throw new ArgumentNullException(nameof(schemaLoadJson));
            }

            SchemaSnapshot.Parse(schemaLoadJson, connection);
            var path = GetSnapshotPath(root, connection);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp";
            File.WriteAllText(temporary, schemaLoadJson, new UTF8Encoding(false));
            try
            {
                if (File.Exists(path))
                {
                    File.Replace(temporary, path, null, true);
                }
                else
                {
                    File.Move(temporary, path);
                }
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }

            Raise(new SchemaSnapshotChangedEventArgs(Path.GetFullPath(root), connection, path));
            return path;
        }

        /// <summary>Removes the snapshot of <paramref name="connection"/> (e.g. the connection was deleted). Raises <see cref="Changed"/>.</summary>
        public static void Delete(string root, string connection)
        {
            var path = GetSnapshotPath(root, connection);
            if (File.Exists(path))
            {
                File.Delete(path);
                Raise(new SchemaSnapshotChangedEventArgs(Path.GetFullPath(root), connection, path));
            }
        }

        /// <summary>
        /// Watches <c>&lt;root&gt;\.vs\kubuno\schema</c> (created when missing) and raises <see cref="Changed"/> for each
        /// snapshot written there by another process or instance. Dispose the result to stop; watchers of the same root
        /// are shared (reference-counted).
        /// </summary>
        public static IDisposable Watch(string root)
        {
            var fullRoot = Path.GetFullPath(root);
            lock (Gate)
            {
                if (!Watchers.TryGetValue(fullRoot, out var entry))
                {
                    entry = new WatchEntry(fullRoot);
                    Watchers[fullRoot] = entry;
                }

                entry.References++;
                return new WatchHandle(fullRoot);
            }
        }

        /// <summary>
        /// The directory whose <c>.vs\kubuno\schema</c> serves <paramref name="filePath"/>: the first candidate (the Open Folder
        /// workspace, the solution directory...) that contains the file; else the nearest ancestor of the file that already
        /// has a snapshot directory; else the first candidate; else the file's Cargo workspace root (the topmost directory
        /// with a <c>Cargo.toml</c>). Null when nothing fits.
        /// </summary>
        public static string? ResolveRoot(string filePath, IEnumerable<string?>? candidates)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return null;
            }

            var full = Path.GetFullPath(filePath);
            var list = (candidates ?? Enumerable.Empty<string?>()).Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => Path.GetFullPath(c!)).ToList();
            foreach (var candidate in list)
            {
                if (IsUnder(full, candidate))
                {
                    return candidate;
                }
            }

            string? topCargo = null;
            for (var directory = Path.GetDirectoryName(full); !string.IsNullOrEmpty(directory); directory = Path.GetDirectoryName(directory))
            {
                if (Directory.Exists(Path.Combine(directory, RelativeDirectory)))
                {
                    return directory;
                }

                if (File.Exists(Path.Combine(directory, "Cargo.toml")))
                {
                    topCargo = directory;
                }
            }

            return list.FirstOrDefault() ?? topCargo;
        }

        internal static bool IsUnder(string path, string directory)
        {
            var prefix = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static void Raise(SchemaSnapshotChangedEventArgs args)
        {
            try
            {
                Changed?.Invoke(null, args);
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                // A listener failing to reload must not fail the writer.
            }
        }

        private sealed class WatchHandle : IDisposable
        {
            private string? _root;

            public WatchHandle(string root)
            {
                _root = root;
            }

            public void Dispose()
            {
                var root = Interlocked.Exchange(ref _root, null);
                if (root is null)
                {
                    return;
                }

                lock (Gate)
                {
                    if (Watchers.TryGetValue(root, out var entry) && --entry.References <= 0)
                    {
                        Watchers.Remove(root);
                        entry.Dispose();
                    }
                }
            }
        }

        private sealed class WatchEntry : IDisposable
        {
            private readonly string _root;
            private readonly FileSystemWatcher? _watcher;

            public WatchEntry(string root)
            {
                _root = root;
                try
                {
                    var directory = GetSchemaDirectory(root);
                    Directory.CreateDirectory(directory);
                    _watcher = new FileSystemWatcher(directory, "*.json")
                    {
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                        IncludeSubdirectories = false,
                    };
                    _watcher.Changed += OnChanged;
                    _watcher.Created += OnChanged;
                    _watcher.Deleted += OnChanged;
                    _watcher.Renamed += (_, e) =>
                    {
                        OnChanged(null, new FileSystemEventArgs(WatcherChangeTypes.Deleted, Path.GetDirectoryName(e.OldFullPath) ?? string.Empty, e.OldName));
                        OnChanged(null, e);
                    };
                    _watcher.EnableRaisingEvents = true;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    _watcher = null;
                }
            }

            public int References { get; set; }

            public void Dispose() => _watcher?.Dispose();

            private void OnChanged(object? sender, FileSystemEventArgs e)
            {
                if (!e.FullPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                Raise(new SchemaSnapshotChangedEventArgs(_root, Path.GetFileNameWithoutExtension(e.FullPath), e.FullPath));
            }
        }
    }
}
