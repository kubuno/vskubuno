using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Kubuno.Desktop.Logic.Resources
{
    /// <summary>One resource of a project, as the Select Resource dialog, the icon picker and F12 see it.</summary>
    public sealed class ResourceItem
    {
        public ResourceItem(string set, string neutralPath, ResourceEntry entry)
        {
            Set = set;
            NeutralPath = neutralPath;
            Entry = entry;
        }

        /// <summary>The resource name (<c>{Res Key}</c>).</summary>
        public string Key => Entry.Name;

        /// <summary>The set: the <c>.kbres</c> file stem (<c>Source=</c>).</summary>
        public string Set { get; }

        public string NeutralPath { get; }

        public ResourceEntry Entry { get; }

        public ResourceKind Kind => Entry.Kind;

        /// <summary>The full path of a linked entry's file; null otherwise.</summary>
        public string? FilePath => Entry.Persistence == ResourcePersistence.Linked
            ? Path.GetFullPath(Path.Combine(Path.GetDirectoryName(NeutralPath) ?? string.Empty, Entry.Path.Replace('/', Path.DirectorySeparatorChar)))
            : null;

        /// <summary>The bytes of a binary entry (embedded, or the linked file read now); null when unreadable or text.</summary>
        public byte[]? ReadBytes()
        {
            if (Entry.Persistence == ResourcePersistence.Embedded)
            {
                return Entry.Bytes;
            }

            try
            {
                return FilePath is { } p && File.Exists(p) ? File.ReadAllBytes(p) : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>The value written in a view: <c>{Res key}</c>, with <c>Source=</c> when another set has the same key.</summary>
        public string Reference(bool qualified) => ResourceNames.Reference(Key, qualified ? Set : null);
    }

    /// <summary>Finds the resource files of the project holding a file (the nearest folder with a Cargo.toml).</summary>
    public static class ProjectResources
    {
        private static readonly string[] SkippedFolders = { "target", "bin", "obj", ".git", ".vs", "node_modules" };

        /// <summary>The project folder of <paramref name="file"/>: the nearest one with a Cargo.toml, else the file's folder.</summary>
        public static string ProjectRoot(string file)
        {
            var start = Directory.Exists(file) ? file : Path.GetDirectoryName(Path.GetFullPath(file)) ?? string.Empty;
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Cargo.toml")))
                {
                    return dir.FullName;
                }
            }

            return start;
        }

        /// <summary>The neutral <c>.kbres</c> files under <paramref name="root"/> (build folders skipped), sorted.</summary>
        public static IReadOnlyList<string> NeutralFiles(string root, int limit = 200)
        {
            var found = new List<string>();
            void Walk(string folder, int depth)
            {
                if (found.Count >= limit || depth > 8)
                {
                    return;
                }

                try
                {
                    foreach (var f in Directory.EnumerateFiles(folder, "*" + ResourceNames.Extension))
                    {
                        if (ResourceNames.SplitFileName(Path.GetFileName(f)) is { Culture: null })
                        {
                            found.Add(f);
                        }
                    }

                    foreach (var sub in Directory.EnumerateDirectories(folder))
                    {
                        if (!SkippedFolders.Contains(Path.GetFileName(sub), StringComparer.OrdinalIgnoreCase))
                        {
                            Walk(sub, depth + 1);
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }

            if (Directory.Exists(root))
            {
                Walk(root, 0);
            }

            found.Sort(StringComparer.OrdinalIgnoreCase);
            return found;
        }

        /// <summary>Every resource of the project of <paramref name="file"/>; <paramref name="readText"/> may give a file's unsaved text.</summary>
        public static IReadOnlyList<ResourceItem> Items(string file, Func<string, string?>? readText = null)
        {
            var items = new List<ResourceItem>();
            foreach (var neutral in NeutralFiles(ProjectRoot(file)))
            {
                string? text = readText?.Invoke(neutral);
                if (text is null)
                {
                    try
                    {
                        text = File.ReadAllText(neutral);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        continue;
                    }
                }

                var set = ResourceNames.SplitFileName(Path.GetFileName(neutral))?.Stem ?? string.Empty;
                items.AddRange(KbresFile.Parse(text).Entries.Select(e => new ResourceItem(set, neutral, e)));
            }

            return items;
        }

        /// <summary>The resource a <c>{Res …}</c> value names among <paramref name="items"/> (its set first when given).</summary>
        public static ResourceItem? Find(IEnumerable<ResourceItem> items, string? value)
        {
            if (ResourceNames.ParseReference(value) is not { } r)
            {
                return null;
            }

            return items.FirstOrDefault(i => i.Key == r.Key && (r.Set is null || string.Equals(i.Set, r.Set, StringComparison.OrdinalIgnoreCase)));
        }
    }
}
