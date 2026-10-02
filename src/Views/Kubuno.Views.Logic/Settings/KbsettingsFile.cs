using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Kubuno.Views.Logic.Resources;

namespace Kubuno.Views.Logic.Settings
{
    /// <summary>One declared setting of a <c>.kbsettings</c> file (docs/STORAGE-COMPONENTS.md §5.3).</summary>
    public sealed class SettingEntry
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>One of <see cref="KbsettingsFile.Types"/>.</summary>
        public string Type { get; set; } = "String";

        /// <summary><c>User</c> or <c>Application</c>.</summary>
        public string Scope { get; set; } = "User";

        /// <summary>User scope: the value follows the user's roaming profile (else this machine only).</summary>
        public bool Roaming { get; set; } = true;

        /// <summary>Invariant text; a <c>StringList</c>'s items one per line.</summary>
        public string Default { get; set; } = string.Empty;

        /// <summary>The accepted values of a <c>String</c> (an enumeration), empty for any text.</summary>
        public List<string> Values { get; set; } = new List<string>();

        /// <summary>Earlier names: the stored value of one of them is moved to this setting by the upgrade.</summary>
        public List<string> PreviousNames { get; set; } = new List<string>();

        public string Description { get; set; } = string.Empty;

        /// <summary>The 1-based line of the entry in the file it was read from (0 when built in code).</summary>
        public int Line { get; set; }

        public bool IsApplication => string.Equals(Scope, "Application", StringComparison.Ordinal);

        public SettingEntry Clone() => new SettingEntry
        {
            Name = Name,
            Type = Type,
            Scope = Scope,
            Roaming = Roaming,
            Default = Default,
            Values = Values.ToList(),
            PreviousNames = PreviousNames.ToList(),
            Description = Description,
            Line = Line,
        };
    }

    /// <summary>
    /// The <c>.kbsettings</c> format (Windows Forms' <c>Settings.settings</c>): the settings an app declares, read by the
    /// <c>kubuno::settings!</c> macro (the typed class), the <c>.kbview</c> language server and this extension's settings
    /// editor. <see cref="ToText"/> writes the canonical form, byte for byte the one of the Rust model
    /// (<c>kubuno_resources_model::settings::SettingsFile::to_text</c>, tested on the same sample): the XML declaration,
    /// <c>&lt;Settings Version="N"[ App="…"]&gt;</c>, one setting per line with its attributes in the order Name, Type,
    /// Scope, Roaming, Default, Values, PreviousNames, Description (the defaults of Scope and Roaming omitted), a list's
    /// default as <c>&lt;Item&gt;</c> lines, two-space indentation, LF line ends and a final newline.
    /// </summary>
    public sealed class KbsettingsFile
    {
        public const string Extension = ".kbsettings";

        /// <summary>The setting types, in the order the editor's drop-down shows them.</summary>
        public static readonly IReadOnlyList<string> Types = new[] { "String", "Bool", "Int", "Float", "StringList" };

        public static readonly IReadOnlyList<string> Scopes = new[] { "User", "Application" };

        public int Version { get; set; } = 1;

        /// <summary>The app id the values are stored under (<c>App</c>), null for the Cargo package name.</summary>
        public string? App { get; set; }

        /// <summary><c>AccountScoped="true"</c>: the values belong to the signed-in account (docs/STORAGE-COMPONENTS.md, decision Q6).</summary>
        public bool AccountScoped { get; set; }

        public List<SettingEntry> Entries { get; set; } = new List<SettingEntry>();

        /// <summary>The text of a new file (Add &gt; New Item, a blank document).</summary>
        public static string Blank() => new KbsettingsFile().ToText();

        /// <summary>The canonical spelling of a type name (with the .NET aliases of a migrated <c>Settings.settings</c>).</summary>
        public static string? CanonicalType(string? name)
        {
            switch ((name ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "string":
                case "system.string":
                    return "String";
                case "bool":
                case "boolean":
                case "system.boolean":
                    return "Bool";
                case "int":
                case "integer":
                case "int32":
                case "int64":
                case "long":
                case "system.int32":
                case "system.int64":
                    return "Int";
                case "float":
                case "double":
                case "single":
                case "system.double":
                case "system.single":
                    return "Float";
                case "stringlist":
                case "stringcollection":
                case "system.collections.specialized.stringcollection":
                    return "StringList";
                default:
                    return null;
            }
        }

        /// <summary>A letter or <c>_</c>, then letters, digits and <c>_</c>, at most 128 characters.</summary>
        public static bool IsValidName(string? name) =>
            !string.IsNullOrEmpty(name) && name!.Length <= 128 && (IsAsciiLetter(name[0]) || name[0] == '_') && name.All(c => IsAsciiLetter(c) || (c >= '0' && c <= '9') || c == '_');

        private static bool IsAsciiLetter(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

        /// <summary>Whether <paramref name="text"/> is a valid default of type <paramref name="type"/> (canonical).</summary>
        public static bool IsValidDefault(string type, string text)
        {
            var t = (text ?? string.Empty).Trim();
            switch (type)
            {
                case "Bool":
                    return t == "true" || t == "false" || t == "True" || t == "False" || t == "1" || t == "0";
                case "Int":
                    return long.TryParse(t, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _);
                case "Float":
                    return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && !double.IsNaN(d) && !double.IsInfinity(d);
                default:
                    return true;
            }
        }

        /// <summary>The value of a setting of type <paramref name="type"/> written without a default.</summary>
        public static string ZeroDefault(string type) => type switch
        {
            "Bool" => "false",
            "Int" => "0",
            "Float" => "0.0",
            _ => string.Empty,
        };

        /// <summary>Whether a name suggests a secret (<c>ApiToken</c>, <c>SmtpPassword</c>): those belong in a <c>&lt;SecretStore&gt;</c>.</summary>
        public static bool LooksLikeSecret(string name)
        {
            var n = (name ?? string.Empty).ToLowerInvariant();
            return new[] { "password", "passwd", "secret", "token", "apikey", "api_key", "credential", "privatekey", "private_key" }.Any(n.Contains);
        }

        /// <summary>
        /// Reads <paramref name="text"/> leniently: the settings that could be read, and every problem found in
        /// <paramref name="errors"/> (an unreadable XML gives an empty file and one error).
        /// </summary>
        public static KbsettingsFile Parse(string text, out List<string> errors)
        {
            errors = new List<string>();
            var file = new KbsettingsFile();
            XDocument doc;
            try
            {
                doc = XDocument.Parse(text ?? string.Empty, LoadOptions.SetLineInfo);
            }
            catch (XmlException ex)
            {
                errors.Add($"line {ex.LineNumber}: {ex.Message}");
                return file;
            }

            var root = doc.Root;
            if (root is null || root.Name.LocalName != "Settings")
            {
                errors.Add("the root element must be <Settings>");
                return file;
            }

            var version = (string?)root.Attribute("Version");
            if (version != null)
            {
                if (int.TryParse(version.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var v) && v >= 1)
                {
                    file.Version = v;
                }
                else
                {
                    errors.Add($"Version must be a whole number from 1 ('{version}')");
                }
            }

            var app = (string?)root.Attribute("App");
            if (!string.IsNullOrEmpty(app))
            {
                file.App = app;
            }

            var accountScoped = (string?)root.Attribute("AccountScoped");
            if (accountScoped != null)
            {
                switch (accountScoped.Trim())
                {
                    case "true":
                    case "True":
                        file.AccountScoped = true;
                        break;
                    case "false":
                    case "False":
                        break;
                    default:
                        errors.Add($"AccountScoped is true or false ('{accountScoped}')");
                        break;
                }
            }

            foreach (var e in root.Elements())
            {
                var line = ((IXmlLineInfo)e).HasLineInfo() ? ((IXmlLineInfo)e).LineNumber : 0;
                if (e.Name.LocalName != "Setting")
                {
                    errors.Add($"line {line}: unknown element <{e.Name.LocalName}> (expected <Setting>)");
                    continue;
                }

                var type = CanonicalType((string?)e.Attribute("Type") ?? "String");
                var entry = new SettingEntry
                {
                    Name = (string?)e.Attribute("Name") ?? string.Empty,
                    Type = type ?? ((string?)e.Attribute("Type") ?? "String"),
                    Scope = string.Equals(((string?)e.Attribute("Scope") ?? "User").Trim(), "Application", StringComparison.OrdinalIgnoreCase) ? "Application" : "User",
                    Description = (string?)e.Attribute("Description") ?? string.Empty,
                    Values = Split((string?)e.Attribute("Values")),
                    PreviousNames = Split((string?)e.Attribute("PreviousNames")),
                    Line = line,
                };
                if (type is null)
                {
                    errors.Add($"line {line}: unknown type '{entry.Type}'");
                }

                var roaming = ((string?)e.Attribute("Roaming") ?? "true").Trim();
                entry.Roaming = !entry.IsApplication && !string.Equals(roaming, "false", StringComparison.OrdinalIgnoreCase);
                if (entry.Type == "StringList")
                {
                    entry.Default = string.Join("\n", e.Elements().Where(i => i.Name.LocalName == "Item").Select(i => i.Value));
                }
                else
                {
                    var d = (string?)e.Attribute("Default");
                    entry.Default = d ?? (entry.Values.FirstOrDefault() ?? ZeroDefault(entry.Type));
                    if (entry.Type == "Bool" && IsValidDefault("Bool", entry.Default))
                    {
                        var t = entry.Default.Trim();
                        entry.Default = t == "true" || t == "True" || t == "1" ? "true" : "false";
                    }
                }

                file.Entries.Add(entry);
            }

            errors.AddRange(file.Validate().Where(p => p.IsError).Select(p => p.Message));
            return file;
        }

        private static List<string> Split(string? list) =>
            (list ?? string.Empty).Split('|').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

        /// <summary>A problem of the declarations (the editor shows them; the macro refuses the errors).</summary>
        public sealed class Problem
        {
            public Problem(SettingEntry? entry, string message, bool isError)
            {
                Entry = entry;
                Message = message;
                IsError = isError;
            }

            public SettingEntry? Entry { get; }

            public string Message { get; }

            public bool IsError { get; }
        }

        /// <summary>The rules of the Rust model: names, duplicates (ignoring case), types, defaults, accepted values.</summary>
        public IEnumerable<Problem> Validate()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (Version < 1)
            {
                yield return new Problem(null, "the version starts at 1", true);
            }

            foreach (var e in Entries)
            {
                if (!IsValidName(e.Name))
                {
                    yield return new Problem(e, $"'{e.Name}' is not a valid setting name (a letter or '_', then letters, digits and '_')", true);
                }

                foreach (var n in new[] { e.Name }.Concat(e.PreviousNames))
                {
                    if (n.Length > 0 && !seen.Add(n))
                    {
                        yield return new Problem(e, $"'{n}' is declared twice (setting names are compared ignoring case)", true);
                    }
                }

                if (!Types.Contains(e.Type))
                {
                    yield return new Problem(e, $"'{e.Name}': unknown type '{e.Type}'", true);
                }
                else if (e.Type != "StringList" && !IsValidDefault(e.Type, e.Default))
                {
                    yield return new Problem(e, $"'{e.Name}': '{e.Default}' is not a valid {e.Type} value", true);
                }

                if (e.Values.Count > 0 && e.Type != "String")
                {
                    yield return new Problem(e, $"'{e.Name}': accepted values are only for String settings", true);
                }
                else if (e.Values.Count > 0 && !e.Values.Contains(e.Default))
                {
                    yield return new Problem(e, $"'{e.Name}': the default '{e.Default}' is not one of the accepted values", true);
                }

                if (e.PreviousNames.Any(p => !IsValidName(p)))
                {
                    yield return new Problem(e, $"'{e.Name}': a previous name is not a valid setting name", true);
                }

                if (LooksLikeSecret(e.Name))
                {
                    yield return new Problem(e, $"'{e.Name}' looks like a secret: settings are stored in plain files or the Registry; keep secrets in a SecretStore", false);
                }
            }
        }

        /// <summary>The canonical text (see the class doc).</summary>
        public string ToText()
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<Settings Version=\"").Append(Math.Max(1, Version).ToString(CultureInfo.InvariantCulture)).Append('"');
            if (App != null)
            {
                sb.Append(" App=\"").Append(KbresFile.EscapeAttr(App)).Append('"');
            }

            if (AccountScoped)
            {
                sb.Append(" AccountScoped=\"true\"");
            }

            if (Entries.Count == 0)
            {
                sb.Append("/>\n");
                return sb.ToString();
            }

            sb.Append(">\n");
            foreach (var e in Entries)
            {
                sb.Append("  <Setting Name=\"").Append(KbresFile.EscapeAttr(e.Name)).Append("\" Type=\"").Append(KbresFile.EscapeAttr(e.Type)).Append('"');
                if (e.IsApplication)
                {
                    sb.Append(" Scope=\"Application\"");
                }
                else if (!e.Roaming)
                {
                    sb.Append(" Roaming=\"false\"");
                }

                var list = e.Type == "StringList";
                if (!list && e.Default.Length > 0)
                {
                    sb.Append(" Default=\"").Append(KbresFile.EscapeAttr(e.Default)).Append('"');
                }

                if (e.Values.Count > 0)
                {
                    sb.Append(" Values=\"").Append(KbresFile.EscapeAttr(string.Join("|", e.Values))).Append('"');
                }

                if (e.PreviousNames.Count > 0)
                {
                    sb.Append(" PreviousNames=\"").Append(KbresFile.EscapeAttr(string.Join("|", e.PreviousNames))).Append('"');
                }

                if (e.Description.Length > 0)
                {
                    sb.Append(" Description=\"").Append(KbresFile.EscapeAttr(e.Description)).Append('"');
                }

                var items = list && e.Default.Length > 0 ? e.Default.Split('\n') : Array.Empty<string>();
                if (items.Length == 0)
                {
                    sb.Append("/>\n");
                }
                else
                {
                    sb.Append(">\n");
                    foreach (var i in items)
                    {
                        sb.Append("    <Item>").Append(KbresFile.EscapeText(i)).Append("</Item>\n");
                    }

                    sb.Append("  </Setting>\n");
                }
            }

            sb.Append("</Settings>\n");
            return sb.ToString();
        }
    }
}
