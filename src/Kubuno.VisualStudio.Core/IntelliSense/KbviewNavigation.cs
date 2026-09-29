using System;
using System.Text.RegularExpressions;

namespace Kubuno.VisualStudio.Core.IntelliSense
{
    /// <summary>
    /// Go To Definition from a <c>.kbview</c> to Rust, where kubuno-views-ls has no answer: from an element's tag
    /// to the Rust type of the control, and from a binding path to the view model's <c>fn get</c> arm that answers it.
    /// </summary>
    public static class KbviewNavigation
    {
        /// <summary>The element name when <paramref name="offset"/> is on a tag's name (<c>&lt;Button</c>, <c>&lt;/Button&gt;</c>), else null (namespace prefixes dropped).</summary>
        public static string? ElementNameAt(string text, int offset)
        {
            if (text is null || offset < 0 || offset > text.Length)
            {
                return null;
            }

            int start = offset;
            while (start > 0 && IsNameChar(text[start - 1]))
            {
                start--;
            }

            int end = offset;
            while (end < text.Length && IsNameChar(text[end]))
            {
                end++;
            }

            if (end == start)
            {
                return null;
            }

            int before = start - 1;
            if (before >= 0 && text[before] == '/')
            {
                before--;
            }

            if (before < 0 || text[before] != '<')
            {
                return null;
            }

            var name = text.Substring(start, end - start);
            int colon = name.LastIndexOf(':');
            name = colon >= 0 ? name.Substring(colon + 1) : name;
            return name.Length > 0 && char.IsLetter(name[0]) ? name : null;
        }

        /// <summary>The binding path when <paramref name="offset"/> is on it (<c>{Binding Status}</c>, <c>{Binding Path=Status}</c>), else null.</summary>
        public static string? BindingPathAt(string text, int offset)
        {
            if (text is null || offset < 0 || offset > text.Length)
            {
                return null;
            }

            int open = text.LastIndexOf('{', Math.Max(0, Math.Min(offset, text.Length - 1)));
            if (open < 0)
            {
                return null;
            }

            int close = text.IndexOf('}', open);
            if (close < 0 || close < offset)
            {
                return null;
            }

            var match = Regex.Match(text.Substring(open, close - open + 1), @"^\{\s*Binding\s+(?:Path\s*=\s*)?(?<path>[A-Za-z_][A-Za-z0-9_\.]*)", RegexOptions.CultureInvariant);
            if (!match.Success)
            {
                return null;
            }

            var group = match.Groups["path"];
            int pathStart = open + group.Index;
            return offset >= pathStart && offset <= pathStart + group.Length ? group.Value : null;
        }

        /// <summary>The offset of the <c>"path" =&gt;</c> arm answering <paramref name="path"/> in a code-behind, or -1.</summary>
        public static int FindBindingArm(string rust, string path)
        {
            if (rust is null || string.IsNullOrEmpty(path))
            {
                return -1;
            }

            var match = Regex.Match(rust, "\"" + Regex.Escape(path) + "\"\\s*(\\|[^=]*)?=>", RegexOptions.CultureInvariant);
            return match.Success ? match.Index + 1 : -1;
        }

        private static bool IsNameChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == ':' || c == '.' || c == '-';
    }
}
