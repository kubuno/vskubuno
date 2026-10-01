using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Kubuno.Desktop.Designer.Icons
{
    /// <summary>
    /// The icons chosen last in the icon picker, most recent first (at most <see cref="Capacity"/>), kept for the user in
    /// <c>%LOCALAPPDATA%\Kubuno\vskubuno\recent-icons.txt</c> - one value per line.
    /// </summary>
    public sealed class RecentIcons
    {
        public const int Capacity = 24;

        private readonly string _file;
        private List<string>? _items;

        public RecentIcons(string? file = null)
        {
            _file = file ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kubuno", "vskubuno", "recent-icons.txt");
        }

        /// <summary>The user's list.</summary>
        public static RecentIcons Default { get; } = new RecentIcons();

        public IReadOnlyList<string> Items => _items ??= Load();

        /// <summary>Puts <paramref name="value"/> first (a glyph name or a file); an empty value or a binding is not kept.</summary>
        public void Add(string? value)
        {
            var v = (value ?? string.Empty).Trim();
            var kind = IconValue.Classify(v);
            if (kind is IconValueKind.None or IconValueKind.Binding)
            {
                return;
            }

            var list = Items.Where(i => !string.Equals(i, v, StringComparison.Ordinal)).ToList();
            list.Insert(0, v);
            _items = list.Take(Capacity).ToList();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
                File.WriteAllLines(_file, _items);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A read-only profile keeps the list for this session only.
            }
        }

        private List<string> Load()
        {
            try
            {
                return File.Exists(_file)
                    ? File.ReadAllLines(_file).Select(l => l.Trim()).Where(l => l.Length > 0).Distinct(StringComparer.Ordinal).Take(Capacity).ToList()
                    : new List<string>();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new List<string>();
            }
        }
    }
}
