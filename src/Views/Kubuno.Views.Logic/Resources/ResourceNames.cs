using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Kubuno.Views.Logic.Resources
{
    /// <summary>
    /// Culture names of satellite files, generated Rust names and the <c>{Res …}</c> markup - the same rules as the
    /// Rust crates (<c>kubuno_resources_model::{culture, names}</c>, <c>kubuno_views::resources</c>).
    /// </summary>
    public static class ResourceNames
    {
        public const string Extension = ".kbres";

        /// <summary>A BCP-47 culture name: a 2-3 letter language, then 1-8 alphanumeric subtags.</summary>
        public static bool IsCulture(string? tag)
        {
            if (string.IsNullOrEmpty(tag))
            {
                return false;
            }

            var parts = tag!.Split('-');
            if (parts[0].Length < 2 || parts[0].Length > 3 || !parts[0].All(IsLetter))
            {
                return false;
            }

            return parts.Skip(1).All(p => p.Length >= 1 && p.Length <= 8 && p.All(c => IsLetter(c) || char.IsDigit(c)));
        }

        private static bool IsLetter(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

        /// <summary><c>FR-fr</c> → <c>fr-FR</c>, <c>zh_hans</c> → <c>zh-Hans</c>.</summary>
        public static string CanonicalCulture(string tag)
        {
            var parts = tag.Trim().Replace('_', '-').Split('-');
            for (var i = 0; i < parts.Length; i++)
            {
                var p = parts[i];
                if (i == 0)
                {
                    parts[i] = p.ToLowerInvariant();
                }
                else if (p.Length == 4 && p.All(IsLetter))
                {
                    parts[i] = char.ToUpperInvariant(p[0]) + p.Substring(1).ToLowerInvariant();
                }
                else if (p.Length == 2 && p.All(IsLetter))
                {
                    parts[i] = p.ToUpperInvariant();
                }
            }

            return string.Join("-", parts);
        }

        /// <summary>(stem, culture) of a resource file name; null when it is not a <c>.kbres</c> name.</summary>
        public static (string Stem, string? Culture)? SplitFileName(string fileName)
        {
            if (!fileName.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var baseName = fileName.Substring(0, fileName.Length - Extension.Length);
            var dot = baseName.LastIndexOf('.');
            if (dot > 0 && IsCulture(baseName.Substring(dot + 1)))
            {
                return (baseName.Substring(0, dot), CanonicalCulture(baseName.Substring(dot + 1)));
            }

            return (baseName, null);
        }

        /// <summary>The neutral file of the set <paramref name="path"/> belongs to (itself when it is one).</summary>
        public static string NeutralFileOf(string path)
        {
            var split = SplitFileName(Path.GetFileName(path));
            return split is { Culture: not null } s ? Path.Combine(Path.GetDirectoryName(path) ?? string.Empty, s.Stem + Extension) : path;
        }

        /// <summary>The satellites of the neutral file <paramref name="neutral"/> on disk: (culture, path), sorted by culture.</summary>
        public static IReadOnlyList<(string Culture, string Path)> Satellites(string neutral, Func<string, IEnumerable<string>>? listFiles = null)
        {
            var dir = Path.GetDirectoryName(neutral) ?? string.Empty;
            var stem = SplitFileName(Path.GetFileName(neutral))?.Stem;
            listFiles ??= d => Directory.Exists(d) ? Directory.EnumerateFiles(d, "*" + Extension) : Enumerable.Empty<string>();
            return listFiles(dir)
                .Select(f => (Path: f, Split: SplitFileName(Path.GetFileName(f))))
                .Where(x => x.Split is { Culture: not null } s && s.Stem == stem)
                .Select(x => (x.Split!.Value.Culture!, x.Path))
                .OrderBy(x => x.Item1, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>The snake_case accessor <c>resources!</c> generates (<c>okButton.Text</c> → <c>ok_button_text</c>).</summary>
        public static string RustName(string name)
        {
            var sb = new StringBuilder();
            for (var i = 0; i < name.Length; i++)
            {
                var c = name[i];
                if (c == '+')
                {
                    if (sb.Length > 0 && sb[sb.Length - 1] != '_')
                    {
                        sb.Append('_');
                    }

                    sb.Append("plus_");
                    continue;
                }

                if (c == '.' || c == '-' || c == ' ' || c == '_')
                {
                    if (sb.Length > 0 && sb[sb.Length - 1] != '_')
                    {
                        sb.Append('_');
                    }

                    continue;
                }

                if (c >= 'A' && c <= 'Z')
                {
                    var prev = i > 0 ? name[i - 1] : '_';
                    var next = i + 1 < name.Length ? name[i + 1] : '_';
                    var boundary = (prev >= 'a' && prev <= 'z') || char.IsDigit(prev) || (prev >= 'A' && prev <= 'Z' && next >= 'a' && next <= 'z');
                    if (boundary && sb.Length > 0 && sb[sb.Length - 1] != '_')
                    {
                        sb.Append('_');
                    }

                    sb.Append(char.ToLowerInvariant(c));
                }
                else if (char.IsLetterOrDigit(c) && c < 128)
                {
                    sb.Append(c);
                }
                else if (sb.Length > 0 && sb[sb.Length - 1] != '_')
                {
                    sb.Append('_');
                }
            }

            while (sb.Length > 1 && sb[sb.Length - 1] == '_')
            {
                sb.Length--;
            }

            var result = sb.Length == 0 ? "_" : sb.ToString();
            if (char.IsDigit(result[0]))
            {
                result = "_" + result;
            }

            return Keywords.Contains(result) ? result + "_" : result;
        }

        /// <summary>The PascalCase type <c>resources!</c> generates for a file stem (<c>main_view</c> → <c>MainView</c>).</summary>
        public static string TypeName(string stem)
        {
            var sb = new StringBuilder();
            var upper = true;
            foreach (var c in stem)
            {
                if (char.IsLetterOrDigit(c) && c < 128)
                {
                    sb.Append(upper ? char.ToUpperInvariant(c) : c);
                    upper = false;
                }
                else
                {
                    upper = true;
                }
            }

            if (sb.Length == 0 || char.IsDigit(sb[0]))
            {
                sb.Insert(0, 'R');
            }

            return sb.ToString();
        }

        private static readonly HashSet<string> Keywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "as", "break", "const", "continue", "crate", "else", "enum", "extern", "false", "fn", "for", "if", "impl", "in", "let", "loop", "match", "mod", "move", "mut", "pub", "ref",
            "return", "self", "static", "struct", "super", "trait", "true", "type", "unsafe", "use", "where", "while", "async", "await", "dyn", "abstract", "become", "box", "do",
            "final", "macro", "override", "priv", "typeof", "unsized", "virtual", "yield", "try", "gen", "culture", "set_culture", "register", "set", "get", "names",
        };

        /// <summary><c>{Res key}</c> or <c>{Res key, Source=set}</c>.</summary>
        public static string Reference(string key, string? set = null) => string.IsNullOrEmpty(set) ? "{Res " + key + "}" : "{Res " + key + ", Source=" + set + "}";

        /// <summary>(key, set) of a <c>{Res …}</c> attribute value; null when it is not one.</summary>
        public static (string Key, string? Set)? ParseReference(string? value)
        {
            var v = value?.Trim();
            if (v is null || !v.StartsWith("{", StringComparison.Ordinal) || !v.EndsWith("}", StringComparison.Ordinal))
            {
                return null;
            }

            var inner = v.Substring(1, v.Length - 2).Trim();
            if (!inner.StartsWith("Res", StringComparison.Ordinal) || (inner.Length > 3 && !char.IsWhiteSpace(inner[3]) && inner[3] != ','))
            {
                return null;
            }

            string? key = null;
            string? set = null;
            var parts = inner.Substring(3).Split(',');
            for (var i = 0; i < parts.Length; i++)
            {
                var part = parts[i].Trim();
                var eq = part.IndexOf('=');
                if (eq < 0)
                {
                    if (i == 0 && part.Length > 0)
                    {
                        key = part;
                    }

                    continue;
                }

                var k = part.Substring(0, eq).Trim();
                var val = part.Substring(eq + 1).Trim();
                if (val.Length == 0)
                {
                    continue;
                }

                if (k is "Key" or "Path" or "ResourceKey")
                {
                    key = val;
                }
                else if (k is "Source" or "Set" or "File")
                {
                    set = val.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) ? val.Substring(0, val.Length - Extension.Length) : val;
                }
            }

            return key is null ? null : (key, set);
        }
    }
}
