using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Kubuno.Desktop.TemplateWizard
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
            if (directory is null)
            {
                return null;
            }

            if (!string.Equals(Path.GetFileName(directory), "src", StringComparison.OrdinalIgnoreCase))
            {
                // A file added outside `src` (Add New Item on the project node puts it next to Cargo.toml, as Windows
                // Forms puts a new UserControl next to the .csproj): declared from the crate root with a #[path].
                var package = PackageDirectoryFor(rsPath, fileExists);
                if (package is null || IsUnder(rsPath, Path.Combine(package, "src")))
                {
                    return null;
                }

                directory = Path.Combine(package, "src");
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

        /// <summary>The nearest folder at or above <paramref name="path"/>'s folder holding a <c>Cargo.toml</c>; null when none.</summary>
        public static string? PackageDirectoryFor(string path, Func<string, bool> fileExists)
        {
            var directory = Path.GetDirectoryName(path);
            while (!string.IsNullOrEmpty(directory))
            {
                if (fileExists(Path.Combine(directory, "Cargo.toml")))
                {
                    return directory;
                }

                directory = Path.GetDirectoryName(directory);
            }

            return null;
        }

        private static bool IsUnder(string path, string directory) =>
            path.StartsWith(directory.TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// How the crate root <paramref name="rootPath"/> names the module file <paramref name="rsPath"/> in a <c>#[path]</c>:
        /// its file name when they share a folder, else the relative path with <c>/</c> (<c>../address_editor.rs</c>).
        /// </summary>
        public static string ModulePathFrom(string rootPath, string rsPath)
        {
            var rootDir = Path.GetDirectoryName(rootPath) ?? string.Empty;
            var fileDir = Path.GetDirectoryName(rsPath) ?? string.Empty;
            if (string.Equals(rootDir, fileDir, StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFileName(rsPath);
            }

            var from = new Uri(rootDir.TrimEnd('\\') + "\\");
            return Uri.UnescapeDataString(from.MakeRelativeUri(new Uri(rsPath)).ToString()).Replace('\\', '/');
        }

        /// <summary>
        /// <paramref name="rootText"/> with <c>mod <paramref name="module"/>;</c> declared: after its last top-level
        /// <c>mod x;</c> line, else after its last <c>use</c> line, else after its leading <c>//!</c> and <c>#![…]</c>
        /// lines. A file not named after its module (<paramref name="fileName"/> <c>RoundButton.rs</c> for <c>round_button</c>)
        /// gets a <c>#[path]</c> attribute. Null when the module is already declared.
        /// </summary>
        public static string? DeclareModule(string rootText, string module, string? fileName = null, string visibility = "")
        {
            if (Regex.IsMatch(rootText, @"(?m)^\s*(pub(\([^)]*\))?\s+)?mod\s+" + Regex.Escape(module) + @"\s*[;{]"))
            {
                return null;
            }

            var newline = rootText.Contains("\r\n") ? "\r\n" : "\n";
            var line = fileName is null || string.Equals(fileName, module + ".rs", StringComparison.Ordinal)
                ? visibility + "mod " + module + ";"
                : "#[path = \"" + fileName + "\"]" + newline + visibility + "mod " + module + ";";
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

        /// <summary>
        /// Whether the manifest text <paramref name="cargoToml"/> declares a dependency named <paramref name="crate"/>
        /// (<c>name = …</c> in a <c>[*dependencies]</c> table, or a <c>[dependencies.name]</c> table).
        /// </summary>
        public static bool DependsOn(string cargoToml, string crate)
        {
            var section = string.Empty;
            foreach (var raw in (cargoToml ?? string.Empty).Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    var dot = section.LastIndexOf('.');
                    if (dot > 0 && section.Substring(0, dot).EndsWith("dependencies", StringComparison.Ordinal) && section.Substring(dot + 1).Trim('"') == crate)
                    {
                        return true;
                    }

                    continue;
                }

                var eq = line.IndexOf('=');
                if (eq > 0 && section.EndsWith("dependencies", StringComparison.Ordinal) && line.Substring(0, eq).Trim().Trim('"') == crate)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// A project that reaches Kubuno through the <c>kubuno</c> facade only (docs/PROGRAMMING-MODEL.md: one
        /// Kubuno dependency) names <c>kubuno_views</c> as <c>kubuno::views</c>: the paths of a generated file are
        /// rewritten (<c>use kubuno_views::prelude::*;</c> → <c>use kubuno::views::prelude::*;</c>).
        /// </summary>
        public static string RetargetToFacade(string rsText) => Regex.Replace(rsText ?? string.Empty, @"(?<![\w:])kubuno_views::", "kubuno::views::");

        /// <summary>
        /// <paramref name="cargoToml"/> with a <c>kubuno</c> dependency added next to its <c>kubuno-views</c> one (same
        /// checkout: <c>…/crates/kubuno-views</c> → <c>…/crates/kubuno</c>), for a form (<c>#[kubuno::view]</c>) added to a
        /// project created before the facade; null when there is nothing to add or no <c>kubuno-views</c> path to derive it from.
        /// </summary>
        public static string? AddFacadeDependency(string cargoToml)
        {
            if (DependsOn(cargoToml, "kubuno"))
            {
                return null;
            }

            var match = Regex.Match(cargoToml ?? string.Empty, @"(?m)^(?<indent>[ \t]*)kubuno-views(?<pad>[ \t]*)=[^\n]*?path[ \t]*=[ \t]*""(?<path>[^""]*?)kubuno-views""[^\n]*$");
            if (!match.Success)
            {
                return null;
            }

            var newline = cargoToml!.Contains("\r\n") ? "\r\n" : "\n";
            var line = match.Groups["indent"].Value + "kubuno" + new string(' ', Math.Max(1, match.Groups["pad"].Value.Length + 6)) + "= { path = \"" + match.Groups["path"].Value + "kubuno\" }";
            var lineEnd = cargoToml.IndexOf('\n', match.Index + match.Length);
            return lineEnd < 0 ? cargoToml + newline + line + newline : cargoToml.Substring(0, lineEnd + 1) + line + newline + cargoToml.Substring(lineEnd + 1);
        }

        /// <summary>
        /// Adapts a generated Rust file to its project (on disk): in a <c>kubuno</c>-only project its
        /// <c>kubuno_views::</c> paths become <c>kubuno::views::</c>; a file using <c>kubuno::</c> in a project without that
        /// dependency gets it added to <c>Cargo.toml</c>. Returns whether something changed.
        /// </summary>
        public static bool AdaptToProjectOnDisk(string rsPath)
        {
            var package = PackageDirectoryFor(rsPath, File.Exists);
            var manifest = package is null ? null : Path.Combine(package, "Cargo.toml");
            if (manifest is null || !File.Exists(manifest) || !File.Exists(rsPath))
            {
                return false;
            }

            var toml = File.ReadAllText(manifest);
            var text = File.ReadAllText(rsPath);
            if (DependsOn(toml, "kubuno") && !DependsOn(toml, "kubuno-views") && text.Contains("kubuno_views::"))
            {
                File.WriteAllText(rsPath, RetargetToFacade(text), new UTF8Encoding(false));
                return true;
            }

            if (Regex.IsMatch(text, @"(?<![\w:])kubuno::") && AddFacadeDependency(toml) is { } updated)
            {
                File.WriteAllText(manifest, updated, new UTF8Encoding(false));
                return true;
            }

            return false;
        }

        /// <summary>
        /// The module file that declares <paramref name="rsPath"/> when it sits in a sub-folder of the crate's <c>src</c>
        /// (<c>src\pages\account_row.rs</c>, docs/DESKTOP-MIGRATION.md "Source layout"): the folder's <c>mod.rs</c>, else
        /// the <c>pages.rs</c> next to the folder. Null when the file is not in such a sub-folder, or when the folder has
        /// no module file yet (<see cref="FolderModuleToCreate"/> says which one to create).
        /// </summary>
        public static string? ParentModuleFor(string rsPath, Func<string, bool> fileExists)
        {
            var folder = SourceSubFolder(rsPath, fileExists);
            if (folder is null)
            {
                return null;
            }

            var modRs = Path.Combine(folder, "mod.rs");
            if (fileExists(modRs) && !string.Equals(modRs, rsPath, StringComparison.OrdinalIgnoreCase))
            {
                return modRs;
            }

            var sibling = folder.TrimEnd('\\', '/') + ".rs";
            return fileExists(sibling) ? sibling : null;
        }

        /// <summary>
        /// The <c>mod.rs</c> to create when <paramref name="rsPath"/> is added to a sub-folder of <c>src</c> that is not a
        /// module yet (a folder just created in Solution Explorer); null when there is nothing to create.
        /// </summary>
        public static string? FolderModuleToCreate(string rsPath, Func<string, bool> fileExists)
        {
            var folder = SourceSubFolder(rsPath, fileExists);
            if (folder is null || ParentModuleFor(rsPath, fileExists) is not null || string.Equals(Path.GetFileName(rsPath), "mod.rs", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return Path.Combine(folder, "mod.rs");
        }

        /// <summary>The folder of <paramref name="rsPath"/> when it is strictly under its package's <c>src</c>; null otherwise.</summary>
        private static string? SourceSubFolder(string rsPath, Func<string, bool> fileExists)
        {
            var package = PackageDirectoryFor(rsPath, fileExists);
            var folder = Path.GetDirectoryName(rsPath);
            if (package is null || folder is null)
            {
                return null;
            }

            var src = Path.Combine(package, "src");
            return IsUnder(folder, src) ? folder : null;
        }

        /// <summary>
        /// The visibility of a module declared in <paramref name="parentText"/>: the one of the modules it already declares
        /// (<c>pub </c> when they are public), else <c>pub </c> in a library (its views are linked by the designer and
        /// re-exported by path) and nothing in a binary.
        /// </summary>
        public static string ModuleVisibility(string parentText, bool isLibrary = false)
        {
            var text = parentText ?? string.Empty;
            if (Regex.IsMatch(text, @"(?m)^pub\s+mod\s+\w+\s*;"))
            {
                return "pub ";
            }

            return Regex.IsMatch(text, @"(?m)^mod\s+\w+\s*;") || !isLibrary ? string.Empty : "pub ";
        }

        /// <summary>
        /// Declares the module of <paramref name="rsPath"/> on disk (keeping the file's encoding): in the crate root for a
        /// file of <c>src</c> (or next to Cargo.toml), in its folder's module file for a file of a sub-folder of <c>src</c>
        /// (creating that <c>mod.rs</c> when the folder is not a module yet). Whether it declared something.
        /// </summary>
        public static bool DeclareModuleOnDisk(string rsPath) => DeclareModuleOnDisk(rsPath, 0);

        private static bool DeclareModuleOnDisk(string rsPath, int depth)
        {
            // A folder's `mod.rs` is declared like the `<folder>.rs` it stands for (`mod pages;` in the folder's parent).
            var isFolderModule = string.Equals(Path.GetFileName(rsPath), "mod.rs", StringComparison.OrdinalIgnoreCase);
            var folderOfModRs = isFolderModule ? Path.GetDirectoryName(rsPath) : null;
            var effective = folderOfModRs is not null ? folderOfModRs.TrimEnd('\\', '/') + ".rs" : rsPath;
            var module = ModuleName(Path.GetFileNameWithoutExtension(effective));

            var declaring = CrateRootFor(effective, File.Exists);
            if (declaring is null && depth < 16)
            {
                // A file of a sub-folder of `src` (docs/DESKTOP-MIGRATION.md "Source layout"): declared by the folder's
                // module file — created, and itself declared by its parent, when the folder is not a module yet.
                if (FolderModuleToCreate(effective, File.Exists) is { } create)
                {
                    var folderName = Path.GetFileName(Path.GetDirectoryName(create)) ?? string.Empty;
                    File.WriteAllText(create, "//! The `" + ModuleName(folderName) + "` folder of the crate.\n", new UTF8Encoding(false));
                    DeclareModuleOnDisk(create, depth + 1);
                }

                declaring = ParentModuleFor(effective, File.Exists);
            }

            if (declaring is null)
            {
                return false;
            }

            var bytes = File.ReadAllBytes(declaring);
            var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            var text = new UTF8Encoding(false).GetString(bytes, hasBom ? 3 : 0, bytes.Length - (hasBom ? 3 : 0));
            // The new module gets the visibility of the modules already declared there (`pub mod` in a library), else a
            // library's modules are public (its views are linked by the designer and re-exported by path).
            var isCrateRoot = CrateRootFor(effective, File.Exists) is not null;
            var package = PackageDirectoryFor(effective, File.Exists);
            var isLibrary = package is not null && File.Exists(Path.Combine(package, "src", "lib.rs"));
            // A `pages.rs` next to its folder names `pages/x.rs` as plain `mod x;`: no #[path] then.
            var siblingStyle = !isCrateRoot && !string.Equals(Path.GetFileName(declaring), "mod.rs", StringComparison.OrdinalIgnoreCase);
            var updated = DeclareModule(text, module, siblingStyle ? null : ModulePathFrom(declaring, effective), ModuleVisibility(text, isLibrary));
            if (updated is null)
            {
                return false;
            }

            File.WriteAllText(declaring, updated, new UTF8Encoding(hasBom));
            return true;
        }
    }
}
