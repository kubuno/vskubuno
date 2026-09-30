using System;
using System.Collections.Generic;

namespace Kubuno.Rust.Logic.SolutionExplorer
{
    /// <summary>What a Solution Explorer symbol node stands for (docs/RSPROJ.md, lot 8).</summary>
    public enum SolutionSymbolKind
    {
        Other,
        Struct,
        Enum,
        Union,
        Trait,
        Impl,
        TraitImpl,
        Function,
        Method,
        Field,
        Variant,
        Const,
        Static,
        Module,
        TypeAlias,
        Macro,
        Region,
        ExternBlock,

        /// <summary>
        /// An element of a file in another language than Rust (a <c>.kbview</c> view element of the desktop layer): its
        /// tag decides its icon, which the layer providing the file's symbols supplies (Kubuno.Rust's
        /// ISolutionSymbolProvider).
        /// </summary>
        Element,
    }

    /// <summary>
    /// Rust visibility, mapped onto the four accessibility variants of the Visual Studio image
    /// catalog exactly the way Roslyn maps C# accessibility: <c>pub</c> = public (no overlay),
    /// <c>pub(crate)</c>/<c>pub(in path)</c> = internal/Friend, <c>pub(super)</c> = protected,
    /// no modifier/<c>pub(self)</c> = private.
    /// </summary>
    public enum SymbolVisibility
    {
        Public,
        Internal,
        Protected,
        Private,
    }

    /// <summary>
    /// One node of a file's symbol tree as shown under the file in Solution Explorer: a Rust item
    /// (from <c>rust-analyzer symbols</c>) or a <c>.kbview</c> element (from kubuno-views-ls's
    /// <c>textDocument/documentSymbol</c>). Pure data, no Visual Studio types.
    /// </summary>
    public sealed class SolutionSymbol
    {
        public SolutionSymbol(string name, SolutionSymbolKind kind, SymbolVisibility visibility, string? detail, int line, int column)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Kind = kind;
            Visibility = visibility;
            Detail = detail;
            Line = line;
            Column = column;
        }

        /// <summary>The symbol's own name (<c>x:Name</c>, or the tag, for a view element).</summary>
        public string Name { get; }

        public SolutionSymbolKind Kind { get; }

        public SymbolVisibility Visibility { get; }

        /// <summary>rust-analyzer's detail (a signature such as <c>fn(&amp;self) -&gt; u8</c>, a field type), or the tag of a named view element.</summary>
        public string? Detail { get; }

        /// <summary>Zero-based line of the symbol's name in the file.</summary>
        public int Line { get; }

        /// <summary>Zero-based UTF-16 column of the symbol's name in the file.</summary>
        public int Column { get; }

        public List<SolutionSymbol> Children { get; } = new List<SolutionSymbol>();

        /// <summary>The label Solution Explorer shows, in the spirit of Roslyn's <c>Dispose(bool)</c> / <c>components : IContainer</c>.</summary>
        public string DisplayText
        {
            get
            {
                switch (Kind)
                {
                    case SolutionSymbolKind.Function:
                    case SolutionSymbolKind.Method:
                        // rust-analyzer's detail is "fn(x: i32) -> Self": show "name(x: i32) -> Self".
                        return Detail != null && Detail.StartsWith("fn(", StringComparison.Ordinal)
                            ? Name + Detail.Substring(2)
                            : Name + "()";
                    case SolutionSymbolKind.Field:
                    case SolutionSymbolKind.Const:
                    case SolutionSymbolKind.Static:
                        return string.IsNullOrEmpty(Detail) ? Name : $"{Name}: {Detail}";
                    case SolutionSymbolKind.Element:
                        return string.IsNullOrEmpty(Detail) ? Name : $"{Name} ({Detail})";
                    default:
                        return Name;
                }
            }
        }

        /// <summary>The element tag of a view element (its detail when named, else its name).</summary>
        public string ElementTag => Kind == SolutionSymbolKind.Element && !string.IsNullOrEmpty(Detail) ? Detail! : Name;

        /// <summary>A key identifying "the same" symbol across two parses of a file (used to keep expansion state on refresh).</summary>
        public string MergeKey => Kind + "|" + Name + "|" + Detail;

        public override string ToString() => DisplayText;
    }
}
