using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Kubuno.VisualStudio.TemplateWizard
{
    /// <summary>
    /// The pure logic of <see cref="ControlItemWizard"/> (docs/EVENTS.md EVT-7b), kept free of Visual Studio types so the
    /// unit tests can compile it on its own: the names of a new control (its struct in PascalCase, its module/file in
    /// snake_case) and the <c>mod</c> declaration it needs in the crate root.
    /// </summary>
    public static class ControlItemNames
    {
        /// <summary>The built-in classes an inherited control can extend (<c>kubuno_views::controls</c>), the usual ones first.</summary>
        public static readonly string[] BaseClasses =
        {
            "Button", "Label", "TextField", "CheckBox", "Switch", "Panel", "Card", "ListBox", "Slider", "ProgressBar",
            "IconButton", "RadioButton", "LinkLabel", "Badge", "TextArea", "SearchField", "MaskedField", "ComboBox", "Dropdown",
            "CheckedListBox", "NumericField", "GroupBox", "Stack", "ScrollArea", "Tabs", "Splitter", "Accordion",
        };

        /// <summary><c>round_button</c>, <c>roundButton</c>, <c>Round Button</c> → <c>RoundButton</c> (a valid Rust type name).</summary>
        public static string ClassName(string name)
        {
            var words = Words(name);
            var result = string.Concat(words.Select(w => char.ToUpperInvariant(w[0]) + w.Substring(1)));
            if (result.Length == 0)
            {
                return "CustomControl";
            }

            return char.IsDigit(result[0]) ? "Control" + result : result;
        }

        /// <summary><c>RoundButton</c> → <c>round_button</c> (a valid Rust module and file name).</summary>
        public static string ModuleName(string name)
        {
            var result = string.Join("_", Words(name).Select(w => w.ToLowerInvariant()));
            if (result.Length == 0)
            {
                return "custom_control";
            }

            return char.IsDigit(result[0]) ? "control_" + result : result;
        }

        /// <summary>The words of a name: split on anything but letters and digits, and at camel-case boundaries (<c>HTTPClient</c> → HTTP, Client).</summary>
        private static string[] Words(string name)
        {
            var stem = Path.GetFileNameWithoutExtension(name ?? string.Empty);
            var spaced = Regex.Replace(stem, @"([a-z0-9])([A-Z])", "$1 $2");
            spaced = Regex.Replace(spaced, @"([A-Z]+)([A-Z][a-z])", "$1 $2");
            return Regex.Split(spaced, @"[^A-Za-z0-9]+").Where(w => w.Length > 0).ToArray();
        }

        /// <summary>
        /// The crate root (<c>src\main.rs</c>, else <c>src\lib.rs</c>) a new module file <paramref name="rsPath"/> belongs
        /// to, when the file sits directly in the crate's <c>src</c> folder; null otherwise (a module in a sub-folder is
        /// declared by its parent module, which the wizard does not guess).
        /// </summary>
        public static string? CrateRootFor(string rsPath, Func<string, bool> fileExists)
        {
            var directory = Path.GetDirectoryName(rsPath);
            if (directory is null || !string.Equals(Path.GetFileName(directory), "src", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var packageDirectory = Path.GetDirectoryName(directory);
            if (packageDirectory is null || !fileExists(Path.Combine(packageDirectory, "Cargo.toml")))
            {
                return null;
            }

            foreach (var candidate in new[] { "main.rs", "lib.rs" })
            {
                var root = Path.Combine(directory, candidate);
                if (fileExists(root) && !string.Equals(root, rsPath, StringComparison.OrdinalIgnoreCase))
                {
                    return root;
                }
            }

            return null;
        }

        /// <summary>
        /// <paramref name="rootText"/> with <c>mod <paramref name="module"/>;</c> declared: after its last top-level
        /// <c>mod x;</c> line, else after its last <c>use</c> line, else after its leading <c>//!</c> and <c>#![…]</c>
        /// lines. A file not named after its module (<paramref name="fileName"/> <c>RoundButton.rs</c> for <c>round_button</c>)
        /// gets a <c>#[path]</c> attribute. Null when the module is already declared.
        /// </summary>
        public static string? DeclareModule(string rootText, string module, string? fileName = null)
        {
            if (Regex.IsMatch(rootText, @"(?m)^\s*(pub(\([^)]*\))?\s+)?mod\s+" + Regex.Escape(module) + @"\s*[;{]"))
            {
                return null;
            }

            var newline = rootText.Contains("\r\n") ? "\r\n" : "\n";
            var line = fileName is null || string.Equals(fileName, module + ".rs", StringComparison.Ordinal)
                ? "mod " + module + ";"
                : "#[path = \"" + fileName + "\"]" + newline + "mod " + module + ";";
            var mods = Regex.Matches(rootText, @"(?m)^(pub(\([^)]*\))?\s+)?mod\s+\w+\s*;[^\n]*$");
            if (mods.Count > 0)
            {
                return InsertAfterLine(rootText, mods[mods.Count - 1], line, newline);
            }

            var uses = Regex.Matches(rootText, @"(?m)^(pub(\([^)]*\))?\s+)?use\s[^;]*;[^\n]*$");
            if (uses.Count > 0)
            {
                // A new module line before the `use` block reads better, but the `use`s may name it: after them.
                return InsertAfterLine(rootText, uses[uses.Count - 1], line, newline);
            }

            var offset = 0;
            var lines = rootText.Split('\n');
            foreach (var l in lines)
            {
                var t = l.TrimStart();
                if (!(t.StartsWith("//!", StringComparison.Ordinal) || t.StartsWith("#![", StringComparison.Ordinal) || t.TrimEnd('\r').Length == 0 && offset > 0))
                {
                    break;
                }

                offset += l.Length + 1;
            }

            offset = Math.Min(offset, rootText.Length);
            return rootText.Substring(0, offset) + (offset > 0 && !rootText.Substring(0, offset).EndsWith(newline + newline, StringComparison.Ordinal) ? newline : string.Empty) + line + newline + (offset < rootText.Length ? newline : string.Empty) + rootText.Substring(offset);
        }

        private static string InsertAfterLine(string text, Match match, string line, string newline)
        {
            var end = match.Index + match.Length;
            var lineEnd = text.IndexOf('\n', end);
            return lineEnd < 0
                ? text + newline + line + newline
                : text.Substring(0, lineEnd + 1) + line + newline + text.Substring(lineEnd + 1);
        }

        /// <summary>Declares the module of <paramref name="rsPath"/> in its crate root on disk (keeping its encoding); whether it did.</summary>
        public static bool DeclareModuleOnDisk(string rsPath)
        {
            var root = CrateRootFor(rsPath, File.Exists);
            if (root is null)
            {
                return false;
            }

            var bytes = File.ReadAllBytes(root);
            var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            var text = new UTF8Encoding(false).GetString(bytes, hasBom ? 3 : 0, bytes.Length - (hasBom ? 3 : 0));
            var updated = DeclareModule(text, ModuleName(Path.GetFileNameWithoutExtension(rsPath)), Path.GetFileName(rsPath));
            if (updated is null)
            {
                return false;
            }

            File.WriteAllText(root, updated, new UTF8Encoding(hasBom));
            return true;
        }
    }
}
