using System;
using System.Text.RegularExpressions;

namespace Kubuno.Rust.Logic.ProjectProperties
{
    /// <summary>One contiguous replacement in a text: the minimal unit applied to a Visual Studio text buffer.</summary>
    public readonly struct TextEdit
    {
        public TextEdit(int start, int oldLength, string newText)
        {
            Start = start;
            OldLength = oldLength;
            NewText = newText ?? throw new ArgumentNullException(nameof(newText));
        }

        public int Start { get; }

        public int OldLength { get; }

        public string NewText { get; }

        /// <summary>The text after the edit.</summary>
        public string ApplyTo(string text) => text.Substring(0, Start) + NewText + text.Substring(Start + OldLength);
    }

    /// <summary>
    /// Reads and surgically rewrites the <c>#![windows_subsystem = "..."]</c> crate attribute of a binary's main
    /// source file - the "Windows subsystem" property of the <c>.rsproj</c> Application page (docs/RSPROJ.md,
    /// "Project properties like .NET"), the counterpart of .NET's Console/Windows Application output type.
    /// Only the attribute line changes; comments and the rest of the file are untouched.
    /// </summary>
    public static class WindowsSubsystemAttribute
    {
        /// <summary>No attribute, or <c>windows_subsystem = "console"</c>.</summary>
        public const string Console = "console";

        /// <summary><c>#![windows_subsystem = "windows"]</c>.</summary>
        public const string Windows = "windows";

        /// <summary><c>#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]</c>: no console in Release only.</summary>
        public const string WindowsInRelease = "windows-release";

        private static readonly Regex AttributeRegex = new Regex(
            @"^[ \t]*#!\[\s*(?:cfg_attr\s*\(\s*(?<cfg>.*?)\s*,\s*)?windows_subsystem\s*=\s*""(?<value>[A-Za-z]+)""\s*\)?\s*\][ \t]*(?://[^\r\n]*)?(?:\r?\n|$)",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);

        /// <summary>The current value: <see cref="Console"/>, <see cref="Windows"/> or <see cref="WindowsInRelease"/>.</summary>
        public static string Read(string source)
        {
            Match match = FindAttribute(source);
            if (!match.Success)
            {
                return Console;
            }

            string value = match.Groups["value"].Value;
            if (!string.Equals(value, Windows, StringComparison.OrdinalIgnoreCase))
            {
                return Console;
            }

            string cfg = match.Groups["cfg"].Value;
            if (cfg.Length == 0)
            {
                return Windows;
            }

            return Regex.Replace(cfg, @"\s+", string.Empty) == "not(debug_assertions)" ? WindowsInRelease : Windows;
        }

        /// <summary>
        /// The edit that makes <paramref name="source"/> declare <paramref name="value"/>, or null when it already
        /// does. An existing attribute line is replaced in place (keeping its indentation); otherwise the line is
        /// inserted after the file's leading inner doc comments (<c>//!</c>) and inner attributes, like
        /// <c>cargo new</c> templates place it. Choosing console with no attribute present changes nothing.
        /// </summary>
        public static TextEdit? Write(string source, string value)
        {
            if (value != Console && value != Windows && value != WindowsInRelease)
            {
                throw new ArgumentException($"Unknown Windows subsystem '{value}'.", nameof(value));
            }

            if (Read(source) == value)
            {
                // Semantically unchanged (also keeps the developer's own spacing of an equivalent attribute).
                return null;
            }

            string newline = source.Contains("\r\n") ? "\r\n" : "\n";
            string line = value switch
            {
                Windows => "#![windows_subsystem = \"windows\"]",
                WindowsInRelease => "#![cfg_attr(not(debug_assertions), windows_subsystem = \"windows\")]",
                _ => "#![windows_subsystem = \"console\"]",
            };

            Match match = FindAttribute(source);
            if (match.Success)
            {
                string original = match.Value;
                string indentation = original.Substring(0, original.Length - original.TrimStart(' ', '\t').Length);
                string lineEnd = original.EndsWith("\r\n", StringComparison.Ordinal) ? "\r\n" : original.EndsWith("\n", StringComparison.Ordinal) ? "\n" : string.Empty;
                string replacement = indentation + line + lineEnd;
                return replacement == original ? (TextEdit?)null : new TextEdit(match.Index, match.Length, replacement);
            }

            if (value == Console)
            {
                return null;
            }

            int insertAt = FindInsertionPoint(source);
            return new TextEdit(insertAt, 0, line + newline);
        }

        /// <summary>The first windows_subsystem attribute among the leading inner attributes/comments of the file.</summary>
        private static Match FindAttribute(string source)
        {
            int headerEnd = FindHeaderEnd(source);
            foreach (Match match in AttributeRegex.Matches(source))
            {
                if (match.Index >= headerEnd)
                {
                    break;
                }
                return match;
            }
            return Match.Empty;
        }

        /// <summary>
        /// End of the crate header: the leading run of blank lines, comments (line, doc and block) and inner
        /// attributes (<c>#![...]</c>, possibly spanning several lines) - crate attributes must precede any item.
        /// </summary>
        private static int FindHeaderEnd(string source)
        {
            int position = source.Length > 0 && source[0] == '﻿' ? 1 : 0;
            while (position < source.Length)
            {
                int lineEnd = source.IndexOf('\n', position);
                int next = lineEnd < 0 ? source.Length : lineEnd + 1;
                string trimmed = source.Substring(position, next - position).Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("//", StringComparison.Ordinal))
                {
                    position = next;
                    continue;
                }
                if (trimmed.StartsWith("/*", StringComparison.Ordinal))
                {
                    int close = source.IndexOf("*/", position, StringComparison.Ordinal);
                    if (close < 0)
                    {
                        return source.Length;
                    }
                    int afterClose = source.IndexOf('\n', close);
                    position = afterClose < 0 ? source.Length : afterClose + 1;
                    continue;
                }
                if (trimmed.StartsWith("#!", StringComparison.Ordinal) && !trimmed.StartsWith("#!/", StringComparison.Ordinal))
                {
                    int close = FindAttributeEnd(source, source.IndexOf("#!", position, StringComparison.Ordinal));
                    if (close < 0)
                    {
                        return source.Length;
                    }
                    int afterClose = source.IndexOf('\n', close);
                    position = afterClose < 0 ? source.Length : afterClose + 1;
                    continue;
                }
                if (position == 0 && trimmed.StartsWith("#!/", StringComparison.Ordinal))
                {
                    position = next; // shebang
                    continue;
                }
                return position;
            }
            return position;
        }

        /// <summary>Index of the ']' closing the inner attribute starting at <paramref name="start"/> ("#!["), bracket- and string-aware.</summary>
        private static int FindAttributeEnd(string source, int start)
        {
            int depth = 0;
            bool inString = false;
            for (int i = start + 2; i < source.Length; i++)
            {
                char c = source[i];
                if (inString)
                {
                    if (c == '\\')
                    {
                        i++;
                    }
                    else if (c == '"')
                    {
                        inString = false;
                    }
                    continue;
                }
                if (c == '"')
                {
                    inString = true;
                }
                else if (c == '[')
                {
                    depth++;
                }
                else if (c == ']')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }
                }
            }
            return -1;
        }

        /// <summary>After the last leading <c>//!</c> doc comment line or inner attribute; the file start otherwise.</summary>
        private static int FindInsertionPoint(string source)
        {
            int headerEnd = FindHeaderEnd(source);
            int position = source.Length > 0 && source[0] == '﻿' ? 1 : 0;
            int insertAt = position;
            while (position < headerEnd)
            {
                int lineEnd = source.IndexOf('\n', position);
                int next = lineEnd < 0 ? source.Length : lineEnd + 1;
                string trimmed = source.Substring(position, next - position).Trim();
                if (trimmed.StartsWith("//!", StringComparison.Ordinal))
                {
                    insertAt = next;
                }
                else if (trimmed.StartsWith("#!", StringComparison.Ordinal) && !trimmed.StartsWith("#!/", StringComparison.Ordinal))
                {
                    int close = FindAttributeEnd(source, source.IndexOf("#!", position, StringComparison.Ordinal));
                    int afterClose = close < 0 ? source.Length : source.IndexOf('\n', close);
                    next = afterClose < 0 ? source.Length : afterClose + 1;
                    insertAt = next;
                }
                position = next;
            }

            if (insertAt == source.Length && source.Length > 0 && source[source.Length - 1] != '\n')
            {
                // Header reaches EOF without a trailing newline: insert at the start instead of gluing lines.
                return source.Length > 0 && source[0] == '﻿' ? 1 : 0;
            }
            return insertAt;
        }
    }
}
