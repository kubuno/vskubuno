using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Kubuno.VisualStudio.Core.ProjectProperties
{
    /// <summary>
    /// The value format of the Project Properties editor's list editors (docs/RSPROJ.md, "Project properties
    /// like .NET"). Visual Studio's <c>MultiStringSelector</c> and <c>NameValueList</c> editors both exchange
    /// a single string of <c>name=value</c> pairs separated by <c>,</c>, where <c>/</c> escapes <c>/</c>,
    /// <c>,</c> and <c>=</c> (the editor's default encoding, <c>NameValuePairListEncoding</c> in
    /// <c>Microsoft.VisualStudio.ProjectSystem.VS.Implementation</c>, read from the installed assembly). A
    /// multi-string selector stores each checked string as its name with a <c>True</c>/<c>False</c> "is read
    /// only" flag as the value.
    /// </summary>
    public static class PropertyListEncoding
    {
        /// <summary>Encodes checked strings for a <c>MultiStringSelector</c> (none read-only).</summary>
        public static string EncodeStrings(IEnumerable<string> values) =>
            EncodePairs(values.Where(v => !string.IsNullOrEmpty(v)).Distinct(StringComparer.Ordinal).Select(v => new KeyValuePair<string, string>(v, "False")));

        /// <summary>The checked strings of a <c>MultiStringSelector</c> value, in order, without duplicates.</summary>
        public static IReadOnlyList<string> DecodeStrings(string? encoded)
        {
            var result = new List<string>();
            foreach (var pair in DecodePairs(encoded))
            {
                if (!result.Contains(pair.Key, StringComparer.Ordinal))
                {
                    result.Add(pair.Key);
                }
            }
            return result;
        }

        /// <summary>Encodes name/value pairs for a <c>NameValueList</c>.</summary>
        public static string EncodePairs(IEnumerable<KeyValuePair<string, string>> pairs) =>
            string.Join(",", pairs.Select(p => Escape(p.Key) + "=" + Escape(p.Value ?? string.Empty)));

        /// <summary>Decodes a <c>NameValueList</c> / <c>MultiStringSelector</c> value; entries without a name are skipped.</summary>
        public static IReadOnlyList<KeyValuePair<string, string>> DecodePairs(string? encoded)
        {
            var result = new List<KeyValuePair<string, string>>();
            if (string.IsNullOrWhiteSpace(encoded))
            {
                return result;
            }

            foreach (string entry in SplitUnescaped(encoded!, ','))
            {
                IReadOnlyList<string> parts = SplitUnescaped(entry, '=', maxParts: 2);
                if (parts.Count < 2)
                {
                    continue;
                }
                string name = Unescape(parts[0]);
                if (name.Length > 0)
                {
                    result.Add(new KeyValuePair<string, string>(name, Unescape(parts[1])));
                }
            }
            return result;
        }

        /// <summary>
        /// Converts the one-<c>NAME=value</c>-per-line environment text the debugger reads
        /// (<c>RustDebuggerEnvironment</c>, see <c>Kubuno.Launch.DebugEnvironmentText</c>) to a <c>NameValueList</c> value.
        /// </summary>
        public static string EnvironmentLinesToPairs(string? lines)
        {
            var pairs = new List<KeyValuePair<string, string>>();
            if (!string.IsNullOrEmpty(lines))
            {
                foreach (string raw in lines!.Split('\n'))
                {
                    string line = raw.TrimEnd('\r');
                    if (line.Trim().Length == 0 || line.TrimStart().StartsWith("#", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    int separator = line.IndexOf('=');
                    if (separator <= 0)
                    {
                        continue;
                    }
                    string name = line.Substring(0, separator).Trim();
                    if (name.Length > 0)
                    {
                        pairs.Add(new KeyValuePair<string, string>(name, line.Substring(separator + 1)));
                    }
                }
            }
            return EncodePairs(pairs);
        }

        /// <summary>The inverse of <see cref="EnvironmentLinesToPairs"/>: one <c>NAME=value</c> per line (<c>\r\n</c>).</summary>
        public static string PairsToEnvironmentLines(string? encoded)
        {
            var builder = new StringBuilder();
            foreach (var pair in DecodePairs(encoded))
            {
                string name = pair.Key.Trim();
                if (name.Length == 0 || name.IndexOf('=') >= 0 || name.IndexOf('\n') >= 0)
                {
                    continue;
                }
                if (builder.Length > 0)
                {
                    builder.Append("\r\n");
                }
                builder.Append(name).Append('=').Append(pair.Value.Replace("\r", string.Empty).Replace("\n", " "));
            }
            return builder.ToString();
        }

        /// <summary>Splits an MSBuild ';' list, trimming entries and dropping empty ones.</summary>
        public static IReadOnlyList<string> SplitMSBuildList(string? value) =>
            string.IsNullOrWhiteSpace(value)
                ? Array.Empty<string>()
                : value!.Split(';').Select(v => v.Trim()).Where(v => v.Length > 0).ToArray();

        private static string Escape(string value) => value.Replace("/", "//").Replace(",", "/,").Replace("=", "/=");

        private static string Unescape(string value) => value.Replace("/=", "=").Replace("/,", ",").Replace("//", "/");

        private static IReadOnlyList<string> SplitUnescaped(string text, char separator, int maxParts = int.MaxValue)
        {
            var parts = new List<string>();
            int start = 0;
            bool escaped = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == separator && !escaped && parts.Count < maxParts - 1)
                {
                    parts.Add(text.Substring(start, i - start));
                    start = i + 1;
                    escaped = false;
                    continue;
                }
                escaped = c == '/' && !escaped;
            }
            parts.Add(text.Substring(start));
            return parts;
        }
    }
}
