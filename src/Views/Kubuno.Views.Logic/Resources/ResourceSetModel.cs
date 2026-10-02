using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Kubuno.Views.Logic.Resources
{
    /// <summary>The file operations the resource editor needs (a fake in tests).</summary>
    public interface IResourceFileSystem
    {
        bool FileExists(string path);

        byte[] ReadAllBytes(string path);

        void WriteAllBytes(string path, byte[] bytes);

        void CopyFile(string from, string to);
    }

    /// <summary>The real file system.</summary>
    public sealed class DiskResourceFileSystem : IResourceFileSystem
    {
        public static readonly DiskResourceFileSystem Instance = new DiskResourceFileSystem();

        public bool FileExists(string path) => File.Exists(path);

        public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);

        public void WriteAllBytes(string path, byte[] bytes)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            File.WriteAllBytes(path, bytes);
        }

        public void CopyFile(string from, string to)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(to) ?? ".");
            File.Copy(from, to, overwrite: false);
        }
    }

    /// <summary>The editor category an entry is listed under (Visual Studio's resx editor: Strings, Images, Icons, Audio, Files, Other).</summary>
    public enum ResourceCategory
    {
        Strings,
        Images,
        Icons,
        Audio,
        Files,
        Other,
    }

    /// <summary>
    /// The model of the resource editor (docs/RESOURCES.md): a neutral <c>.kbres</c> file and its satellites, edited
    /// together - a rename or a removal applies to every culture, a translation goes to its culture's file - with an
    /// undo/redo history of whole-set snapshots (one step per gesture, whatever the number of files it touched).
    /// The Visual Studio editor keeps one text buffer per file in sync with <see cref="Texts"/>.
    /// </summary>
    public sealed class ResourceSetModel
    {
        private readonly IResourceFileSystem _fs;
        private readonly Stack<Snapshot> _undo = new Stack<Snapshot>();
        private readonly Stack<Snapshot> _redo = new Stack<Snapshot>();
        private KbresFile _neutral;
        private SortedDictionary<string, KbresFile> _satellites;

        public ResourceSetModel(string neutralPath, string neutralText, IEnumerable<(string Culture, string Text)> satellites, IResourceFileSystem? fs = null)
        {
            NeutralPath = neutralPath;
            _fs = fs ?? DiskResourceFileSystem.Instance;
            _neutral = KbresFile.Read(neutralText, out var diagnostics);
            Diagnostics = diagnostics;
            _satellites = new SortedDictionary<string, KbresFile>(StringComparer.Ordinal);
            foreach (var (culture, text) in satellites)
            {
                _satellites[ResourceNames.CanonicalCulture(culture)] = KbresFile.Parse(text);
            }
        }

        /// <summary>Raised after every change (an edit, an undo, a redo, a reload).</summary>
        public event EventHandler? Changed;

        public string NeutralPath { get; }

        /// <summary>The folder linked paths are relative to.</summary>
        public string Directory => Path.GetDirectoryName(NeutralPath) ?? string.Empty;

        /// <summary>The set's name: the neutral file's stem.</summary>
        public string SetName => ResourceNames.SplitFileName(Path.GetFileName(NeutralPath))?.Stem ?? Path.GetFileNameWithoutExtension(NeutralPath);

        /// <summary>Problems of the neutral file as last loaded.</summary>
        public IReadOnlyList<ResourceDiagnostic> Diagnostics { get; private set; }

        public IReadOnlyList<ResourceEntry> Entries => _neutral.Entries;

        public IReadOnlyList<string> Cultures => _satellites.Keys.ToList();

        public bool CanUndo => _undo.Count > 0;

        public bool CanRedo => _redo.Count > 0;

        /// <summary>The path of a culture's file (<c>null</c> = the neutral file).</summary>
        public string PathOf(string? culture) => culture is null ? NeutralPath : Path.Combine(Directory, SetName + "." + culture + ResourceNames.Extension);

        /// <summary>The text of every file of the set: the neutral file and each satellite, canonical.</summary>
        public IReadOnlyDictionary<string, string> Texts()
        {
            var texts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [NeutralPath] = _neutral.ToText() };
            foreach (var pair in _satellites)
            {
                texts[PathOf(pair.Key)] = pair.Value.ToText();
            }

            return texts;
        }

        public ResourceEntry? Get(string name) => _neutral.Get(name);

        /// <summary>The value of <paramref name="name"/> written in <paramref name="culture"/>'s file (null when not translated there).</summary>
        public ResourceEntry? Translation(string name, string culture) => _satellites.TryGetValue(culture, out var f) ? f.Get(name) : null;

        /// <summary>The String entries a culture does not translate.</summary>
        public IReadOnlyList<string> MissingTranslations(string culture) =>
            _satellites.TryGetValue(culture, out var f) ? _neutral.Entries.Where(e => e.Kind == ResourceKind.String && f.Get(e.Name) is null).Select(e => e.Name).ToList() : Array.Empty<string>();

        public static ResourceCategory CategoryOf(ResourceKind kind) => kind switch
        {
            ResourceKind.String => ResourceCategory.Strings,
            ResourceKind.Image => ResourceCategory.Images,
            ResourceKind.Icon => ResourceCategory.Icons,
            ResourceKind.Audio => ResourceCategory.Audio,
            ResourceKind.File => ResourceCategory.Files,
            _ => ResourceCategory.Other,
        };

        public IEnumerable<ResourceEntry> InCategory(ResourceCategory category) => _neutral.Entries.Where(e => CategoryOf(e.Kind) == category);

        /// <summary>A free name starting with <paramref name="baseName"/> (<c>String1</c>, <c>String2</c>…).</summary>
        public string UniqueName(string baseName)
        {
            var clean = new string(baseName.Select(c => char.IsLetterOrDigit(c) && c < 128 || c == '_' ? c : '_').ToArray());
            if (clean.Length == 0 || !(char.IsLetter(clean[0]) || clean[0] == '_'))
            {
                clean = "_" + clean;
            }

            if (_neutral.Get(clean) is null && KbresFile.IsValidName(clean))
            {
                return clean;
            }

            for (var i = 2; ; i++)
            {
                var candidate = clean + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (_neutral.Get(candidate) is null)
                {
                    return candidate;
                }
            }
        }

        /// <summary>The first free <c>String1</c>, <c>String2</c>… (new entries, like the resx editor).</summary>
        public string NumberedName(string prefix)
        {
            for (var i = 1; ; i++)
            {
                var candidate = prefix + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (_neutral.Get(candidate) is null)
                {
                    return candidate;
                }
            }
        }

        // ---- Edits (each one undoable step) ----

        public ResourceEntry AddString(string? name = null, string value = "", string? comment = null)
        {
            var entry = ResourceEntry.String(name ?? NumberedName("String"), value, comment);
            Edit(() => Add(entry));
            return entry;
        }

        public ResourceEntry AddText(ResourceKind kind, string name, string value, string? comment = null)
        {
            if (kind == ResourceKind.Color && !KbresFile.IsColor(value))
            {
                throw new ArgumentException("not a colour: " + value);
            }

            var entry = new ResourceEntry(name, kind, ResourcePersistence.Text, text: value, comment: comment);
            Edit(() => Add(entry));
            return entry;
        }

        /// <summary>
        /// Adds the file <paramref name="sourcePath"/> (Add Existing File, a drop): linked - copied into the set's
        /// <c>Resources</c> folder when it is outside the set's folder, like Visual Studio's resx editor - or embedded.
        /// </summary>
        public ResourceEntry AddFile(string sourcePath, ResourcePersistence persistence = ResourcePersistence.Linked, string? name = null)
        {
            var ext = Path.GetExtension(sourcePath).TrimStart('.').ToLowerInvariant();
            var kind = KbresFile.KindForExtension(ext);
            var entryName = name ?? UniqueName(Path.GetFileNameWithoutExtension(sourcePath));
            ResourceEntry entry;
            if (persistence == ResourcePersistence.Embedded)
            {
                entry = ResourceEntry.Embedded(kind, entryName, ext, _fs.ReadAllBytes(sourcePath));
            }
            else
            {
                var full = Path.GetFullPath(sourcePath);
                var dir = AppendSeparator(Path.GetFullPath(Directory));
                if (!full.StartsWith(dir, StringComparison.OrdinalIgnoreCase))
                {
                    var target = FreePath(Path.Combine(Directory, "Resources", Path.GetFileName(sourcePath)));
                    _fs.CopyFile(sourcePath, target);
                    full = Path.GetFullPath(target);
                }

                entry = ResourceEntry.Linked(kind, entryName, Relative(full));
            }

            Edit(() => Add(entry));
            return entry;
        }

        public void Remove(string name) => Edit(() =>
        {
            _neutral.Entries.RemoveAll(e => e.Name == name);
            foreach (var f in _satellites.Values)
            {
                f.Entries.RemoveAll(e => e.Name == name);
            }
        });

        /// <summary>Renames an entry in every culture; throws when the new name is invalid or taken.</summary>
        public void Rename(string oldName, string newName)
        {
            if (oldName == newName)
            {
                return;
            }

            if (!KbresFile.IsValidName(newName))
            {
                throw new ArgumentException("`" + newName + "` is not a valid resource name");
            }

            if (_neutral.Get(newName) is not null)
            {
                throw new ArgumentException("a resource named `" + newName + "` already exists");
            }

            Edit(() =>
            {
                foreach (var f in new[] { _neutral }.Concat(_satellites.Values))
                {
                    var i = f.IndexOf(oldName);
                    if (i >= 0)
                    {
                        f.Entries[i] = f.Entries[i].WithName(newName);
                    }
                }
            });
        }

        /// <summary>
        /// Sets the text of a String/Color/Font entry in <paramref name="culture"/>'s file (null = neutral). An empty value
        /// in a satellite removes the translation (the neutral value shows again); a new culture file is created as needed.
        /// </summary>
        public void SetValue(string name, string? culture, string value)
        {
            var neutral = _neutral.Get(name) ?? throw new ArgumentException("no resource named `" + name + "`");
            if (neutral.Kind == ResourceKind.Color && value.Length > 0 && !KbresFile.IsColor(value))
            {
                throw new ArgumentException("not a colour: " + value);
            }

            Edit(() =>
            {
                if (culture is null)
                {
                    _neutral.Entries[_neutral.IndexOf(name)] = neutral.WithText(value);
                    return;
                }

                var file = Satellite(culture);
                var i = file.IndexOf(name);
                if (value.Length == 0)
                {
                    if (i >= 0)
                    {
                        file.Entries.RemoveAt(i);
                    }

                    return;
                }

                var translated = new ResourceEntry(name, neutral.Kind, ResourcePersistence.Text, text: value);
                if (i >= 0)
                {
                    file.Entries[i] = file.Entries[i].WithText(value);
                }
                else
                {
                    InsertInNeutralOrder(file, translated);
                }
            });
        }

        /// <summary>Gives a culture its own file for a binary entry (a localised image), linked like the neutral one.</summary>
        public void SetFileTranslation(string name, string culture, string sourcePath)
        {
            var neutral = _neutral.Get(name) ?? throw new ArgumentException("no resource named `" + name + "`");
            var full = Path.GetFullPath(sourcePath);
            var dir = AppendSeparator(Path.GetFullPath(Directory));
            if (!full.StartsWith(dir, StringComparison.OrdinalIgnoreCase))
            {
                var target = FreePath(Path.Combine(Directory, "Resources", Path.GetFileNameWithoutExtension(sourcePath) + "." + culture + Path.GetExtension(sourcePath)));
                _fs.CopyFile(sourcePath, target);
                full = Path.GetFullPath(target);
            }

            Edit(() =>
            {
                var file = Satellite(culture);
                file.Entries.RemoveAll(e => e.Name == name);
                InsertInNeutralOrder(file, ResourceEntry.Linked(neutral.Kind, name, Relative(full)));
            });
        }

        public void SetComment(string name, string? comment) => Edit(() =>
        {
            var i = _neutral.IndexOf(name);
            if (i >= 0)
            {
                _neutral.Entries[i] = _neutral.Entries[i].WithComment(comment);
            }
        });

        /// <summary>
        /// Switches a binary entry between linked and embedded (the resx editor's "Persistence"): embedding reads the
        /// linked file into the <c>.kbres</c> file; linking writes the bytes to <c>Resources/&lt;name&gt;.&lt;format&gt;</c>.
        /// </summary>
        public void SetPersistence(string name, ResourcePersistence persistence)
        {
            var e = _neutral.Get(name) ?? throw new ArgumentException("no resource named `" + name + "`");
            if (e.Persistence == persistence || e.Persistence == ResourcePersistence.Text || persistence == ResourcePersistence.Text)
            {
                return;
            }

            ResourceEntry replacement;
            if (persistence == ResourcePersistence.Embedded)
            {
                var bytes = _fs.ReadAllBytes(FullPath(e.Path));
                replacement = new ResourceEntry(e.Name, e.Kind, ResourcePersistence.Embedded, format: e.EffectiveFormat, bytes: bytes, comment: e.Comment, textFile: e.TextFile);
            }
            else
            {
                var target = FreePath(Path.Combine(Directory, "Resources", e.Name + "." + (e.Format.Length > 0 ? e.Format : "bin")));
                _fs.WriteAllBytes(target, e.Bytes);
                replacement = new ResourceEntry(e.Name, e.Kind, ResourcePersistence.Linked, path: Relative(Path.GetFullPath(target)), comment: e.Comment, textFile: e.TextFile);
            }

            Edit(() => _neutral.Entries[_neutral.IndexOf(name)] = replacement);
        }

        /// <summary>Adds an (empty) culture file - "Add culture" in the editor, or the localized item template.</summary>
        public void AddCulture(string culture)
        {
            if (!ResourceNames.IsCulture(culture))
            {
                throw new ArgumentException("`" + culture + "` is not a culture name (fr, fr-FR, zh-Hans…)");
            }

            Edit(() => Satellite(ResourceNames.CanonicalCulture(culture)));
        }

        /// <summary>Moves an entry to <paramref name="index"/> in the neutral file (order is the developer's).</summary>
        public void Move(string name, int index) => Edit(() =>
        {
            var i = _neutral.IndexOf(name);
            if (i < 0)
            {
                return;
            }

            var e = _neutral.Entries[i];
            _neutral.Entries.RemoveAt(i);
            _neutral.Entries.Insert(Math.Max(0, Math.Min(index, _neutral.Entries.Count)), e);
        });

        /// <summary>The bytes of a binary entry in <paramref name="culture"/> (fallback to neutral), for thumbnails; null when unreadable.</summary>
        public byte[]? BytesOf(string name, string? culture = null)
        {
            var e = (culture is null ? null : Translation(name, culture)) ?? _neutral.Get(name);
            if (e is null)
            {
                return null;
            }

            if (e.Persistence == ResourcePersistence.Embedded)
            {
                return e.Bytes;
            }

            if (e.Persistence == ResourcePersistence.Linked)
            {
                try
                {
                    var full = FullPath(e.Path);
                    return _fs.FileExists(full) ? _fs.ReadAllBytes(full) : null;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                {
                    return null;
                }
            }

            return null;
        }

        /// <summary>The full path of a linked entry's file.</summary>
        public string FullPath(string relative) => Path.GetFullPath(Path.Combine(Directory, relative.Replace('/', Path.DirectorySeparatorChar)));

        public void Undo()
        {
            if (_undo.Count == 0)
            {
                return;
            }

            _redo.Push(Capture());
            Restore(_undo.Pop());
        }

        public void Redo()
        {
            if (_redo.Count == 0)
            {
                return;
            }

            _undo.Push(Capture());
            Restore(_redo.Pop());
        }

        /// <summary>Reloads the files from text changed outside the editor (another editor, source control); clears no history.</summary>
        public void Reload(string neutralText, IEnumerable<(string Culture, string Text)> satellites)
        {
            _neutral = KbresFile.Read(neutralText, out var diagnostics);
            Diagnostics = diagnostics;
            _satellites = new SortedDictionary<string, KbresFile>(StringComparer.Ordinal);
            foreach (var (culture, text) in satellites)
            {
                _satellites[ResourceNames.CanonicalCulture(culture)] = KbresFile.Parse(text);
            }

            Changed?.Invoke(this, EventArgs.Empty);
        }

        // ---- Internals ----

        private void Add(ResourceEntry entry)
        {
            if (!KbresFile.IsValidName(entry.Name))
            {
                throw new ArgumentException("`" + entry.Name + "` is not a valid resource name");
            }

            if (_neutral.Get(entry.Name) is not null)
            {
                throw new ArgumentException("a resource named `" + entry.Name + "` already exists");
            }

            _neutral.Entries.Add(entry);
        }

        private KbresFile Satellite(string culture)
        {
            if (!_satellites.TryGetValue(culture, out var file))
            {
                file = new KbresFile();
                _satellites[culture] = file;
            }

            return file;
        }

        private void InsertInNeutralOrder(KbresFile file, ResourceEntry entry)
        {
            var order = _neutral.IndexOf(entry.Name);
            var at = file.Entries.FindIndex(x => _neutral.IndexOf(x.Name) > order);
            if (at < 0)
            {
                file.Entries.Add(entry);
            }
            else
            {
                file.Entries.Insert(at, entry);
            }
        }

        private string Relative(string full)
        {
            var baseUri = new Uri(AppendSeparator(Path.GetFullPath(Directory)));
            return Uri.UnescapeDataString(baseUri.MakeRelativeUri(new Uri(full)).ToString()).Replace('\\', '/');
        }

        private string FreePath(string path)
        {
            var dir = Path.GetDirectoryName(path) ?? string.Empty;
            var name = Path.GetFileNameWithoutExtension(path);
            var ext = Path.GetExtension(path);
            var candidate = path;
            for (var i = 2; _fs.FileExists(candidate); i++)
            {
                candidate = Path.Combine(dir, name + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + ext);
            }

            return candidate;
        }

        private static string AppendSeparator(string folder) => folder.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ? folder : folder + Path.DirectorySeparatorChar;

        /// <summary>Runs one edit as one undo step; on failure the set is left as it was.</summary>
        private void Edit(Action change)
        {
            var before = Capture();
            try
            {
                change();
            }
            catch
            {
                Restore(before, raise: false);
                throw;
            }

            _undo.Push(before);
            _redo.Clear();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private Snapshot Capture() => new Snapshot(_neutral.ToText(), _satellites.ToDictionary(p => p.Key, p => p.Value.ToText()));

        private void Restore(Snapshot s, bool raise = true)
        {
            _neutral = KbresFile.Parse(s.Neutral);
            _satellites = new SortedDictionary<string, KbresFile>(s.Satellites.ToDictionary(p => p.Key, p => KbresFile.Parse(p.Value)), StringComparer.Ordinal);
            if (raise)
            {
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }

        private sealed class Snapshot
        {
            public Snapshot(string neutral, Dictionary<string, string> satellites)
            {
                Neutral = neutral;
                Satellites = satellites;
            }

            public string Neutral { get; }

            public Dictionary<string, string> Satellites { get; }
        }
    }
}
