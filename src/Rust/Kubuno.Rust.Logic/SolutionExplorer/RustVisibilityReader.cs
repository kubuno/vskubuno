using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Kubuno.Rust.Logic.SolutionExplorer
{
    /// <summary>
    /// Reads a Rust item's visibility from the source text between the start of its syntax node and
    /// its name (e.g. <c>#[derive(Debug)] pub(crate) struct </c>). rust-analyzer's document symbols
    /// carry no visibility, so it is read back from the text itself.
    /// </summary>
    public static class RustVisibilityReader
    {
        private static readonly Regex PubModifier = new Regex(
            @"^pub\b\s*(?:\(\s*(?<scope>crate|super|self|in\b[^)]*)\s*\))?",
            RegexOptions.CultureInvariant);

        /// <summary>The visibility written in <paramref name="itemPrefix"/>; private when it has no <c>pub</c>.</summary>
        public static SymbolVisibility Read(string itemPrefix) => Read(itemPrefix, out _);

        /// <param name="itemPrefix">Source text from the start of the item's node up to its name.</param>
        /// <param name="isMacroExport">Whether one of the item's attributes is <c>#[macro_export]</c> (a <c>macro_rules!</c> is then public).</param>
        public static SymbolVisibility Read(string itemPrefix, out bool isMacroExport)
        {
            var code = StripTrivia(itemPrefix ?? string.Empty, out isMacroExport).TrimStart();
            var match = PubModifier.Match(code);
            if (!match.Success)
            {
                return SymbolVisibility.Private;
            }

            var scope = match.Groups["scope"];
            if (!scope.Success)
            {
                return SymbolVisibility.Public;
            }

            var value = scope.Value.Trim();
            if (value == "crate" || value.StartsWith("in", StringComparison.Ordinal))
            {
                return SymbolVisibility.Internal;
            }

            return value == "super" ? SymbolVisibility.Protected : SymbolVisibility.Private;
        }

        /// <summary>Removes comments (line, block, doc) and attributes (<c>#[...]</c>, <c>#![...]</c>).</summary>
        private static string StripTrivia(string text, out bool isMacroExport)
        {
            isMacroExport = false;
            var result = new StringBuilder(text.Length);
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (c == '/' && Next(text, i) == '/')
                {
                    while (i < text.Length && text[i] != '\n')
                    {
                        i++;
                    }
                }
                else if (c == '/' && Next(text, i) == '*')
                {
                    int end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    i = end < 0 ? text.Length : end + 2;
                }
                else if (c == '#' && (Next(text, i) == '[' || (Next(text, i) == '!' && Next(text, i + 1) == '[')))
                {
                    int open = text.IndexOf('[', i);
                    int end = SkipBracketed(text, open);
                    if (text.Substring(open, end - open).IndexOf("macro_export", StringComparison.Ordinal) >= 0)
                    {
                        isMacroExport = true;
                    }

                    i = end;
                }
                else
                {
                    result.Append(c);
                    i++;
                }
            }

            return result.ToString();
        }

        private static char Next(string text, int i) => i + 1 < text.Length ? text[i + 1] : '\0';

        /// <summary>The index just past the <c>]</c> matching the <c>[</c> at <paramref name="open"/> (string literals honoured).</summary>
        private static int SkipBracketed(string text, int open)
        {
            int depth = 0;
            for (int i = open; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '"')
                {
                    for (i++; i < text.Length && text[i] != '"'; i++)
                    {
                        if (text[i] == '\\')
                        {
                            i++;
                        }
                    }
                }
                else if (c == '[')
                {
                    depth++;
                }
                else if (c == ']' && --depth == 0)
                {
                    return i + 1;
                }
            }

            return text.Length;
        }
    }
}
