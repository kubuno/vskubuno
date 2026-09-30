using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Kubuno.Rust.Cargo.Toml;

namespace Kubuno.Desktop.Logic.DataSources
{
    /// <summary>The result of <see cref="DataSourceCodeWriter.EnsureCargoManifest"/>.</summary>
    public sealed class CargoManifestUpdate
    {
        public CargoManifestUpdate(string text, string userSecretsId, bool changed, bool hasKubunoDependency, bool addedDataFeature, bool addedUserSecretsId, string dataCrate = "kubuno::data")
        {
            DataCrate = dataCrate;
            Text = text;
            UserSecretsId = userSecretsId;
            Changed = changed;
            HasKubunoDependency = hasKubunoDependency;
            AddedDataFeature = addedDataFeature;
            AddedUserSecretsId = addedUserSecretsId;
        }

        /// <summary>The manifest's new text (the old one when nothing changed).</summary>
        public string Text { get; }

        /// <summary>The crate's <c>[package.metadata.kubuno] user-secrets-id</c> (existing or new).</summary>
        public string UserSecretsId { get; }

        public bool Changed { get; }

        /// <summary>False when the crate depends neither on <c>kubuno</c> nor on <c>kubuno-data</c> (nothing could be enabled).</summary>
        public bool HasKubunoDependency { get; }

        public bool AddedDataFeature { get; }

        public bool AddedUserSecretsId { get; }

        /// <summary>How the crate reaches kubuno-data: <c>kubuno::data</c> (the facade) or <c>kubuno_data</c> (a direct dependency).</summary>
        public string DataCrate { get; }
    }

    /// <summary>
    /// The code the "Add Data Source" wizard writes or edits (docs/DATA.md §9, DATA-6), as pure text transformations - every edit
    /// is surgical and idempotent: running it again changes nothing, and comments, order and formatting of the developer's files
    /// are kept. The regenerable part of a data source is its <c>.kbdata</c> (plus the compile-time expansion of
    /// <c>data_source!</c>); <c>src/data/&lt;name&gt;.rs</c> is written once and then belongs to the developer.
    /// </summary>
    public static class DataSourceCodeWriter
    {
        /// <summary>The folder of the data sources, relative to the crate: <c>src/data</c>.</summary>
        public const string DataFolder = "src/data";

        /// <summary>
        /// <c>src/data/mod.rs</c> with a <c>pub mod &lt;name&gt;;</c> line: the whole file when <paramref name="existing"/> is null, else
        /// the line inserted after the file's last <c>mod</c> declaration (or after its leading comments). Unchanged when the module
        /// is already declared.
        /// </summary>
        public static string EnsureModDeclaration(string? existing, string module)
        {
            if (existing is null)
            {
                return "//! The data sources of this application (Data Sources window, \"Add Data Source...\"): one module per\n" +
                       "//! `.kbdata` file, whose rows and queries `data_source!` generates at compile time.\n\n" +
                       "pub mod " + module + ";\n";
            }

            return InsertModLine(existing, module, "pub mod " + module + ";");
        }

        /// <summary><c>main.rs</c> (or <c>lib.rs</c>) with <c>mod data;</c> after its last top-level <c>mod</c> line; unchanged when <c>data</c> is already declared.</summary>
        public static string EnsureDataModule(string mainText, string module = "data") => InsertModLine(mainText, module, "mod " + module + ";");

        /// <summary>
        /// <c>main.rs</c> with, as the first statement of <c>fn main</c>, the registration of the application's user secrets id
        /// (<c>[package.metadata.kubuno] user-secrets-id</c>, embedded at compile time by <c>user_secrets_id!</c>): without it the data
        /// components only look for <c>ConnectionStrings:&lt;name&gt;</c> in the environment. <paramref name="dataCrate"/> is the path of
        /// kubuno-data in the crate (<c>kubuno::data</c> through the facade, or <c>kubuno_data</c>). Unchanged when the file already
        /// registers an id or has no <c>fn main</c>.
        /// </summary>
        public static string EnsureUserSecretsRegistration(string mainText, string dataCrate = "kubuno::data")
        {
            if (mainText.Contains("set_user_secrets_id"))
            {
                return mainText;
            }

            var match = Regex.Match(mainText, @"^[ \t]*(pub[ \t]+)?(async[ \t]+)?fn[ \t]+main[ \t]*\([^)]*\)[^{;]*\{[ \t]*(\r?\n)", RegexOptions.Multiline);
            if (!match.Success)
            {
                return mainText;
            }

            string newLine = match.Groups[3].Value;
            int insertAt = match.Index + match.Length;
            string next = mainText.Substring(insertAt);
            string indent = new string(next.TakeWhile(c => c == ' ' || c == '\t').ToArray());
            if (indent.Length == 0 || next.TrimStart(' ', '\t').StartsWith("}", StringComparison.Ordinal))
            {
                indent = "    ";
            }

            string lines =
                indent + "// The data sources find their connection strings in the environment, the Windows Credential Manager" + newLine +
                indent + "// and the user secrets of this application (Cargo.toml [package.metadata.kubuno] user-secrets-id)." + newLine +
                indent + dataCrate + "::set_user_secrets_id(" + dataCrate + "::user_secrets_id!().as_deref());" + newLine;
            return mainText.Substring(0, insertAt) + lines + next;
        }

        /// <summary>Whether <paramref name="text"/> declares module <paramref name="module"/> (<c>mod x;</c>, <c>pub mod x;</c>, <c>mod x {</c>, any visibility).</summary>
        public static bool DeclaresModule(string text, string module) =>
            Regex.IsMatch(text ?? string.Empty, @"^[ \t]*(#\[[^\]]*\][ \t]*)*(pub(\([^)]*\))?[ \t]+)?mod[ \t]+(r#)?" + Regex.Escape(module) + @"[ \t]*(;|\{)", RegexOptions.Multiline);

        /// <summary>
        /// The developer-owned <c>src/data/&lt;name&gt;.rs</c>: a header, <c>kubuno::data::data_source!("&lt;name&gt;.kbdata");</c> and an empty
        /// <c>impl</c> block per row struct for their own code.
        /// </summary>
        public static string UserSourceFile(string module, string kbdataFileName, IEnumerable<KeyValuePair<string, string>> tablesAndRows, string dataCrate = "kubuno::data", string newLine = "\n")
        {
            var builder = new StringBuilder();
            builder.Append("//! The `").Append(module).Append("` data source: `").Append(kbdataFileName).Append("` describes its tables, and").Append(newLine);
            builder.Append("//! `data_source!` generates their typed rows and queries at compile time, checked by sqlx against the").Append(newLine);
            builder.Append("//! offline cache `.sqlx` (Data Sources window > \"Configure...\" rewrites only the `.kbdata`; refresh the").Append(newLine);
            builder.Append("//! cache after a change). This file is yours: it is never rewritten.").Append(newLine).Append(newLine);
            builder.Append(dataCrate).Append("::data_source!(\"").Append(kbdataFileName).Append("\");").Append(newLine);
            foreach (var pair in tablesAndRows)
            {
                builder.Append(newLine);
                builder.Append("/// A row of `").Append(pair.Key).Append("`.").Append(newLine);
                builder.Append("impl ").Append(pair.Value).Append(" {").Append(newLine);
                builder.Append("    // Your code: methods of the `").Append(pair.Key).Append("` rows (validation, computed values...).").Append(newLine);
                builder.Append('}').Append(newLine);
            }

            return builder.ToString();
        }

        /// <summary>
        /// The <c>.kbdata</c> to write: <paramref name="generated"/> (the tool's <c>kbdata.build</c> text, which starts with its own header
        /// comment) for a new file; for a "Configure..." of <paramref name="existing"/>, the generated body under the existing file's leading
        /// comment block (the developer's header is kept, the generated one dropped), in the existing file's line endings.
        /// </summary>
        public static string KbdataText(string? existing, string generated)
        {
            string newLine = existing != null && existing.Contains("\r\n") ? "\r\n" : "\n";
            string text = NormalizeNewLines(generated.TrimStart('﻿'), newLine);
            if (existing is null)
            {
                return text;
            }

            string header = LeadingComments(existing);
            if (header.Length == 0)
            {
                return text;
            }

            string body = text.Substring(LeadingComments(text).Length).TrimStart('\r', '\n');
            if (!header.EndsWith(newLine, StringComparison.Ordinal))
            {
                header += newLine;
            }

            return header + newLine + body;
        }

        /// <summary>The comment lines (and blank lines between them) that start <paramref name="text"/>.</summary>
        public static string LeadingComments(string text)
        {
            var lines = SplitKeepingEnds(text.TrimStart('﻿'));
            var builder = new StringBuilder();
            int lastComment = -1;
            for (int i = 0; i < lines.Count; i++)
            {
                string trimmed = lines[i].Trim();
                if (trimmed.StartsWith("#", StringComparison.Ordinal))
                {
                    lastComment = i;
                }
                else if (trimmed.Length != 0)
                {
                    break;
                }
            }

            for (int i = 0; i <= lastComment; i++)
            {
                builder.Append(lines[i]);
            }

            return builder.ToString();
        }

        /// <summary>
        /// Enables what a data source needs in the crate's <c>Cargo.toml</c>: the <c>data</c> feature of the <c>kubuno</c> dependency (merged
        /// with its existing features; a crate that depends on <c>kubuno-data</c> directly needs nothing) and a
        /// <c>[package.metadata.kubuno] user-secrets-id</c> (a new GUID, from <paramref name="newId"/>) when there is none.
        /// </summary>
        public static CargoManifestUpdate EnsureCargoManifest(string manifestText, Func<string> newId)
        {
            var document = TomlDocument.Parse(manifestText);
            bool addedFeature = false;
            bool hasDependency = false;
            var kubuno = document.GetValue("dependencies", "kubuno");
            if (kubuno != null)
            {
                hasDependency = true;
                if (kubuno.Kind == TomlValueKind.String)
                {
                    // `kubuno = "0.1"` → `kubuno = { version = "0.1", features = ["data"] }`.
                    document.SetValue(TomlValue.Table(new[]
                    {
                        new KeyValuePair<string, TomlValue>("version", kubuno),
                        new KeyValuePair<string, TomlValue>("features", TomlValue.Array(new[] { TomlValue.String("data") })),
                    }), "dependencies", "kubuno");
                    addedFeature = true;
                }
                else if (kubuno.Kind == TomlValueKind.Table)
                {
                    var features = document.GetValue("dependencies", "kubuno", "features")?.AsArray();
                    var names = features?.Select(f => f.AsString()).Where(f => f != null).Cast<string>().ToList() ?? new List<string>();
                    if (!names.Contains("data"))
                    {
                        var items = (features ?? Array.Empty<TomlValue>()).ToList();
                        items.Add(TomlValue.String("data"));
                        document.SetValue(TomlValue.Array(items), "dependencies", "kubuno", "features");
                        addedFeature = true;
                    }
                }
            }
            else if (document.GetValue("dependencies", "kubuno-data") != null)
            {
                hasDependency = true;
            }

            bool addedId = false;
            string? id = document.GetValue("package", "metadata", "kubuno", "user-secrets-id")?.AsString();
            if (string.IsNullOrWhiteSpace(id))
            {
                id = newId();
                document.SetValue(TomlValue.String(id), "package", "metadata", "kubuno", "user-secrets-id");
                addedId = true;
            }

            return new CargoManifestUpdate(document.Text, id!, !string.Equals(document.Text, manifestText, StringComparison.Ordinal), hasDependency, addedFeature, addedId, kubuno is null && hasDependency ? "kubuno_data" : "kubuno::data");
        }

        /// <summary>The <c>[package] name</c> of a manifest, or null (a virtual workspace manifest).</summary>
        public static string? PackageName(string manifestText)
        {
            try
            {
                return TomlDocument.Parse(manifestText).GetValue("package", "name")?.AsString();
            }
            catch (TomlParseException)
            {
                return null;
            }
        }

        private static string InsertModLine(string text, string module, string line)
        {
            if (DeclaresModule(text, module))
            {
                return text;
            }

            string newLine = text.Contains("\r\n") ? "\r\n" : "\n";
            var lines = SplitKeepingEnds(text);

            // After the last top-level `mod x;` line (not inside a block: the line starts at column 0).
            int lastMod = -1;
            var modLine = new Regex(@"^(#\[[^\]]*\]\s*)*(pub(\([^)]*\))?\s+)?mod\s+[A-Za-z_][A-Za-z0-9_]*\s*;");
            for (int i = 0; i < lines.Count; i++)
            {
                if (modLine.IsMatch(lines[i]))
                {
                    lastMod = i;
                }
            }

            int insertAt;
            string prefix = string.Empty;
            string suffix = string.Empty;
            if (lastMod >= 0)
            {
                insertAt = lastMod + 1;
            }
            else
            {
                // After the leading inner doc comments, inner attributes and `//` comments, blank lines around it.
                int lastHeader = -1;
                for (int i = 0; i < lines.Count; i++)
                {
                    string trimmed = lines[i].Trim();
                    if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith("#![", StringComparison.Ordinal))
                    {
                        lastHeader = i;
                    }
                    else if (trimmed.Length != 0)
                    {
                        break;
                    }
                }

                insertAt = lastHeader + 1;
                prefix = lastHeader >= 0 ? newLine : string.Empty;
                bool nextIsBlank = insertAt < lines.Count && lines[insertAt].Trim().Length == 0;
                suffix = insertAt < lines.Count && !nextIsBlank ? newLine : string.Empty;
            }

            var builder = new StringBuilder();
            for (int i = 0; i < insertAt && i < lines.Count; i++)
            {
                builder.Append(lines[i]);
            }

            if (builder.Length > 0 && !(builder[builder.Length - 1] == '\n'))
            {
                builder.Append(newLine);
            }

            builder.Append(prefix).Append(line).Append(newLine).Append(suffix);
            for (int i = insertAt; i < lines.Count; i++)
            {
                builder.Append(lines[i]);
            }

            return builder.ToString();
        }

        private static List<string> SplitKeepingEnds(string text)
        {
            var lines = new List<string>();
            int start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    lines.Add(text.Substring(start, i - start + 1));
                    start = i + 1;
                }
            }

            if (start < text.Length)
            {
                lines.Add(text.Substring(start));
            }

            return lines;
        }

        private static string NormalizeNewLines(string text, string newLine) => text.Replace("\r\n", "\n").Replace("\n", newLine);
    }
}
