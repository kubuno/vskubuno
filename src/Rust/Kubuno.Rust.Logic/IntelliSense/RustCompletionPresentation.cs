using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Kubuno.Shared.Logic.QuickInfo;
using Kubuno.Rust.Logic.QuickInfo;
using Kubuno.Rust.Logic.SolutionExplorer;

namespace Kubuno.Rust.Logic.IntelliSense
{
    /// <summary>What a rust-analyzer completion item is, as the completion list shows it (icon and filter).</summary>
    public enum RustCompletionCategory
    {
        Other,
        Local,
        Field,
        Method,
        Function,
        Struct,
        Enum,
        EnumMember,
        Trait,
        Module,
        Keyword,
        Snippet,
        Macro,
        Constant,
        TypeParameter,
        TypeAlias,
    }

    /// <summary>A filter button under the completion list (the C# list's "chips").</summary>
    public sealed class RustCompletionFilterInfo
    {
        public RustCompletionFilterInfo(string key, string english, string french, string accessKey, string monikerName)
        {
            Key = key;
            English = english;
            French = french;
            AccessKey = accessKey;
            MonikerName = monikerName;
        }

        public string Key { get; }

        public string English { get; }

        public string French { get; }

        public string AccessKey { get; }

        public string MonikerName { get; }

        public string Text(bool french) => french ? French : English;
    }

    /// <summary>
    /// How Kubuno shows rust-analyzer's completion items the way Visual Studio shows C#'s: the same icons as the
    /// QuickInfo tooltips and Solution Explorer (<see cref="SymbolMonikerNames"/>), the same filter buttons, the
    /// IntelliCode-like starred items at the top, and a C#-style description tooltip (icon + colorized signature +
    /// documentation).
    /// </summary>
    public static class RustCompletionPresentation
    {
        /// <summary>The characters that commit the selected item and are then typed, like C#'s.</summary>
        public const string CommitCharacters = "(.;,[<)]>{";

        /// <summary>The prefix IntelliCode puts before a recommended item.</summary>
        public const string StarPrefix = "★ ";

        /// <summary>The filter buttons, in the order C# shows its own.</summary>
        public static readonly IReadOnlyList<RustCompletionFilterInfo> Filters = new[]
        {
            new RustCompletionFilterInfo("module", "Modules", "Modules", "n", "ModulePublic"),
            new RustCompletionFilterInfo("struct", "Structures", "Structures", "s", "StructurePublic"),
            new RustCompletionFilterInfo("enum", "Enums", "Énumérations", "e", "EnumerationPublic"),
            new RustCompletionFilterInfo("trait", "Traits", "Traits", "i", "InterfacePublic"),
            new RustCompletionFilterInfo("method", "Methods and functions", "Méthodes et fonctions", "m", "MethodPublic"),
            new RustCompletionFilterInfo("field", "Fields", "Champs", "f", "FieldPublic"),
            new RustCompletionFilterInfo("local", "Locals and parameters", "Variables locales et paramètres", "l", "LocalVariable"),
            new RustCompletionFilterInfo("constant", "Constants", "Constantes", "o", "ConstantPublic"),
            new RustCompletionFilterInfo("macro", "Macros", "Macros", "r", "MacroPublic"),
            new RustCompletionFilterInfo("keyword", "Keywords", "Mots clés", "k", "IntellisenseKeyword"),
            new RustCompletionFilterInfo("snippet", "Snippets", "Extraits", "t", "Snippet"),
        };

        /// <summary>The category of an item from its LSP <c>kind</c>, label and detail (rust-analyzer's own mapping, reversed).</summary>
        public static RustCompletionCategory Categorize(int? kind, string label, string? detail)
        {
            label ??= string.Empty;
            if (label.EndsWith("!", StringComparison.Ordinal) || label.Contains("!(") || label.Contains("![") || label.Contains("!{")
                || (detail?.StartsWith("macro_rules!", StringComparison.Ordinal) ?? false))
            {
                return RustCompletionCategory.Macro;
            }

            switch (kind)
            {
                case 2: return RustCompletionCategory.Method;
                case 3:
                case 4:
                    return RustCompletionCategory.Function;
                case 5:
                case 10:
                    return RustCompletionCategory.Field;
                case 6:
                case 12:
                case 18:
                    return RustCompletionCategory.Local;
                case 7:
                case 22:
                    return RustCompletionCategory.Struct;
                case 8: return RustCompletionCategory.Trait;
                case 9: return RustCompletionCategory.Module;
                case 13: return RustCompletionCategory.Enum;
                case 14: return RustCompletionCategory.Keyword;
                case 15: return RustCompletionCategory.Snippet;
                case 20: return RustCompletionCategory.EnumMember;
                case 21: return RustCompletionCategory.Constant;
                case 25: return RustCompletionCategory.TypeParameter;
                case 26: return RustCompletionCategory.TypeAlias;
                default: return RustCompletionCategory.Other;
            }
        }

        /// <summary>The <c>KnownMonikers</c> name of the item's icon (the public variant: completion does not say the visibility).</summary>
        public static string IconMonikerName(RustCompletionCategory category)
        {
            switch (category)
            {
                case RustCompletionCategory.Local: return "LocalVariable";
                case RustCompletionCategory.Keyword: return "IntellisenseKeyword";
                case RustCompletionCategory.Snippet: return "Snippet";
                case RustCompletionCategory.TypeParameter: return "Type";
                case RustCompletionCategory.Other: return "Type";
                default: return SymbolMonikerNames.ForRust(ToSymbolKind(category), SymbolVisibility.Public);
            }
        }

        /// <summary>The key of the filter button (<see cref="Filters"/>) an item belongs to, or null.</summary>
        public static string? FilterKey(RustCompletionCategory category)
        {
            switch (category)
            {
                case RustCompletionCategory.Module: return "module";
                case RustCompletionCategory.Struct:
                case RustCompletionCategory.TypeAlias:
                case RustCompletionCategory.TypeParameter:
                    return "struct";
                case RustCompletionCategory.Enum:
                case RustCompletionCategory.EnumMember:
                    return "enum";
                case RustCompletionCategory.Trait: return "trait";
                case RustCompletionCategory.Method:
                case RustCompletionCategory.Function:
                    return "method";
                case RustCompletionCategory.Field: return "field";
                case RustCompletionCategory.Local: return "local";
                case RustCompletionCategory.Constant: return "constant";
                case RustCompletionCategory.Macro: return "macro";
                case RustCompletionCategory.Keyword: return "keyword";
                case RustCompletionCategory.Snippet: return "snippet";
                default: return null;
            }
        }

        /// <summary>The text shown in the list: rust-analyzer's label without the <c>(…)</c> call hint (C# lists method names bare).</summary>
        public static string DisplayText(string label, RustCompletionCategory category)
        {
            label ??= string.Empty;
            if (label.EndsWith("<\u2026>", StringComparison.Ordinal))
            {
                // Generic types read `Dictionary<>` in a C# list.
                label = label.Substring(0, label.Length - 3) + "<>";
            }

            if (category is RustCompletionCategory.Function or RustCompletionCategory.Method or RustCompletionCategory.EnumMember)
            {
                foreach (var suffix in new[] { "(…)", "()", "{…}", "{}" })
                {
                    if (label.EndsWith(suffix, StringComparison.Ordinal) && label.Length > suffix.Length)
                    {
                        return label.Substring(0, label.Length - suffix.Length);
                    }
                }
            }

            return label;
        }

        /// <summary>
        /// What a punctuation character commits (<c>(</c>, <c>&lt;</c>, <c>.</c>...): the bare name - <c>len</c>,
        /// <c>Vec</c>, <c>println!</c> - so the character typed next is not doubled by the item's own brackets.
        /// </summary>
        public static string BareName(string label)
        {
            label ??= string.Empty;
            foreach (var suffix in new[] { "(…)", "()", "{…}", "{}", "<…>", "<>", "[…]", "[]" })
            {
                if (label.EndsWith(suffix, StringComparison.Ordinal) && label.Length > suffix.Length)
                {
                    return label.Substring(0, label.Length - suffix.Length).TrimEnd();
                }
            }

            return label;
        }

        /// <summary>
        /// The key the completion list is sorted by (ordinal, ascending): rust-analyzer's own relevance tier first (its
        /// <c>sortText</c> is the inverted score in fixed-width hexadecimal, so the best tier sorts first), then - inside a
        /// tier only - associated functions (<c>new</c>, <c>with_capacity</c>...) before methods, then alphabetical. The
        /// order never crosses a relevance tier, except that constructors after a <c>Type::</c> path may be lifted to
        /// <paramref name="promotedTier"/> (see <see cref="NextTier"/>).
        /// </summary>
        public static string SortKey(string? sortText, RustCompletionCategory category, string displayText, string? promotedTier = null)
        {
            var tier = "ffffffff";
            if (sortText is { Length: >= 8 } && uint.TryParse(sortText.Substring(0, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
            {
                tier = sortText.Substring(0, 8).ToLowerInvariant();
            }

            char group = category == RustCompletionCategory.Method ? '2' : '0';
            if (promotedTier != null && string.CompareOrdinal(promotedTier, tier) < 0)
            {
                tier = promotedTier;
                group = '1'; // behind the functions that are natively in that tier, ahead of its methods
            }

            return tier + group + displayText.ToLowerInvariant();
        }

        /// <summary>
        /// The relevance tier right after <paramref name="bestSortText"/>'s (the list's best): where constructors are lifted to
        /// after a <c>Type::</c> path (see <see cref="IsConstructorLike"/>), so they lead the rest of the list without ever
        /// passing the top-relevance items. Null when the sort text is not rust-analyzer's.
        /// </summary>
        public static string? NextTier(string? bestSortText) =>
            bestSortText is { Length: >= 8 } && uint.TryParse(bestSortText.Substring(0, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value) && value < uint.MaxValue
                ? (value + 1).ToString("x8", CultureInfo.InvariantCulture)
                : null;

        /// <summary>
        /// An associated function (no <c>self</c>) that builds a value of the type being completed - <c>new</c>,
        /// <c>with_capacity</c>, <c>from</c>, <c>default</c>: its return type is <c>Self</c> or <paramref name="typeName"/>
        /// (possibly inside <c>Result</c>/<c>Option</c>).
        /// </summary>
        public static bool IsConstructorLike(RustCompletionCategory category, string? detail, string? typeName)
        {
            if (category != RustCompletionCategory.Function || string.IsNullOrEmpty(detail))
            {
                return false;
            }

            int arrow = detail!.LastIndexOf("->", StringComparison.Ordinal);
            if (arrow < 0)
            {
                return false;
            }

            var returned = detail.Substring(arrow + 2);
            return Regex.IsMatch(returned, @"\bSelf\b")
                || (!string.IsNullOrEmpty(typeName) && Regex.IsMatch(returned, @"\b" + Regex.Escape(typeName!) + @"\b"));
        }

        /// <summary>
        /// The indexes of the items to star (IntelliCode-like): only rust-analyzer's top-relevance items - the ones it
        /// marks <c>preselect</c> (clearly relevant, and at the best score of the list) - at most <paramref name="max"/>,
        /// in list order. Nothing is starred when rust-analyzer preselects nothing.
        /// </summary>
        public static IReadOnlyList<int> StarredIndexes(IReadOnlyList<string?> sortTexts, IReadOnlyList<bool> preselected, int max = 3)
        {
            string? best = null;
            for (int i = 0; i < sortTexts.Count; i++)
            {
                var tier = sortTexts[i] is { Length: >= 8 } text ? text.Substring(0, 8).ToLowerInvariant() : null;
                if (tier != null && (best == null || string.CompareOrdinal(tier, best) < 0))
                {
                    best = tier;
                }
            }

            if (best == null)
            {
                return Array.Empty<int>();
            }

            return Enumerable.Range(0, sortTexts.Count)
                .Where(i => preselected[i] && sortTexts[i] is { Length: >= 8 } text && string.Equals(text.Substring(0, 8), best, StringComparison.OrdinalIgnoreCase))
                .OrderBy(i => sortTexts[i]!.Substring(0, 8), StringComparer.OrdinalIgnoreCase)
                .ThenBy(i => i)
                .Take(max)
                .ToList();
        }

        /// <summary>The one-line declaration shown in the description (<c>fn len(&amp;self) -&gt; usize</c>, <c>let x: i32</c>...).</summary>
        public static string Signature(RustCompletionCategory category, string displayText, string? detail)
        {
            var name = displayText;
            detail = detail?.Trim();
            bool hasDetail = !string.IsNullOrEmpty(detail) && detail != name;
            switch (category)
            {
                case RustCompletionCategory.Function:
                case RustCompletionCategory.Method:
                    if (hasDetail && detail!.StartsWith("fn(", StringComparison.Ordinal))
                    {
                        return "fn " + name + detail.Substring(2);
                    }

                    if (hasDetail && detail!.Contains("fn("))
                    {
                        // "pub const unsafe fn(...)" and the like.
                        int at = detail.IndexOf("fn(", StringComparison.Ordinal);
                        return detail.Substring(0, at) + "fn " + name + detail.Substring(at + 2);
                    }

                    return hasDetail ? detail! : "fn " + name;
                case RustCompletionCategory.Local:
                    return hasDetail ? "let " + name + ": " + detail : "let " + name;
                case RustCompletionCategory.Field:
                    return hasDetail ? name + ": " + detail : name;
                case RustCompletionCategory.Constant:
                    return hasDetail ? "const " + name + ": " + detail : "const " + name;
                case RustCompletionCategory.Struct: return "struct " + name;
                case RustCompletionCategory.Enum: return "enum " + name;
                case RustCompletionCategory.Trait: return "trait " + name;
                case RustCompletionCategory.Module: return "mod " + name;
                case RustCompletionCategory.TypeAlias: return hasDetail ? "type " + name + " = " + detail : "type " + name;
                case RustCompletionCategory.Macro: return "macro_rules! " + name.TrimEnd('!');
                case RustCompletionCategory.EnumMember: return hasDetail ? detail! : name;
                default: return hasDetail ? detail! : name;
            }
        }

        /// <summary>
        /// The description beside the list, laid out like C#'s: icon + colorized declaration (+ the path of an item that
        /// will be imported, in grey), then the documentation.
        /// </summary>
        public static QuickInfoElement Description(RustCompletionCategory category, string displayText, string? detail, string? documentationMarkdown, string? importPath, bool french)
        {
            var rows = new List<QuickInfoElement>();
            if (category == RustCompletionCategory.Keyword)
            {
                rows.Add(QuickInfoLayout.Header(
                    QuickInfoImage.Moniker(IconMonikerName(category)),
                    new[] { new QuickInfoRun(QuickInfoTextKind.Keyword, displayText), new QuickInfoRun(QuickInfoTextKind.Muted, french ? " (mot clé)" : " (keyword)") },
                    null));
            }
            else if (category == RustCompletionCategory.Snippet)
            {
                rows.Add(QuickInfoLayout.Header(
                    QuickInfoImage.Moniker(IconMonikerName(category)),
                    new[] { new QuickInfoRun(QuickInfoTextKind.Text, displayText), new QuickInfoRun(QuickInfoTextKind.Muted, french ? " (extrait)" : " (snippet)") },
                    string.IsNullOrWhiteSpace(detail) ? null : detail));
            }
            else
            {
                var container = string.IsNullOrWhiteSpace(importPath) ? null : (french ? "Importe " : "Imports ") + importPath;
                rows.Add(QuickInfoLayout.Header(
                    QuickInfoImage.Moniker(IconMonikerName(category)),
                    RustSyntax.ClassifySignature(Signature(category, displayText, detail), ToItemKind(category)),
                    container));
            }

            var documentation = DocumentationRenderer.Render(documentationMarkdown);
            if (documentation != null)
            {
                rows.Add(documentation);
            }

            return new QuickInfoContainer(QuickInfoContainerStyle.Stacked | QuickInfoContainerStyle.VerticalPadding, rows);
        }

        public static RustItemKind ToItemKind(RustCompletionCategory category)
        {
            switch (category)
            {
                case RustCompletionCategory.Local: return RustItemKind.Local;
                case RustCompletionCategory.Field: return RustItemKind.Field;
                case RustCompletionCategory.Method: return RustItemKind.Method;
                case RustCompletionCategory.Function: return RustItemKind.Function;
                case RustCompletionCategory.Struct: return RustItemKind.Struct;
                case RustCompletionCategory.Enum: return RustItemKind.Enum;
                case RustCompletionCategory.EnumMember: return RustItemKind.Variant;
                case RustCompletionCategory.Trait: return RustItemKind.Trait;
                case RustCompletionCategory.Module: return RustItemKind.Module;
                case RustCompletionCategory.Keyword: return RustItemKind.Keyword;
                case RustCompletionCategory.Macro: return RustItemKind.Macro;
                case RustCompletionCategory.Constant: return RustItemKind.Const;
                case RustCompletionCategory.TypeParameter: return RustItemKind.TypeParameter;
                case RustCompletionCategory.TypeAlias: return RustItemKind.TypeAlias;
                default: return RustItemKind.Other;
            }
        }

        private static SolutionSymbolKind ToSymbolKind(RustCompletionCategory category)
        {
            switch (category)
            {
                case RustCompletionCategory.Field: return SolutionSymbolKind.Field;
                case RustCompletionCategory.Method: return SolutionSymbolKind.Method;
                case RustCompletionCategory.Function: return SolutionSymbolKind.Function;
                case RustCompletionCategory.Struct: return SolutionSymbolKind.Struct;
                case RustCompletionCategory.Enum: return SolutionSymbolKind.Enum;
                case RustCompletionCategory.EnumMember: return SolutionSymbolKind.Variant;
                case RustCompletionCategory.Trait: return SolutionSymbolKind.Trait;
                case RustCompletionCategory.Module: return SolutionSymbolKind.Module;
                case RustCompletionCategory.Macro: return SolutionSymbolKind.Macro;
                case RustCompletionCategory.Constant: return SolutionSymbolKind.Const;
                case RustCompletionCategory.TypeAlias: return SolutionSymbolKind.TypeAlias;
                default: return SolutionSymbolKind.Other;
            }
        }
    }
}
