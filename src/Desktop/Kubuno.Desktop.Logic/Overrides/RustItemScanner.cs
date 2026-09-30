using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Kubuno.Desktop.Logic.Overrides
{
    /// <summary>A <c>struct</c> item of a Rust file.</summary>
    public sealed class RustStructItem
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>Where the item starts (its first attribute or doc comment).</summary>
        public int ItemStart { get; set; }

        /// <summary>The start of the line holding <c>struct</c> (after the attributes): where a new attribute goes.</summary>
        public int DeclarationLineStart { get; set; }

        /// <summary>Just past the item (its closing <c>}</c> or <c>;</c>).</summary>
        public int ItemEnd { get; set; }

        /// <summary>Its outer attributes, as written (<c>#[derive(Component, Default)]</c>…), with their spans.</summary>
        public List<(int Start, int End, string Text)> Attributes { get; } = new List<(int, int, string)>();

        /// <summary>The derives it names (last path segments).</summary>
        public IEnumerable<string> Derives =>
            Attributes.Where(a => Regex.IsMatch(a.Text, @"^#\[\s*derive\s*\(")).SelectMany(a => Regex.Matches(a.Text.Substring(a.Text.IndexOf('(') + 1), @"[A-Za-z_][\w:]*").Cast<Match>().Select(m => m.Value.Split(new[] { "::" }, StringSplitOptions.None).Last()));

        /// <summary>Its <c>#[kubuno(…)]</c> attribute, when it has one.</summary>
        public (int Start, int End, string Text)? KubunoAttribute => Attributes.Where(a => Regex.IsMatch(a.Text, @"^#\[\s*kubuno\s*\(")).Select(a => ((int, int, string)?)a).FirstOrDefault();

        /// <summary>The base <c>extends</c> names (last segment), when given.</summary>
        public string? Extends
        {
            get
            {
                var attr = KubunoAttribute;
                if (attr is null)
                {
                    return null;
                }

                var m = Regex.Match(attr.Value.Text, @"extends\s*=\s*""?([A-Za-z_][\w:]*)""?");
                return m.Success ? m.Groups[1].Value.Split(new[] { "::" }, StringSplitOptions.None).Last() : null;
            }
        }

        /// <summary>The levels named in <c>levels(…)</c> / <c>overrides(…)</c>.</summary>
        public IReadOnlyList<string> NamedLevels(string option)
        {
            var attr = KubunoAttribute;
            if (attr is null)
            {
                return Array.Empty<string>();
            }

            var m = Regex.Match(attr.Value.Text, option + @"\s*\(([^)]*)\)");
            return m.Success ? m.Groups[1].Value.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList() : (IReadOnlyList<string>)Array.Empty<string>();
        }

        public bool IsComponent => Derives.Any(d => d == "Component" || d == "UserControl");

        public bool IsUserControl => Derives.Any(d => d == "UserControl");
    }

    /// <summary>An <c>impl</c> block of a Rust file.</summary>
    public sealed class RustImplItem
    {
        /// <summary>The trait (last segment), null for an inherent impl.</summary>
        public string? Trait { get; set; }

        /// <summary>The type (last segment).</summary>
        public string Type { get; set; } = string.Empty;

        public int ItemStart { get; set; }

        public int OpenBrace { get; set; }

        public int CloseBrace { get; set; }

        /// <summary>The <c>fn</c> names at the impl's own level.</summary>
        public List<string> Methods { get; } = new List<string>();
    }

    /// <summary>
    /// A small, syntax-aware scan of a Rust file for what the override assistance needs (docs/EVENTS.md EVT-7b): its
    /// structs with their attributes and its <c>impl</c> blocks with their methods. Comments, strings and char
    /// literals are masked first, so braces and keywords inside them never count. Independent of rust-analyzer (it
    /// works on unsaved, even broken, code).
    /// </summary>
    public sealed class RustItemScanner
    {
        private RustItemScanner(string text, string masked)
        {
            Text = text;
            Masked = masked;
        }

        public string Text { get; }

        /// <summary>The text with comments, strings and char literals blanked (same length, line breaks kept).</summary>
        public string Masked { get; }

        public List<RustStructItem> Structs { get; } = new List<RustStructItem>();

        public List<RustImplItem> Impls { get; } = new List<RustImplItem>();

        /// <summary>Whether the file imports the Kubuno prelude.</summary>
        public bool HasPrelude => Regex.IsMatch(Masked, @"use\s+kubuno_views\s*::\s*prelude\s*::\s*\*");

        public static RustItemScanner Scan(string text)
        {
            text ??= string.Empty;
            var scanner = new RustItemScanner(text, Mask(text));
            scanner.FindStructs();
            scanner.FindImpls();
            return scanner;
        }

        /// <summary>Blanks comments, strings and char literals (keeps their line breaks).</summary>
        public static string Mask(string text)
        {
            var chars = text.ToCharArray();
            void Blank(int from, int to)
            {
                for (var k = from; k < to && k < chars.Length; k++)
                {
                    if (chars[k] != '\n' && chars[k] != '\r')
                    {
                        chars[k] = ' ';
                    }
                }
            }

            var i = 0;
            while (i < text.Length)
            {
                var c = text[i];
                if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
                {
                    var end = text.IndexOf('\n', i);
                    end = end < 0 ? text.Length : end;
                    Blank(i, end);
                    i = end;
                }
                else if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
                {
                    var depth = 1;
                    var j = i + 2;
                    while (j < text.Length && depth > 0)
                    {
                        if (text[j] == '/' && j + 1 < text.Length && text[j + 1] == '*')
                        {
                            depth++;
                            j += 2;
                        }
                        else if (text[j] == '*' && j + 1 < text.Length && text[j + 1] == '/')
                        {
                            depth--;
                            j += 2;
                        }
                        else
                        {
                            j++;
                        }
                    }

                    Blank(i, j);
                    i = j;
                }
                else if ((c == 'r' || (c == 'b' && i + 1 < text.Length && text[i + 1] == 'r')) && IsRawStringStart(text, i, out var hashes, out var quote) && (i == 0 || !IsIdent(text[i - 1])))
                {
                    var closing = "\"" + new string('#', hashes);
                    var end = text.IndexOf(closing, quote + 1, StringComparison.Ordinal);
                    end = end < 0 ? text.Length : end + closing.Length;
                    Blank(quote, end);
                    i = end;
                }
                else if (c == '"')
                {
                    var j = i + 1;
                    while (j < text.Length && text[j] != '"')
                    {
                        j += text[j] == '\\' ? 2 : 1;
                    }

                    Blank(i + 1, j);
                    i = Math.Min(j + 1, text.Length);
                }
                else if (c == '\'')
                {
                    // A char literal ('x', '\n', '\u{1F600}') - not a lifetime ('a, 'static).
                    if (i + 1 < text.Length && text[i + 1] == '\\')
                    {
                        var j = text.IndexOf('\'', i + 2);
                        j = j < 0 ? text.Length : j;
                        Blank(i + 1, j);
                        i = Math.Min(j + 1, text.Length);
                    }
                    else if (i + 2 < text.Length && text[i + 2] == '\'')
                    {
                        Blank(i + 1, i + 2);
                        i += 3;
                    }
                    else
                    {
                        i++;
                    }
                }
                else
                {
                    i++;
                }
            }

            return new string(chars);
        }

        private static bool IsRawStringStart(string text, int i, out int hashes, out int quote)
        {
            var j = text[i] == 'b' ? i + 2 : i + 1;
            hashes = 0;
            while (j < text.Length && text[j] == '#')
            {
                hashes++;
                j++;
            }

            quote = j;
            return j < text.Length && text[j] == '"';
        }

        private static bool IsIdent(char c) => char.IsLetterOrDigit(c) || c == '_';

        /// <summary>The offset just past the bracket matching the one at <paramref name="open"/>, in the masked text.</summary>
        public int MatchBracket(int open)
        {
            var o = Masked[open];
            var closeChar = o == '{' ? '}' : o == '(' ? ')' : o == '[' ? ']' : '>';
            var depth = 0;
            for (var i = open; i < Masked.Length; i++)
            {
                if (Masked[i] == o)
                {
                    depth++;
                }
                else if (Masked[i] == closeChar)
                {
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }
                }
            }

            return Masked.Length;
        }

        private void FindStructs()
        {
            foreach (Match m in Regex.Matches(Masked, @"\bstruct\s+([A-Za-z_]\w*)"))
            {
                var item = new RustStructItem { Name = m.Groups[1].Value };

                // The declaration's line, then the attributes above it (masked doc comments are blanks).
                var lineStart = Masked.LastIndexOf('\n', Math.Max(0, m.Index - 1)) + 1;
                item.DeclarationLineStart = lineStart;
                var start = lineStart;
                var cursor = lineStart;
                while (true)
                {
                    // Skip blank space backwards.
                    var j = cursor - 1;
                    while (j >= 0 && char.IsWhiteSpace(Masked[j]))
                    {
                        j--;
                    }

                    if (j < 0 || Masked[j] != ']')
                    {
                        break;
                    }

                    var open = FindAttributeStart(j);
                    if (open < 0)
                    {
                        break;
                    }

                    item.Attributes.Insert(0, (open, j + 1, Text.Substring(open, j + 1 - open)));
                    start = Masked.LastIndexOf('\n', Math.Max(0, open - 1)) + 1;
                    cursor = open;
                }

                // Its doc comments belong to the item too.
                while (start > 0)
                {
                    var previousStart = Text.LastIndexOf('\n', Math.Max(0, start - 2)) + 1;
                    if (previousStart >= start || !Text.Substring(previousStart, start - previousStart).TrimStart().StartsWith("///", StringComparison.Ordinal))
                    {
                        break;
                    }

                    start = previousStart;
                }

                item.ItemStart = start;

                // The body.
                var k = m.Index + m.Length;
                while (k < Masked.Length && Masked[k] != '{' && Masked[k] != ';' && Masked[k] != '(')
                {
                    k++;
                }

                if (k < Masked.Length && Masked[k] == '{')
                {
                    item.ItemEnd = MatchBracket(k) + 1;
                }
                else if (k < Masked.Length && Masked[k] == '(')
                {
                    var close = MatchBracket(k);
                    var semi = Masked.IndexOf(';', close);
                    item.ItemEnd = semi < 0 ? Masked.Length : semi + 1;
                }
                else
                {
                    item.ItemEnd = Math.Min(k + 1, Masked.Length);
                }

                Structs.Add(item);
            }
        }

        /// <summary>The <c>#</c> of the attribute whose closing <c>]</c> is at <paramref name="close"/>, or -1.</summary>
        private int FindAttributeStart(int close)
        {
            var depth = 0;
            for (var i = close; i >= 0; i--)
            {
                if (Masked[i] == ']')
                {
                    depth++;
                }
                else if (Masked[i] == '[')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return i > 0 && Masked[i - 1] == '#' ? i - 1 : -1;
                    }
                }
            }

            return -1;
        }

        private void FindImpls()
        {
            foreach (Match m in Regex.Matches(Masked, @"\bimpl\b"))
            {
                var i = m.Index + 4;
                i = SkipSpace(i);
                if (i < Masked.Length && Masked[i] == '<')
                {
                    i = SkipSpace(MatchAngles(i) + 1);
                }

                var first = ReadPath(ref i);
                if (first is null)
                {
                    continue;
                }

                i = SkipSpace(i);
                string? trait = null;
                var type = first;
                if (Masked.Length >= i + 3 && Masked.Substring(i, 3) == "for" && (i + 3 >= Masked.Length || !IsIdent(Masked[i + 3])))
                {
                    i = SkipSpace(i + 3);
                    trait = first;
                    type = ReadPath(ref i) ?? string.Empty;
                }

                var open = Masked.IndexOf('{', i);
                var semi = Masked.IndexOf(';', i);
                if (open < 0 || (semi >= 0 && semi < open))
                {
                    continue;
                }

                var item = new RustImplItem
                {
                    Trait = trait is null ? null : LastSegment(trait),
                    Type = LastSegment(type),
                    ItemStart = Masked.LastIndexOf('\n', Math.Max(0, m.Index - 1)) + 1,
                    OpenBrace = open,
                    CloseBrace = MatchBracket(open),
                };

                // Methods at the impl's own depth.
                var depth = 0;
                for (var p = open; p < item.CloseBrace; p++)
                {
                    if (Masked[p] == '{')
                    {
                        depth++;
                    }
                    else if (Masked[p] == '}')
                    {
                        depth--;
                    }
                    else if (depth == 1 && Masked[p] == 'f' && p + 2 < Masked.Length && Masked[p + 1] == 'n' && char.IsWhiteSpace(Masked[p + 2]) && (p == 0 || !IsIdent(Masked[p - 1])))
                    {
                        var q = SkipSpace(p + 2);
                        var name = new string(Masked.Skip(q).TakeWhile(IsIdent).ToArray());
                        if (name.Length > 0)
                        {
                            item.Methods.Add(name);
                        }
                    }
                }

                Impls.Add(item);
            }
        }

        private int SkipSpace(int i)
        {
            while (i < Masked.Length && char.IsWhiteSpace(Masked[i]))
            {
                i++;
            }

            return i;
        }

        private int MatchAngles(int open)
        {
            var depth = 0;
            for (var i = open; i < Masked.Length; i++)
            {
                if (Masked[i] == '<')
                {
                    depth++;
                }
                else if (Masked[i] == '>' && (i == 0 || Masked[i - 1] != '-'))
                {
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }
                }
            }

            return Masked.Length - 1;
        }

        /// <summary>Reads a type path (<c>a::B&lt;T&gt;</c>, <c>dyn X</c> not supported); null when there is none.</summary>
        private string? ReadPath(ref int i)
        {
            var start = i;
            while (i < Masked.Length && (IsIdent(Masked[i]) || Masked[i] == ':'))
            {
                i++;
            }

            if (i == start)
            {
                return null;
            }

            var path = Masked.Substring(start, i - start);
            if (i < Masked.Length && Masked[i] == '<')
            {
                i = MatchAngles(i) + 1;
            }

            return path;
        }

        private static string LastSegment(string path) => path.Split(new[] { "::" }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? path;
    }
}
