using System;
using System.Collections.Generic;
using System.Linq;

namespace Kubuno.Views.Designer.Menus
{
    /// <summary>
    /// The keyboard shortcut text of <c>ShortcutKeys</c> as the ShortcutKeys editor reads and writes it (docs/MENUS.md
    /// section 4): modifiers then one key, canonical spelling <c>Ctrl+Shift+Alt+Key</c>. The grammar itself is the
    /// language server's (<c>kubuno_desktop_views_syntax::shortcut</c>); this is the editor's view of it. Pure, unit-tested.
    /// </summary>
    public static class ShortcutText
    {
        /// <summary>The keys the editor offers, in its list order (the canonical names).</summary>
        public static IReadOnlyList<string> Keys { get; } =
            Enumerable.Range('A', 26).Select(c => ((char)c).ToString())
                .Concat(Enumerable.Range(0, 10).Select(d => d.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                .Concat(Enumerable.Range(1, 24).Select(f => "F" + f.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                .Concat(new[] { "Delete", "Insert", "Home", "End", "PageUp", "PageDown", "Up", "Down", "Left", "Right", "Enter", "Escape", "Space", "Tab", "Backspace", "Plus", "Minus", "Comma", "Period" })
                .ToList();

        private static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Del"] = "Delete", ["Ins"] = "Insert", ["PgUp"] = "PageUp", ["PgDn"] = "PageDown", ["Esc"] = "Escape", ["Return"] = "Enter",
            ["Back"] = "Backspace", ["+"] = "Plus", ["-"] = "Minus", [","] = "Comma", ["."] = "Period", ["OemPlus"] = "Plus", ["OemMinus"] = "Minus",
        };

        /// <summary>A parsed shortcut; <see cref="Key"/> is a name of <see cref="Keys"/>, or null when the text names none.</summary>
        public readonly struct Parts
        {
            public Parts(bool ctrl, bool shift, bool alt, string? key)
            {
                Ctrl = ctrl;
                Shift = shift;
                Alt = alt;
                Key = key;
            }

            public bool Ctrl { get; }

            public bool Shift { get; }

            public bool Alt { get; }

            public string? Key { get; }
        }

        /// <summary>Reads <paramref name="text"/> (any case, the usual aliases, <c>Ctrl++</c>); unknown parts are dropped.</summary>
        public static Parts Parse(string? text)
        {
            var value = (text ?? string.Empty).Trim();
            bool ctrl = false, shift = false, alt = false;
            string? key = null;
            if (value.EndsWith("++", StringComparison.Ordinal))
            {
                key = "Plus";
                value = value.Substring(0, value.Length - 2);
            }

            foreach (var raw in value.Split('+'))
            {
                var part = raw.Trim();
                switch (part.ToLowerInvariant())
                {
                    case "":
                        break;
                    case "ctrl":
                    case "control":
                        ctrl = true;
                        break;
                    case "shift":
                    case "maj":
                        shift = true;
                        break;
                    case "alt":
                        alt = true;
                        break;
                    default:
                        key = KeyName(part) ?? key;
                        break;
                }
            }

            return new Parts(ctrl, shift, alt, key);
        }

        /// <summary>The canonical name of key <paramref name="name"/>, or null when it is not one of <see cref="Keys"/>.</summary>
        public static string? KeyName(string name)
        {
            if (Aliases.TryGetValue(name, out var alias))
            {
                return alias;
            }

            if (name.Length == 2 && (name[0] == 'D' || name[0] == 'd') && char.IsDigit(name[1]))
            {
                return name.Substring(1);
            }

            return Keys.FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The canonical text of a shortcut; empty without a key (the attribute is removed).</summary>
        public static string Format(bool ctrl, bool shift, bool alt, string? key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return string.Empty;
            }

            return (ctrl ? "Ctrl+" : string.Empty) + (shift ? "Shift+" : string.Empty) + (alt ? "Alt+" : string.Empty) + key;
        }

        /// <summary><paramref name="text"/> in its canonical spelling (empty when it names no key).</summary>
        public static string Normalize(string? text)
        {
            var p = Parse(text);
            return Format(p.Ctrl, p.Shift, p.Alt, p.Key);
        }
    }
}
