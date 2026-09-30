using System;
using System.Collections.Generic;

namespace Kubuno.Rust.Launch
{
    /// <summary>
    /// Parses the free-text "Environment" debug setting of a <c>.rsproj</c> (its Debug property
    /// page, persisted as the <c>RustDebuggerEnvironment</c> MSBuild property) into the overrides
    /// <see cref="RustDebugEnvironment.Build"/> applies last. The format is the one Visual C++'s
    /// own "Environment" debugging setting uses: one <c>NAME=value</c> pair per line.
    /// </summary>
    public static class DebugEnvironmentText
    {
        /// <summary>
        /// One <c>NAME=value</c> per line (<c>\n</c> or <c>\r\n</c>). The value is everything after
        /// the FIRST <c>=</c>, kept verbatim (so it may itself contain <c>=</c> or <c>;</c> - a
        /// PATH-like value is a single entry); the name is trimmed. Blank lines, lines starting with
        /// <c>#</c> and lines without a name are ignored. A later duplicate name wins; names are
        /// case-insensitive, like Windows environment variables.
        /// </summary>
        public static IReadOnlyDictionary<string, string> Parse(string? text)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(text))
            {
                return result;
            }

            foreach (var rawLine in text!.Split('\n'))
            {
                var line = rawLine.TrimEnd('\r');
                if (line.Trim().Length == 0 || line.TrimStart().StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                var separator = line.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                var name = line.Substring(0, separator).Trim();
                if (name.Length == 0)
                {
                    continue;
                }

                result[name] = line.Substring(separator + 1);
            }

            return result;
        }
    }
}
