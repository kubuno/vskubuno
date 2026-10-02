using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Kubuno.Shared.DevAssistant.Logic.Markdown
{
    public enum MarkdownBlockKind
    {
        Paragraph,
        Heading,
        Code,
        Bullet,
        Numbered,
        Quote,
        Rule,
    }

    public enum MarkdownInlineKind
    {
        Text,
        Bold,
        Italic,
        Code,
        Link,
    }

    /// <summary>A run of inline text with one style.</summary>
    public sealed class MarkdownInline
    {
        public MarkdownInline(MarkdownInlineKind kind, string text, string? url = null)
        {
            Kind = kind;
            Text = text;
            Url = url;
        }

        public MarkdownInlineKind Kind { get; }

        public string Text { get; }

        public string? Url { get; }
    }

    /// <summary>One block of a rendered answer.</summary>
    public sealed class MarkdownBlock
    {
        public MarkdownBlock(MarkdownBlockKind kind, string text, int level = 0, string? language = null, bool isOpen = false)
        {
            Kind = kind;
            Text = text;
            Level = level;
            Language = language;
            IsOpen = isOpen;
        }

        public MarkdownBlockKind Kind { get; }

        /// <summary>Raw text (code: the code; others: the inline Markdown).</summary>
        public string Text { get; }

        /// <summary>Heading level, list item number.</summary>
        public int Level { get; }

        /// <summary>A code block's info string (<c>rust</c>, <c>xml</c>...).</summary>
        public string? Language { get; }

        /// <summary>A code block still streaming (no closing fence yet).</summary>
        public bool IsOpen { get; }

        public IReadOnlyList<MarkdownInline> Inlines => MarkdownParser.ParseInlines(Text);
    }

    /// <summary>
    /// A small Markdown block parser for the transcript (docs/AI-ASSISTANT.md section 5.1): headings, paragraphs, fenced
    /// code, bullet and numbered lists, quotes, rules, and inline bold/italic/code/links. Re-run on the whole message as
    /// it streams (an unclosed fence is an open code block), so it must stay linear and tolerant.
    /// </summary>
    public static class MarkdownParser
    {
        private static readonly Regex Heading = new Regex(@"^(#{1,6})\s+(.*)$", RegexOptions.CultureInvariant);
        private static readonly Regex Bullet = new Regex(@"^\s*[-*+]\s+(.*)$", RegexOptions.CultureInvariant);
        private static readonly Regex Numbered = new Regex(@"^\s*(\d+)[.)]\s+(.*)$", RegexOptions.CultureInvariant);
        private static readonly Regex Fence = new Regex(@"^\s*(```|~~~)\s*([\w#+\-.]*)", RegexOptions.CultureInvariant);
        private static readonly Regex Rule = new Regex(@"^\s*([-*_])(\s*\1){2,}\s*$", RegexOptions.CultureInvariant);
        private static readonly Regex Inline = new Regex(@"`(?<code>[^`]+)`|\*\*(?<bold>[^*]+)\*\*|__(?<bold2>[^_]+)__|(?<![\w*])\*(?<italic>[^*\s][^*]*)\*(?!\w)|(?<![\w_])_(?<italic2>[^_\s][^_]*)_(?![\w_])|\[(?<ltext>[^\]]+)\]\((?<url>[^)\s]+)\)", RegexOptions.CultureInvariant);

        public static IReadOnlyList<MarkdownBlock> Parse(string? markdown)
        {
            var blocks = new List<MarkdownBlock>();
            if (string.IsNullOrEmpty(markdown))
            {
                return blocks;
            }

            var lines = markdown!.Replace("\r\n", "\n").Split('\n');
            var paragraph = new StringBuilder();
            void FlushParagraph()
            {
                if (paragraph.Length > 0)
                {
                    blocks.Add(new MarkdownBlock(MarkdownBlockKind.Paragraph, paragraph.ToString().TrimEnd()));
                    paragraph.Clear();
                }
            }

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var fence = Fence.Match(line);
                if (fence.Success)
                {
                    FlushParagraph();
                    var marker = fence.Groups[1].Value;
                    var language = fence.Groups[2].Value;
                    var code = new StringBuilder();
                    bool closed = false;
                    for (i++; i < lines.Length; i++)
                    {
                        if (lines[i].TrimStart().StartsWith(marker, StringComparison.Ordinal) && lines[i].Trim().Length == marker.Length)
                        {
                            closed = true;
                            break;
                        }

                        if (code.Length > 0)
                        {
                            code.Append('\n');
                        }

                        code.Append(lines[i]);
                    }

                    blocks.Add(new MarkdownBlock(MarkdownBlockKind.Code, code.ToString(), 0, language.Length == 0 ? null : language, isOpen: !closed));
                    continue;
                }

                if (line.Trim().Length == 0)
                {
                    FlushParagraph();
                    continue;
                }

                Match match;
                if ((match = Heading.Match(line)).Success)
                {
                    FlushParagraph();
                    blocks.Add(new MarkdownBlock(MarkdownBlockKind.Heading, match.Groups[2].Value.Trim(), match.Groups[1].Length));
                }
                else if (Rule.IsMatch(line))
                {
                    FlushParagraph();
                    blocks.Add(new MarkdownBlock(MarkdownBlockKind.Rule, string.Empty));
                }
                else if ((match = Bullet.Match(line)).Success)
                {
                    FlushParagraph();
                    blocks.Add(new MarkdownBlock(MarkdownBlockKind.Bullet, match.Groups[1].Value));
                }
                else if ((match = Numbered.Match(line)).Success)
                {
                    FlushParagraph();
                    blocks.Add(new MarkdownBlock(MarkdownBlockKind.Numbered, match.Groups[2].Value, int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)));
                }
                else if (line.TrimStart().StartsWith(">", StringComparison.Ordinal))
                {
                    FlushParagraph();
                    blocks.Add(new MarkdownBlock(MarkdownBlockKind.Quote, line.TrimStart().Substring(1).Trim()));
                }
                else
                {
                    if (paragraph.Length > 0)
                    {
                        paragraph.Append(' ');
                    }

                    paragraph.Append(line.Trim());
                }
            }

            FlushParagraph();
            return blocks;
        }

        public static IReadOnlyList<MarkdownInline> ParseInlines(string? text)
        {
            var inlines = new List<MarkdownInline>();
            if (string.IsNullOrEmpty(text))
            {
                return inlines;
            }

            int position = 0;
            foreach (Match match in Inline.Matches(text!))
            {
                if (match.Index > position)
                {
                    inlines.Add(new MarkdownInline(MarkdownInlineKind.Text, text!.Substring(position, match.Index - position)));
                }

                if (match.Groups["code"].Success)
                {
                    inlines.Add(new MarkdownInline(MarkdownInlineKind.Code, match.Groups["code"].Value));
                }
                else if (match.Groups["bold"].Success || match.Groups["bold2"].Success)
                {
                    inlines.Add(new MarkdownInline(MarkdownInlineKind.Bold, match.Groups["bold"].Success ? match.Groups["bold"].Value : match.Groups["bold2"].Value));
                }
                else if (match.Groups["italic"].Success || match.Groups["italic2"].Success)
                {
                    inlines.Add(new MarkdownInline(MarkdownInlineKind.Italic, match.Groups["italic"].Success ? match.Groups["italic"].Value : match.Groups["italic2"].Value));
                }
                else
                {
                    inlines.Add(new MarkdownInline(MarkdownInlineKind.Link, match.Groups["ltext"].Value, match.Groups["url"].Value));
                }

                position = match.Index + match.Length;
            }

            if (position < text!.Length)
            {
                inlines.Add(new MarkdownInline(MarkdownInlineKind.Text, text.Substring(position)));
            }

            return inlines;
        }
    }
}
