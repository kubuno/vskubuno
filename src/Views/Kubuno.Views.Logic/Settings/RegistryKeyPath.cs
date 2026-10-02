using System;
using System.Collections.Generic;
using System.Linq;

namespace Kubuno.Views.Logic.Settings
{
    /// <summary>
    /// The paths of the Registry key picker of a <c>&lt;RegistryKey&gt;</c> (docs/STORAGE-COMPONENTS.md §5.2): the hive
    /// names a view writes (<c>CurrentUser</c>…), their short and long Windows spellings, and a path typed or pasted in
    /// any of the usual forms (<c>HKEY_LOCAL_MACHINE\SOFTWARE\x</c>, <c>HKLM/SOFTWARE/x/</c>) normalized to what the
    /// component's <c>Path</c> holds (no hive, backslashes, no empty segment).
    /// </summary>
    public static class RegistryKeyPath
    {
        /// <summary>The hives, in the order the picker shows them: the view's name, the short and the long spelling.</summary>
        public static readonly IReadOnlyList<(string Name, string Short, string Long)> Hives = new[]
        {
            ("CurrentUser", "HKCU", "HKEY_CURRENT_USER"),
            ("LocalMachine", "HKLM", "HKEY_LOCAL_MACHINE"),
            ("ClassesRoot", "HKCR", "HKEY_CLASSES_ROOT"),
            ("Users", "HKU", "HKEY_USERS"),
            ("CurrentConfig", "HKCC", "HKEY_CURRENT_CONFIG"),
        };

        /// <summary>The WOW64 views a view writes.</summary>
        public static readonly IReadOnlyList<string> Views = new[] { "Default", "Registry32", "Registry64" };

        /// <summary>The view's hive name of any spelling (<c>HKLM</c>, <c>HKEY_LOCAL_MACHINE</c>, <c>LocalMachine</c>), null when none.</summary>
        public static string? HiveName(string? text)
        {
            var t = (text ?? string.Empty).Trim();
            foreach (var (name, shortName, longName) in Hives)
            {
                if (string.Equals(t, name, StringComparison.OrdinalIgnoreCase) || string.Equals(t, shortName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(t, longName, StringComparison.OrdinalIgnoreCase))
                {
                    return name;
                }
            }

            return null;
        }

        /// <summary>The short spelling of a view's hive name (<c>CurrentUser</c> → <c>HKCU</c>).</summary>
        public static string ShortName(string hive) => Hives.FirstOrDefault(h => h.Name == hive).Short ?? hive;

        /// <summary>
        /// Splits a typed or pasted path: its hive when it starts with one (else null), and the path below it with
        /// backslashes and no empty segment.
        /// </summary>
        public static (string? Hive, string Path) Parse(string? text)
        {
            var segments = (text ?? string.Empty).Trim().Replace('/', '\\').Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            if (segments.Count > 0 && HiveName(segments[0]) is { } hive)
            {
                segments.RemoveAt(0);
                return (hive, string.Join("\\", segments));
            }

            return (null, string.Join("\\", segments));
        }

        /// <summary><c>parent\child</c> (either may be empty).</summary>
        public static string Join(string parent, string child) => parent.Length == 0 ? child : child.Length == 0 ? parent : parent + "\\" + child;

        /// <summary>The full display form: <c>HKCU\Software\x</c>.</summary>
        public static string Display(string hive, string path) => path.Length == 0 ? ShortName(hive) : ShortName(hive) + "\\" + path;

        /// <summary>The segments of a path, for expanding the picker's tree down to it.</summary>
        public static IReadOnlyList<string> Segments(string path) => Parse(path).Path.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
    }
}
