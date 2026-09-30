using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Kubuno.Rust.Logic.IntelliSense
{
    /// <summary>What a rust-analyzer code lens counts.</summary>
    public enum CodeLensKind
    {
        References,
        Implementations,
    }

    /// <summary>One resolved count shown above an item ("3 references").</summary>
    public sealed class CodeLensCount
    {
        public CodeLensCount(CodeLensKind kind, int count)
        {
            Kind = kind;
            Count = count;
        }

        public CodeLensKind Kind { get; }

        public int Count { get; }
    }

    /// <summary>
    /// The text of Kubuno's Rust code lenses, worded like C#'s CodeLens ("3 references", "1 implementation" /
    /// "3 références", "1 implémentation"), from rust-analyzer's resolved lenses (whose titles are English only).
    /// </summary>
    public static class CodeLensText
    {
        private static readonly Regex LeadingNumber = new Regex(@"^\s*(\d+)", RegexOptions.CultureInvariant);

        /// <summary>
        /// Reads a resolved lens: its kind from the title (rust-analyzer writes "N reference(s)" / "N implementation(s)"),
        /// its count from the locations rust-analyzer passes to <c>rust-analyzer.showReferences</c> when present, else from the title.
        /// Null for anything else (Run/Debug lenses...).
        /// </summary>
        public static CodeLensCount? Parse(string? title, int? locationCount)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return null;
            }

            CodeLensKind kind;
            if (title!.IndexOf("implementation", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                kind = CodeLensKind.Implementations;
            }
            else if (title.IndexOf("reference", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                kind = CodeLensKind.References;
            }
            else
            {
                return null;
            }

            if (locationCount is int count)
            {
                return new CodeLensCount(kind, count);
            }

            var match = LeadingNumber.Match(title);
            return match.Success ? new CodeLensCount(kind, int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)) : null;
        }

        public static string Format(CodeLensCount count, bool french)
        {
            bool plural = french ? count.Count > 1 : count.Count != 1;
            switch (count.Kind)
            {
                case CodeLensKind.Implementations:
                    return count.Count + (french ? (plural ? " implémentations" : " implémentation") : (plural ? " implementations" : " implementation"));
                default:
                    return count.Count + (french ? (plural ? " références" : " référence") : (plural ? " references" : " reference"));
            }
        }

        /// <summary>The line's counts, references first (C#'s order).</summary>
        public static IReadOnlyList<CodeLensCount> Order(IEnumerable<CodeLensCount> counts) =>
            counts.OrderBy(c => c.Kind == CodeLensKind.References ? 0 : 1).ToList();
    }
}
