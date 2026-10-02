using System;
using System.Collections.Generic;
using Kubuno.Desktop.Designer.Selection;

namespace Kubuno.Desktop.Designer.Outline
{
    /// <summary>
    /// Turns a <c>textDocument/documentSymbol</c> response (<see cref="DocumentSymbolDto"/>, carrying no
    /// id of its own) into a tree of <see cref="OutlineNode"/>s, each with the SAME stable element id
    /// (docs/DESIGNER.md §8) <c>kubuno-views-ls</c>'s own <c>edit_bridge</c>/<c>ast::Element::stable_id</c>
    /// would compute for it - "documentSymbol ... mapped to stable ids", docs/DESIGNER.md §6's DSG-8 row.
    ///
    /// No extra round trip is needed to get these ids: <c>symbols.rs</c>'s own <c>document_symbols</c>
    /// walks <c>Element::children()</c> in EXACTLY the same document order <c>stable_id</c> itself indexes
    /// by (checked directly against that module's source - <c>element_symbol</c> recurses via
    /// <c>el.children().map(...)</c>, the identical iterator <c>stable_id</c>'s own inverse,
    /// <c>Document::resolve_id</c>, walks), so each node's id is fully determined by its ordinal POSITION
    /// in the response tree - see <see cref="StableElementId.Child"/>, this method's own reuse of DSG-2's
    /// id-construction rule.
    ///
    /// <c>document_symbols</c> returns EXACTLY ONE top-level entry (the document root, whose own stable
    /// id is <see cref="StableElementId.Root"/> - the empty string, not <c>"0"</c>) when the document has
    /// a root element, or an empty list otherwise (that module's own doc: "None when the document has no
    /// root element at all"). This builder assumes that shape for the common case but degrades
    /// gracefully - never throwing - if a future/different top-level shape shows up (see
    /// <see cref="Build"/>'s own remarks).
    /// </summary>
    public static class DocumentSymbolTreeBuilder
    {
        /// <summary><see cref="Array.Empty{T}"/> for a <see langword="null"/>/empty response (docs/DESIGNER.md §8's own "degrade to nothing usable" rule, mirrored here one layer up for a tree instead of a single value).</summary>
        public static IReadOnlyList<OutlineNode> Build(IReadOnlyList<DocumentSymbolDto>? symbols)
        {
            if (symbols is null || symbols.Count == 0)
            {
                return Array.Empty<OutlineNode>();
            }

            // The expected, ordinary shape: one entry, the document root - see this class's own doc
            // comment. A response with more than one top-level entry (not something `symbols.rs` produces
            // today) still gets SOME id rather than throwing, numbering top-level entries like any other
            // level - best-effort, not a contract this builder enforces.
            if (symbols.Count == 1)
            {
                return new[] { BuildNode(symbols[0], StableElementId.Root) };
            }

            var result = new List<OutlineNode>(symbols.Count);
            for (var i = 0; i < symbols.Count; i++)
            {
                result.Add(BuildNode(symbols[i], i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }

            return result;
        }

        private static OutlineNode BuildNode(DocumentSymbolDto symbol, string elementId)
        {
            var children = new List<OutlineNode>(symbol.Children.Count);
            for (var i = 0; i < symbol.Children.Count; i++)
            {
                children.Add(BuildNode(symbol.Children[i], StableElementId.Child(elementId, i)));
            }

            return new OutlineNode(elementId, symbol.Name, symbol.Detail, symbol.Range, children);
        }
    }
}
