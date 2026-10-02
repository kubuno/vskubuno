using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Kubuno.Rust.Logic.SolutionExplorer;
using Kubuno.Shared.Logic.QuickInfo;

namespace Kubuno.Rust.Logic.QuickInfo
{
    /// <summary>
    /// A rust-analyzer <c>textDocument/hover</c> markdown, taken apart: rust-analyzer writes the containing
    /// path in a first <c>```rust</c> block, the declaration in a second one, then <c>---</c>-separated
    /// sections (memory layout, generic substitutions, dyn-compatibility, variance, and the rustdoc).
    /// </summary>
    public sealed class RustHover
    {
        private static readonly Regex NotePattern = new Regex(
            @"^(size = |align = |.*\bdyn[- ]compatible\b|(in|co|contra|bi)variant$|`[^`]+` = `)",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private RustHover(string? container, string signature, RustItemKind kind, SymbolVisibility visibility, IReadOnlyList<string> notes, string documentation)
        {
            Container = container;
            Signature = signature;
            Kind = kind;
            Visibility = visibility;
            Notes = notes;
            Documentation = documentation;
        }

        /// <summary>The containing path (<c>kubuno_views::events</c>), or null (locals, parameters, keywords).</summary>
        public string? Container { get; }

        /// <summary>The declaration on one line (<c>pub fn new(x: f64, y: f64) -&gt; Self</c>).</summary>
        public string Signature { get; }

        public RustItemKind Kind { get; }

        public SymbolVisibility Visibility { get; }

        /// <summary>Short facts rust-analyzer adds (memory layout, <c>`K` = `String`</c> substitutions...).</summary>
        public IReadOnlyList<string> Notes { get; }

        /// <summary>The rustdoc, as markdown.</summary>
        public string Documentation { get; }

        public static RustHover? Parse(string? markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown))
            {
                return null;
            }

            var sections = SplitSections(markdown!);
            var head = Markdown.Parse(sections[0]).Blocks;
            var codeBlocks = head.TakeWhile(b => b.Kind == MarkdownBlockKind.Code).Take(2).Select(b => b.Text).ToList();
            if (codeBlocks.Count == 0 || head.Count > codeBlocks.Count)
            {
                // Not rust-analyzer's usual shape: show it all as documentation.
                return new RustHover(null, string.Empty, RustItemKind.Other, SymbolVisibility.Public, Array.Empty<string>(), markdown!.Trim());
            }

            string? container = codeBlocks.Count == 2 ? codeBlocks[0].Trim() : null;
            var declaration = codeBlocks[codeBlocks.Count - 1];
            var signature = RustSyntax.ToOneLine(declaration, container, out var kind);
            var visibility = RustVisibilityReader.Read(signature);

            var notes = new List<string>();
            var docs = new List<string>();
            foreach (var section in sections.Skip(1))
            {
                var trimmed = section.Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }

                if (trimmed.StartsWith("```rust", StringComparison.Ordinal))
                {
                    // rust-analyzer joins several hover results with "---" (e.g. a type and the crate it
                    // comes from): only the first one is this tooltip's subject.
                    break;
                }

                if (!trimmed.Contains("\n\n") && trimmed.Split('\n').All(line => NotePattern.IsMatch(line.Trim())))
                {
                    notes.AddRange(trimmed.Split('\n').Select(line => Markdown.ToPlainText(line.Trim())));
                }
                else
                {
                    docs.Add(trimmed);
                }
            }

            return new RustHover(container, signature, kind, visibility, notes, string.Join("\n\n", docs));
        }

        /// <summary>Splits on the <c>---</c> lines that are not inside a fenced code block.</summary>
        private static List<string> SplitSections(string markdown)
        {
            var sections = new List<string>();
            var current = new List<string>();
            string? fence = null;
            foreach (var line in markdown.Replace("\r\n", "\n").Split('\n'))
            {
                var trimmed = line.Trim();
                if (fence == null && (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal)))
                {
                    fence = trimmed.Substring(0, 3);
                }
                else if (fence != null && trimmed.StartsWith(fence, StringComparison.Ordinal))
                {
                    fence = null;
                }
                else if (fence == null && trimmed == "---")
                {
                    sections.Add(string.Join("\n", current));
                    current.Clear();
                    continue;
                }

                current.Add(line);
            }

            sections.Add(string.Join("\n", current));
            return sections;
        }

        /// <summary>The <c>KnownMonikers</c> name of the icon, as Roslyn picks it for C# (kind + accessibility).</summary>
        public string IconMonikerName
        {
            get
            {
                switch (Kind)
                {
                    case RustItemKind.Local: return "LocalVariable";
                    case RustItemKind.Parameter: return "Parameter";
                    case RustItemKind.TypeParameter:
                    case RustItemKind.Lifetime:
                        return "Type";
                    case RustItemKind.Keyword: return "IntellisenseKeyword";
                    case RustItemKind.Crate: return "Assembly";
                    case RustItemKind.Other: return "Type";
                }

                return SymbolMonikerNames.ForRust(ToSymbolKind(Kind), Kind == RustItemKind.Variant ? SymbolVisibility.Public : Visibility);
            }
        }

        private static SolutionSymbolKind ToSymbolKind(RustItemKind kind)
        {
            switch (kind)
            {
                case RustItemKind.Function: return SolutionSymbolKind.Function;
                case RustItemKind.Method: return SolutionSymbolKind.Method;
                case RustItemKind.Struct: return SolutionSymbolKind.Struct;
                case RustItemKind.Enum: return SolutionSymbolKind.Enum;
                case RustItemKind.Union: return SolutionSymbolKind.Union;
                case RustItemKind.Trait: return SolutionSymbolKind.Trait;
                case RustItemKind.TypeAlias: return SolutionSymbolKind.TypeAlias;
                case RustItemKind.Const: return SolutionSymbolKind.Const;
                case RustItemKind.Static: return SolutionSymbolKind.Static;
                case RustItemKind.Module: return SolutionSymbolKind.Module;
                case RustItemKind.Macro: return SolutionSymbolKind.Macro;
                case RustItemKind.Field: return SolutionSymbolKind.Field;
                case RustItemKind.Variant: return SolutionSymbolKind.Variant;
                default: return SolutionSymbolKind.Other;
            }
        }

        /// <summary>
        /// The tooltip, laid out like a C# QuickInfo: icon + classified one-line signature, the containing
        /// path in grey, then the documentation and rust-analyzer's notes.
        /// </summary>
        public QuickInfoElement ToQuickInfo()
        {
            var rows = new List<QuickInfoElement>();
            if (Signature.Length > 0)
            {
                rows.Add(QuickInfoLayout.Header(
                    QuickInfoImage.Moniker(IconMonikerName),
                    RustSyntax.ClassifySignature(Signature, Kind),
                    Container));
            }

            var documentation = DocumentationRenderer.Render(Documentation);
            if (documentation != null)
            {
                rows.Add(documentation);
            }

            if (Notes.Count > 0)
            {
                rows.Add(new QuickInfoContainer(
                    QuickInfoContainerStyle.Stacked,
                    Notes.Select(note => (QuickInfoElement)new QuickInfoText(new QuickInfoRun(QuickInfoTextKind.Muted, note)))));
            }

            return new QuickInfoContainer(QuickInfoContainerStyle.Stacked | QuickInfoContainerStyle.VerticalPadding, rows);
        }
    }
}
