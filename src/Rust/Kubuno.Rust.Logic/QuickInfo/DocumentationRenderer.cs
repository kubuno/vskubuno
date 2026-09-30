using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Kubuno.VisualStudio.Core.QuickInfo
{
    /// <summary>
    /// Turns hover documentation (markdown) into QuickInfo elements, the way a C# tooltip shows an XML doc
    /// comment: the summary (everything before the first heading) as readable text - paragraphs, inline code
    /// in the code font, bullet lists, clickable links, code examples as classified monospace lines - plus
    /// the sections a caller must know about (<c># Panics</c>, <c># Errors</c>, <c># Safety</c>, like
    /// Roslyn's "Exceptions:"/"Returns:"). Long documentation is cut, ending with "…".
    /// </summary>
    public static class DocumentationRenderer
    {
        /// <summary>Roughly how many characters of documentation a tooltip shows before cutting.</summary>
        public const int DefaultBudget = 1400;

        /// <summary>The maximum number of lines of one code example.</summary>
        public const int MaxCodeLines = 8;

        private static readonly Regex KeptSections = new Regex(
            @"^(panics|errors|safety|returns|arguments|parameters|paniques|erreurs|s[ée]curit[ée]|retour|arguments|param[èe]tres)\b",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        /// <summary>The documentation as one stacked element, or null when there is none.</summary>
        public static QuickInfoElement? Render(string? markdown, int budget = DefaultBudget)
        {
            if (string.IsNullOrWhiteSpace(markdown))
            {
                return null;
            }

            var document = Markdown.Parse(markdown);
            var rows = new List<QuickInfoElement>();
            List<QuickInfoElement>? list = null;
            bool inKeptPart = true;
            bool truncated = false;
            int used = 0;

            void CloseList()
            {
                if (list != null)
                {
                    rows.Add(new QuickInfoContainer(QuickInfoContainerStyle.Stacked, list));
                    list = null;
                }
            }

            foreach (var block in document.Blocks)
            {
                if (block.Kind == MarkdownBlockKind.Heading)
                {
                    inKeptPart = KeptSections.IsMatch(Markdown.ToPlainText(block.Text).Trim());
                    if (!inKeptPart)
                    {
                        continue;
                    }
                }

                if (!inKeptPart || block.Kind == MarkdownBlockKind.Rule)
                {
                    continue;
                }

                int cost = block.Kind == MarkdownBlockKind.Code ? Math.Min(block.Text.Length, 80 * MaxCodeLines) : block.Text.Length;
                if (used > 0 && used + cost > budget)
                {
                    truncated = true;
                    break;
                }

                used += cost;
                switch (block.Kind)
                {
                    case MarkdownBlockKind.Paragraph:
                        CloseList();
                        rows.Add(new QuickInfoText(Markdown.ParseInline(block.Text, document.References)));
                        break;
                    case MarkdownBlockKind.Heading:
                        CloseList();
                        rows.Add(new QuickInfoText(Markdown.ParseInline(block.Text, document.References, baseStyle: QuickInfoRunStyle.Bold)));
                        break;
                    case MarkdownBlockKind.ListItem:
                        list ??= new List<QuickInfoElement>();
                        list.Add(new QuickInfoContainer(
                            QuickInfoContainerStyle.Wrapped,
                            new QuickInfoText(new QuickInfoRun(QuickInfoTextKind.Text, new string(' ', 4 * block.Level) + block.Marker + " ")),
                            new QuickInfoText(Markdown.ParseInline(block.Text, document.References))));
                        break;
                    case MarkdownBlockKind.Code:
                        CloseList();
                        rows.Add(RenderCode(block.Text, block.Language));
                        break;
                }
            }

            CloseList();
            if (truncated)
            {
                rows.Add(new QuickInfoText(new QuickInfoRun(QuickInfoTextKind.Muted, "…")));
            }

            return rows.Count == 0 ? null : new QuickInfoContainer(QuickInfoContainerStyle.Stacked | QuickInfoContainerStyle.VerticalPadding, rows);
        }

        /// <summary>A code block: one classified line per source line (Rust, the rustdoc default) or plain code-font lines.</summary>
        public static QuickInfoElement RenderCode(string code, string? language)
        {
            bool isRust = string.IsNullOrEmpty(language) || language!.StartsWith("rust", StringComparison.OrdinalIgnoreCase)
                || language.Equals("rs", StringComparison.OrdinalIgnoreCase) || language.StartsWith("no_run", StringComparison.OrdinalIgnoreCase)
                || language.StartsWith("should_panic", StringComparison.OrdinalIgnoreCase) || language.StartsWith("ignore", StringComparison.OrdinalIgnoreCase)
                || language.StartsWith("compile_fail", StringComparison.OrdinalIgnoreCase) || language.StartsWith("edition", StringComparison.OrdinalIgnoreCase);
            var lines = code.Replace("\r\n", "\n").Split('\n')
                .Where(line => !isRust || !(line.TrimStart().StartsWith("# ", StringComparison.Ordinal) || line.Trim() == "#"))
                .ToList();
            while (lines.Count > 0 && lines[lines.Count - 1].Trim().Length == 0)
            {
                lines.RemoveAt(lines.Count - 1);
            }

            bool cut = lines.Count > MaxCodeLines;
            var rows = lines.Take(MaxCodeLines).Select(line =>
            {
                var text = line.Replace("\t", "    ");
                if (text.Length == 0)
                {
                    return new QuickInfoText(new QuickInfoRun(QuickInfoTextKind.Text, " ", QuickInfoRunStyle.Code));
                }

                return isRust
                    ? new QuickInfoText(RustSyntax.ClassifyCode(text))
                    : new QuickInfoText(new QuickInfoRun(QuickInfoTextKind.Text, text, QuickInfoRunStyle.Code));
            }).Cast<QuickInfoElement>().ToList();
            if (cut)
            {
                rows.Add(new QuickInfoText(new QuickInfoRun(QuickInfoTextKind.Muted, "…", QuickInfoRunStyle.Code)));
            }

            return new QuickInfoContainer(QuickInfoContainerStyle.Stacked, rows);
        }
    }

    /// <summary>Layout pieces shared by the Rust and <c>.kbview</c> tooltips.</summary>
    public static class QuickInfoLayout
    {
        /// <summary>
        /// The first row of a tooltip: the symbol icon, then the classified signature with, below it, the
        /// containing path in grey (the way a C# tooltip reads <c>void Form1.InitializeComponent()</c>).
        /// </summary>
        public static QuickInfoElement Header(QuickInfoImage icon, IEnumerable<QuickInfoRun> signature, string? container)
        {
            QuickInfoElement text = new QuickInfoText(signature);
            if (!string.IsNullOrEmpty(container))
            {
                text = new QuickInfoContainer(
                    QuickInfoContainerStyle.Stacked,
                    text,
                    new QuickInfoText(new QuickInfoRun(QuickInfoTextKind.Muted, container!)));
            }

            return new QuickInfoContainer(QuickInfoContainerStyle.Wrapped, icon, text);
        }
    }
}
