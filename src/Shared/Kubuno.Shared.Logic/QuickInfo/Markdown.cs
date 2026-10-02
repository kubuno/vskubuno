using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Kubuno.Shared.Logic.QuickInfo
{
    /// <summary>The block kinds of <see cref="Markdown"/>.</summary>
    public enum MarkdownBlockKind
    {
        Paragraph,
        Heading,
        ListItem,
        Code,
        Rule,
    }

    /// <summary>One block of a parsed markdown document.</summary>
    public sealed class MarkdownBlock
    {
        public MarkdownBlock(MarkdownBlockKind kind, string text, int level = 0, string? language = null, string? marker = null)
        {
            Kind = kind;
            Text = text;
            Level = level;
            Language = language;
            Marker = marker;
        }

        public MarkdownBlockKind Kind { get; }

        /// <summary>Inline markdown (paragraph, heading, list item) or raw code (code block, newline-separated).</summary>
        public string Text { get; }

        /// <summary>Heading level (1-6), or list nesting level (0 = top level).</summary>
        public int Level { get; }

        /// <summary>The info string of a fenced code block (<c>rust</c>, <c>text</c>...), empty when absent.</summary>
        public string? Language { get; }

        /// <summary>The list marker as it will be displayed (<c>•</c>, <c>1.</c>).</summary>
        public string? Marker { get; }

        public override string ToString() => Kind + ": " + Text;
    }

    /// <summary>A parsed markdown document: its blocks plus the link reference definitions it declared.</summary>
    public sealed class MarkdownDocument
    {
        public MarkdownDocument(IReadOnlyList<MarkdownBlock> blocks, IReadOnlyDictionary<string, string> references)
        {
            Blocks = blocks;
            References = references;
        }

        public IReadOnlyList<MarkdownBlock> Blocks { get; }

        /// <summary><c>[label]: url</c> definitions, keyed by lower-cased label.</summary>
        public IReadOnlyDictionary<string, string> References { get; }
    }

    /// <summary>
    /// The small markdown subset language servers put in hovers (rust-analyzer renders rustdoc to it):
    /// paragraphs, ATX headings, bullet/numbered lists, fenced code blocks, rules, link reference
    /// definitions; inline code, bold, italic, links (inline, reference, shortcut, autolinks), escapes.
    /// Not a full CommonMark implementation - only what a tooltip needs, without a dependency.
    /// </summary>
    public static class Markdown
    {
        private static readonly Regex Fence = new Regex(@"^ {0,3}(?<fence>`{3,}|~{3,})\s*(?<info>[^`\s]*)", RegexOptions.CultureInvariant);
        private static readonly Regex ReferenceDefinition = new Regex(@"^ {0,3}\[(?<label>[^\]]+)\]:\s*<?(?<url>[^\s>]+)>?(?:\s+.*)?$", RegexOptions.CultureInvariant);
        private static readonly Regex Heading = new Regex(@"^ {0,3}(?<hashes>#{1,6})\s+(?<text>.*?)\s*#*\s*$", RegexOptions.CultureInvariant);
        private static readonly Regex Rule = new Regex(@"^ {0,3}((-\s*){3,}|(\*\s*){3,}|(_\s*){3,})$", RegexOptions.CultureInvariant);
        private static readonly Regex ListItem = new Regex(@"^(?<indent> *)(?<marker>[-*+]|\d{1,9}[.)])\s+(?<text>.*)$", RegexOptions.CultureInvariant);

        public static MarkdownDocument Parse(string? markdown)
        {
            var blocks = new List<MarkdownBlock>();
            var references = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var lines = (markdown ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            var paragraph = new StringBuilder();
            var item = new StringBuilder();
            int itemLevel = 0;
            string? itemMarker = null;
            var listIndents = new List<int>();

            void FlushParagraph()
            {
                if (paragraph.Length > 0)
                {
                    blocks.Add(new MarkdownBlock(MarkdownBlockKind.Paragraph, paragraph.ToString()));
                    paragraph.Clear();
                }
            }

            void FlushItem()
            {
                if (itemMarker != null)
                {
                    blocks.Add(new MarkdownBlock(MarkdownBlockKind.ListItem, item.ToString(), itemLevel, marker: itemMarker));
                    item.Clear();
                    itemMarker = null;
                }
            }

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var fence = Fence.Match(line);
                if (fence.Success)
                {
                    FlushParagraph();
                    FlushItem();
                    var closing = fence.Groups["fence"].Value;
                    var code = new List<string>();
                    int indent = line.Length - line.TrimStart(' ').Length;
                    for (i++; i < lines.Length; i++)
                    {
                        var trimmed = lines[i].TrimStart(' ');
                        if (trimmed.StartsWith(closing, StringComparison.Ordinal) && trimmed.Trim().Trim(closing[0]).Length == 0)
                        {
                            break;
                        }

                        code.Add(RemoveIndent(lines[i], indent));
                    }

                    blocks.Add(new MarkdownBlock(MarkdownBlockKind.Code, string.Join("\n", code), language: fence.Groups["info"].Value));
                    continue;
                }

                if (line.Trim().Length == 0)
                {
                    FlushParagraph();
                    FlushItem();
                    listIndents.Clear();
                    continue;
                }

                var reference = ReferenceDefinition.Match(line);
                if (reference.Success && paragraph.Length == 0)
                {
                    FlushItem();
                    var label = reference.Groups["label"].Value.Trim();
                    if (!references.ContainsKey(label))
                    {
                        references[label] = reference.Groups["url"].Value;
                    }

                    continue;
                }

                var heading = Heading.Match(line);
                if (heading.Success)
                {
                    FlushParagraph();
                    FlushItem();
                    blocks.Add(new MarkdownBlock(MarkdownBlockKind.Heading, heading.Groups["text"].Value, heading.Groups["hashes"].Value.Length));
                    continue;
                }

                if (Rule.IsMatch(line))
                {
                    FlushParagraph();
                    FlushItem();
                    blocks.Add(new MarkdownBlock(MarkdownBlockKind.Rule, string.Empty));
                    continue;
                }

                var listItem = ListItem.Match(line);
                if (listItem.Success)
                {
                    FlushParagraph();
                    FlushItem();
                    int indent = listItem.Groups["indent"].Value.Length;
                    while (listIndents.Count > 0 && listIndents[listIndents.Count - 1] > indent)
                    {
                        listIndents.RemoveAt(listIndents.Count - 1);
                    }

                    if (listIndents.Count == 0 || listIndents[listIndents.Count - 1] < indent)
                    {
                        listIndents.Add(indent);
                    }

                    itemLevel = listIndents.Count - 1;
                    var marker = listItem.Groups["marker"].Value;
                    itemMarker = char.IsDigit(marker[0]) ? marker.TrimEnd(')', '.') + "." : "•";
                    item.Append(listItem.Groups["text"].Value.Trim());
                    continue;
                }

                var text = line.Trim();
                if (text.StartsWith(">", StringComparison.Ordinal))
                {
                    text = text.TrimStart('>').Trim();
                }

                if (itemMarker != null)
                {
                    AppendSoft(item, line, text);
                }
                else
                {
                    AppendSoft(paragraph, line, text);
                }
            }

            FlushParagraph();
            FlushItem();
            return new MarkdownDocument(blocks, references);
        }

        /// <summary>Appends a continuation line: a soft break is a space, a hard break (two trailing spaces or a backslash) a newline.</summary>
        private static void AppendSoft(StringBuilder target, string rawLine, string text)
        {
            if (target.Length > 0)
            {
                bool hard = target[target.Length - 1] == '\n';
                if (!hard)
                {
                    target.Append(' ');
                }
            }

            if (rawLine.EndsWith("  ", StringComparison.Ordinal))
            {
                target.Append(text).Append('\n');
            }
            else if (text.EndsWith("\\", StringComparison.Ordinal) && !text.EndsWith("\\\\", StringComparison.Ordinal))
            {
                target.Append(text, 0, text.Length - 1).Append('\n');
            }
            else
            {
                target.Append(text);
            }
        }

        private static string RemoveIndent(string line, int indent)
        {
            int i = 0;
            while (i < indent && i < line.Length && line[i] == ' ')
            {
                i++;
            }

            return line.Substring(i);
        }

        /// <summary>
        /// Parses inline markdown into runs of <paramref name="baseKind"/>: code spans get
        /// <see cref="QuickInfoRunStyle.Code"/>, emphasis Bold/Italic, links a <see cref="QuickInfoRun.Url"/>
        /// (only http/https targets are kept - an unresolved intra-doc link such as <c>[`str`]</c> shows its
        /// text, without the brackets when it is a code span).
        /// </summary>
        public static List<QuickInfoRun> ParseInline(string text, IReadOnlyDictionary<string, string>? references = null, QuickInfoTextKind baseKind = QuickInfoTextKind.Text, QuickInfoRunStyle baseStyle = QuickInfoRunStyle.None)
        {
            var builder = new RunBuilder();
            ParseInline(text ?? string.Empty, references ?? EmptyReferences, baseKind, baseStyle, null, builder);
            return builder.Build();
        }

        private static readonly IReadOnlyDictionary<string, string> EmptyReferences = new Dictionary<string, string>();

        private static void ParseInline(string text, IReadOnlyDictionary<string, string> references, QuickInfoTextKind kind, QuickInfoRunStyle style, string? url, RunBuilder output)
        {
            var plain = new StringBuilder();

            void FlushPlain()
            {
                if (plain.Length > 0)
                {
                    output.Add(kind, plain.ToString(), style, url);
                    plain.Clear();
                }
            }

            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];

                // Backslash escape.
                if (c == '\\' && i + 1 < text.Length && IsAsciiPunctuation(text[i + 1]))
                {
                    plain.Append(text[i + 1]);
                    i += 2;
                    continue;
                }

                // Code span: a run of N backticks up to the next run of exactly N.
                if (c == '`')
                {
                    int ticks = CountRun(text, i, '`');
                    int close = FindBacktickRun(text, i + ticks, ticks);
                    if (close >= 0)
                    {
                        FlushPlain();
                        var code = text.Substring(i + ticks, close - i - ticks).Replace('\n', ' ');
                        if (code.Length >= 2 && code[0] == ' ' && code[code.Length - 1] == ' ' && code.Trim().Length > 0)
                        {
                            code = code.Substring(1, code.Length - 2);
                        }

                        output.Add(kind, code, style | QuickInfoRunStyle.Code, url);
                        i = close + ticks;
                        continue;
                    }

                    plain.Append(text, i, ticks);
                    i += ticks;
                    continue;
                }

                // Emphasis: ** / __ (bold), * / _ (italic).
                if (c == '*' || c == '_')
                {
                    int run = CountRun(text, i, c);
                    int width = run >= 2 ? 2 : 1;
                    bool leftFlanking = i + width < text.Length && !char.IsWhiteSpace(text[i + width]);
                    bool intraword = c == '_' && i > 0 && char.IsLetterOrDigit(text[i - 1]);
                    if (leftFlanking && !intraword)
                    {
                        int close = FindEmphasisClose(text, i + width, c, width);
                        if (close >= 0)
                        {
                            FlushPlain();
                            var inner = text.Substring(i + width, close - i - width);
                            ParseInline(inner, references, kind, style | (width == 2 ? QuickInfoRunStyle.Bold : QuickInfoRunStyle.Italic), url, output);
                            i = close + width;
                            continue;
                        }
                    }

                    plain.Append(text, i, run);
                    i += run;
                    continue;
                }

                // Images: keep the alt text.
                if (c == '!' && i + 1 < text.Length && text[i + 1] == '[')
                {
                    int closeBracket = FindClosingBracket(text, i + 1);
                    if (closeBracket > 0 && closeBracket + 1 < text.Length && text[closeBracket + 1] == '(')
                    {
                        int closeParen = text.IndexOf(')', closeBracket + 2);
                        if (closeParen > 0)
                        {
                            plain.Append(text, i + 2, closeBracket - i - 2);
                            i = closeParen + 1;
                            continue;
                        }
                    }
                }

                // Links.
                if (c == '[')
                {
                    int closeBracket = FindClosingBracket(text, i);
                    if (closeBracket > 0)
                    {
                        var label = text.Substring(i + 1, closeBracket - i - 1);
                        string? target = null;
                        int next = closeBracket + 1;
                        bool isLink = false;
                        if (next < text.Length && text[next] == '(')
                        {
                            int closeParen = FindClosingParen(text, next);
                            if (closeParen > 0)
                            {
                                var destination = text.Substring(next + 1, closeParen - next - 1).Trim();
                                int space = destination.IndexOfAny(new[] { ' ', '\t' });
                                target = (space > 0 ? destination.Substring(0, space) : destination).Trim('<', '>');
                                next = closeParen + 1;
                                isLink = true;
                            }
                        }
                        else if (next < text.Length && text[next] == '[')
                        {
                            int closeRef = text.IndexOf(']', next + 1);
                            if (closeRef > 0)
                            {
                                var refLabel = text.Substring(next + 1, closeRef - next - 1);
                                references.TryGetValue(refLabel.Length == 0 ? label : refLabel, out target);
                                next = closeRef + 1;
                                isLink = true;
                            }
                        }
                        else if (references.TryGetValue(label, out var shortcut))
                        {
                            target = shortcut;
                            isLink = true;
                        }
                        else if (label.StartsWith("`", StringComparison.Ordinal) && label.EndsWith("`", StringComparison.Ordinal))
                        {
                            // An unresolved intra-doc link ([`str`]): show the code, not the brackets.
                            isLink = true;
                        }

                        if (isLink)
                        {
                            FlushPlain();
                            ParseInline(label, references, kind, style, IsWebUrl(target) ? target : url, output);
                            i = next;
                            continue;
                        }
                    }
                }

                // Autolinks and inline HTML.
                if (c == '<')
                {
                    int close = text.IndexOf('>', i + 1);
                    if (close > 0)
                    {
                        var inner = text.Substring(i + 1, close - i - 1);
                        if (IsWebUrl(inner))
                        {
                            FlushPlain();
                            output.Add(kind, inner, style, inner);
                            i = close + 1;
                            continue;
                        }

                        if (Regex.IsMatch(inner, @"^/?[A-Za-z][A-Za-z0-9]*(\s[^<>]*)?/?$"))
                        {
                            if (inner.StartsWith("br", StringComparison.OrdinalIgnoreCase))
                            {
                                plain.Append('\n');
                            }

                            i = close + 1;
                            continue;
                        }
                    }
                }

                plain.Append(c);
                i++;
            }

            FlushPlain();
        }

        public static bool IsWebUrl(string? url) =>
            url != null
            && (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            && Uri.IsWellFormedUriString(url, UriKind.Absolute);

        private static bool IsAsciiPunctuation(char c) => c < 128 && char.IsPunctuation(c) || c == '`' || c == '*' || c == '_' || c == '<' || c == '>' || c == '|' || c == '~' || c == '^' || c == '+' || c == '=' || c == '$';

        private static int CountRun(string text, int start, char c)
        {
            int n = 0;
            while (start + n < text.Length && text[start + n] == c)
            {
                n++;
            }

            return n;
        }

        private static int FindBacktickRun(string text, int start, int ticks)
        {
            int i = start;
            while (i < text.Length)
            {
                if (text[i] == '`')
                {
                    int run = CountRun(text, i, '`');
                    if (run == ticks)
                    {
                        return i;
                    }

                    i += run;
                }
                else
                {
                    i++;
                }
            }

            return -1;
        }

        private static int FindEmphasisClose(string text, int start, char c, int width)
        {
            int i = start;
            while (i < text.Length)
            {
                char ch = text[i];
                if (ch == '\\')
                {
                    i += 2;
                    continue;
                }

                if (ch == '`')
                {
                    int ticks = CountRun(text, i, '`');
                    int close = FindBacktickRun(text, i + ticks, ticks);
                    i = close >= 0 ? close + ticks : i + ticks;
                    continue;
                }

                if (ch == c)
                {
                    int run = CountRun(text, i, c);
                    bool rightFlanking = i > start && !char.IsWhiteSpace(text[i - 1]);
                    bool intraword = c == '_' && i + run < text.Length && char.IsLetterOrDigit(text[i + run]);
                    if (rightFlanking && !intraword && (run == width || (run >= 3 && width <= 2)))
                    {
                        return run >= 3 && width == 1 ? i + run - 1 : i;
                    }

                    if (width == 1 && run == 2)
                    {
                        // A nested bold span inside an italic one: skip it whole.
                        int nested = FindEmphasisClose(text, i + 2, c, 2);
                        if (nested > 0)
                        {
                            i = nested + 2;
                            continue;
                        }
                    }

                    i += run;
                    continue;
                }

                i++;
            }

            return -1;
        }

        private static int FindClosingBracket(string text, int open)
        {
            int depth = 0;
            for (int i = open; i < text.Length; i++)
            {
                char ch = text[i];
                if (ch == '\\')
                {
                    i++;
                }
                else if (ch == '`')
                {
                    int ticks = CountRun(text, i, '`');
                    int close = FindBacktickRun(text, i + ticks, ticks);
                    if (close >= 0)
                    {
                        i = close + ticks - 1;
                    }
                }
                else if (ch == '[')
                {
                    depth++;
                }
                else if (ch == ']' && --depth == 0)
                {
                    return i;
                }
            }

            return -1;
        }

        private static int FindClosingParen(string text, int open)
        {
            int depth = 0;
            for (int i = open; i < text.Length; i++)
            {
                if (text[i] == '(')
                {
                    depth++;
                }
                else if (text[i] == ')' && --depth == 0)
                {
                    return i;
                }
                else if (text[i] == ' ' && depth == 1 && i + 1 < text.Length && text[i + 1] != '"' && text[i + 1] != '\'')
                {
                    return -1;
                }
            }

            return -1;
        }

        /// <summary>Removes markdown syntax, keeping the text (used for plain-text comparisons and headings).</summary>
        public static string ToPlainText(string inline) => string.Concat(ParseInline(inline).Select(r => r.Text));

        public static IEnumerable<string> SplitLines(string text) => text.Replace("\r\n", "\n").Split('\n');
    }
}
