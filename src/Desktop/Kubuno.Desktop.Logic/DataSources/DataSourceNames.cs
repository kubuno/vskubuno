using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Kubuno.VisualStudio.Core.DataSources
{
    /// <summary>
    /// Names of the Data Sources feature (docs/DATA.md §9, DATA-6): the data source's Rust module name, the connection string
    /// name, the <c>x:Name</c>s a drop gives the view's components and controls, and the labels shown for columns. Pure.
    /// </summary>
    public static class DataSourceNames
    {
        private static readonly HashSet<string> RustKeywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "as", "break", "const", "continue", "crate", "else", "enum", "extern", "false", "fn", "for", "if", "impl", "in", "let",
            "loop", "match", "mod", "move", "mut", "pub", "ref", "return", "self", "Self", "static", "struct", "super", "trait",
            "true", "type", "unsafe", "use", "where", "while", "async", "await", "dyn", "abstract", "become", "box", "do", "final",
            "macro", "override", "priv", "typeof", "unsized", "virtual", "yield", "try", "gen", "union",
        };

        /// <summary>Module names the wizard never proposes for a source (they would clash with the <c>data</c> module's own items).</summary>
        private static readonly HashSet<string> ReservedModules = new HashSet<string>(StringComparer.Ordinal) { "mod", "data", "main", "lib", "tests" };

        public const int MaxLength = 64;

        /// <summary>Whether <paramref name="name"/> is a Rust identifier (ASCII letters, digits, <c>_</c>, not starting with a digit, not a keyword).</summary>
        public static bool IsRustIdentifier(string? name)
        {
            if (string.IsNullOrEmpty(name) || name!.Length > MaxLength || RustKeywords.Contains(name) || name == "_")
            {
                return false;
            }

            if (!(IsAsciiLetter(name[0]) || name[0] == '_'))
            {
                return false;
            }

            return name.All(c => IsAsciiLetter(c) || char.IsDigit(c) && c < 128 || c == '_');
        }

        /// <summary>
        /// Checks a data source name: the module <c>src/data/&lt;name&gt;.rs</c> and the file <c>&lt;name&gt;.kbdata</c>, so a
        /// lower snake case Rust identifier. Returns null when valid, else the message to show.
        /// </summary>
        public static string? ValidateSourceName(string? name)
        {
            if (name is null || string.IsNullOrWhiteSpace(name))
            {
                return DataSourcesText.NameRequired;
            }

            if (!IsRustIdentifier(name))
            {
                return DataSourcesText.NameNotIdentifier(name);
            }

            if (name.Any(char.IsUpper))
            {
                return DataSourcesText.NameNotSnakeCase(name, ToSnakeCase(name));
            }

            if (ReservedModules.Contains(name))
            {
                return DataSourcesText.NameReserved(name);
            }

            return null;
        }

        /// <summary>
        /// Checks the name of the connection string (<c>ConnectionStrings:&lt;name&gt;</c>): 1 to 64 characters, letters, digits,
        /// <c>_</c>, <c>-</c> and <c>.</c>, starting with a letter. Returns null when valid, else the message to show.
        /// </summary>
        public static string? ValidateConnectionName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return DataSourcesText.ConnectionNameRequired;
            }

            if (name!.Length > MaxLength || !IsAsciiLetter(name[0]) || !name.All(c => IsAsciiLetter(c) || (char.IsDigit(c) && c < 128) || c == '_' || c == '-' || c == '.'))
            {
                return DataSourcesText.ConnectionNameInvalid;
            }

            return null;
        }

        /// <summary><c>Shop DB</c> → <c>shop_db</c>, <c>OrderLines</c> → <c>order_lines</c>; a valid lower snake case identifier (keywords get a <c>_db</c> suffix).</summary>
        public static string ToSnakeCase(string? text)
        {
            var builder = new StringBuilder();
            bool previousLowerOrDigit = false;
            foreach (char c in text ?? string.Empty)
            {
                if (IsAsciiLetter(c) && char.IsUpper(c))
                {
                    if (previousLowerOrDigit && builder.Length > 0 && builder[builder.Length - 1] != '_')
                    {
                        builder.Append('_');
                    }

                    builder.Append(char.ToLowerInvariant(c));
                    previousLowerOrDigit = false;
                }
                else if (IsAsciiLetter(c) || (char.IsDigit(c) && c < 128))
                {
                    builder.Append(c);
                    previousLowerOrDigit = true;
                }
                else if (builder.Length > 0 && builder[builder.Length - 1] != '_')
                {
                    builder.Append('_');
                    previousLowerOrDigit = false;
                }
            }

            string result = builder.ToString().Trim('_');
            if (result.Length == 0)
            {
                result = "data_source";
            }

            if (char.IsDigit(result[0]))
            {
                result = "db_" + result;
            }

            if (result.Length > MaxLength)
            {
                result = result.Substring(0, MaxLength).TrimEnd('_');
            }

            if (RustKeywords.Contains(result) || ReservedModules.Contains(result))
            {
                result += "_db";
            }

            return result;
        }

        /// <summary>A connection string name from a free text (a Data Explorer connection name): invalid characters become <c>_</c>.</summary>
        public static string ToConnectionName(string? text)
        {
            var builder = new StringBuilder();
            foreach (char c in text ?? string.Empty)
            {
                builder.Append(IsAsciiLetter(c) || (char.IsDigit(c) && c < 128) || c == '_' || c == '-' || c == '.' ? c : '_');
            }

            string result = builder.ToString().Trim('_');
            if (result.Length == 0 || !IsAsciiLetter(result[0]))
            {
                result = "Db" + result;
            }

            return result.Length > MaxLength ? result.Substring(0, MaxLength) : result;
        }

        /// <summary>
        /// <c>Customers</c> → <c>customers</c>, <c>customerId</c> → <c>customer_id</c>, <c>Order Date</c> → <c>order_date</c>: the base of an
        /// <c>x:Name</c>, which <c>#[kubuno::view]</c> turns into a field of the view's struct - so snake case, like the template's own
        /// names (a camelCase field is a <c>non_snake_case</c> warning in the application's build).
        /// </summary>
        public static string ToFieldName(string? text)
        {
            var words = SplitWords(text);
            if (words.Count == 0)
            {
                return "data";
            }

            string result = string.Join("_", words.Select(w => w.ToLowerInvariant()));
            return char.IsDigit(result[0]) ? "t" + result : result;
        }

        /// <summary><c>order_lines</c> → <c>orderLines</c>, <c>Customers</c> → <c>customers</c>.</summary>
        public static string ToCamelCase(string? text)
        {
            var words = SplitWords(text);
            if (words.Count == 0)
            {
                return "data";
            }

            var builder = new StringBuilder();
            for (int i = 0; i < words.Count; i++)
            {
                string word = words[i];
                if (i == 0)
                {
                    // An all-caps first word (ID, URL) is lowered whole; otherwise only its first letter.
                    builder.Append(word.All(c => !IsAsciiLetter(c) || char.IsUpper(c)) ? word.ToLowerInvariant() : char.ToLowerInvariant(word[0]) + word.Substring(1));
                }
                else
                {
                    builder.Append(char.ToUpperInvariant(word[0])).Append(word.Substring(1));
                }
            }

            string result = builder.ToString();
            return char.IsDigit(result[0]) ? "t" + result : result;
        }

        /// <summary><c>birth_date</c> → <c>Birth date</c>, <c>customerId</c> → <c>Customer id</c>, <c>id</c> → <c>Id</c>: a column's label.</summary>
        public static string Humanize(string? column)
        {
            var words = SplitWords(column);
            if (words.Count == 0)
            {
                return column ?? string.Empty;
            }

            var parts = words.Select((w, i) =>
            {
                bool acronym = w.Length > 1 && w.All(c => !IsAsciiLetter(c) || char.IsUpper(c));
                if (acronym)
                {
                    return w;
                }

                string lower = w.ToLower(CultureInfo.InvariantCulture);
                return i == 0 ? char.ToUpperInvariant(lower[0]) + lower.Substring(1) : lower;
            });
            return string.Join(" ", parts);
        }

        /// <summary>The row struct kubuno-data-model derives from a table (<c>customers</c> → <c>Customer</c>): the port of <c>naming::row_struct_name</c>.</summary>
        public static string RowName(string table)
        {
            string bare = table.Contains('.') ? table.Substring(table.LastIndexOf('.') + 1) : table;
            var words = bare.Split(new[] { '_', ' ', '-' }, StringSplitOptions.RemoveEmptyEntries);
            var builder = new StringBuilder();
            for (int i = 0; i < words.Length; i++)
            {
                string word = i + 1 == words.Length ? Singular(words[i]) : words[i];
                if (word.Length > 0)
                {
                    builder.Append(char.ToUpperInvariant(word[0])).Append(word.Substring(1));
                }
            }

            string result = builder.ToString();
            return result.Length == 0 || char.IsDigit(result[0]) ? "Row" + result : result;
        }

        /// <summary>The port of <c>naming::singular</c>.</summary>
        public static string Singular(string word)
        {
            string lower = word.ToLowerInvariant();
            if (lower.EndsWith("ies", StringComparison.Ordinal) && word.Length > 3)
            {
                return word.Substring(0, word.Length - 3) + "y";
            }

            if ((lower.EndsWith("sses", StringComparison.Ordinal) || lower.EndsWith("xes", StringComparison.Ordinal) || lower.EndsWith("ches", StringComparison.Ordinal) || lower.EndsWith("shes", StringComparison.Ordinal)) && word.Length > 4)
            {
                return word.Substring(0, word.Length - 2);
            }

            if (lower.EndsWith("ss", StringComparison.Ordinal) || lower.EndsWith("us", StringComparison.Ordinal) || lower.EndsWith("is", StringComparison.Ordinal))
            {
                return word;
            }

            return lower.EndsWith("s", StringComparison.Ordinal) && word.Length > 1 ? word.Substring(0, word.Length - 1) : word;
        }

        /// <summary><paramref name="baseName"/>, else <c>baseName1</c>, <c>baseName2</c>... - the first not in <paramref name="taken"/> (which receives it).</summary>
        public static string Unique(string baseName, ISet<string> taken)
        {
            string candidate = baseName;
            for (int i = 1; taken.Contains(candidate); i++)
            {
                candidate = baseName + i.ToString(CultureInfo.InvariantCulture);
            }

            taken.Add(candidate);
            return candidate;
        }

        /// <summary>A source name not yet used by a file of <paramref name="existing"/> (<c>shop</c>, <c>shop2</c>...).</summary>
        public static string UniqueSourceName(string baseName, IEnumerable<string> existing)
        {
            var set = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
            if (!set.Contains(baseName))
            {
                return baseName;
            }

            for (int i = 2; ; i++)
            {
                string candidate = baseName + i.ToString(CultureInfo.InvariantCulture);
                if (!set.Contains(candidate))
                {
                    return candidate;
                }
            }
        }

        private static List<string> SplitWords(string? text)
        {
            var words = new List<string>();
            var current = new StringBuilder();
            char previous = '\0';
            foreach (char c in text ?? string.Empty)
            {
                bool alphanumeric = IsAsciiLetter(c) || (char.IsDigit(c) && c < 128);
                if (!alphanumeric)
                {
                    Flush();
                }
                else
                {
                    // camelCase boundary: a capital after a lower-case letter or a digit.
                    if (IsAsciiLetter(c) && char.IsUpper(c) && current.Length > 0 && (char.IsLower(previous) || char.IsDigit(previous)))
                    {
                        Flush();
                    }

                    current.Append(c);
                }

                previous = c;
            }

            Flush();
            return words;

            void Flush()
            {
                if (current.Length > 0)
                {
                    words.Add(current.ToString());
                    current.Clear();
                }
            }
        }

        private static bool IsAsciiLetter(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
    }
}
