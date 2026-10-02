using System;
using System.Collections.Generic;
using Kubuno.Views.Designer.Editing;

namespace Kubuno.Views.Designer.Outline
{
    /// <summary>
    /// One node of a <c>textDocument/documentSymbol</c> response (docs/DESIGNER.md §1's Document
    /// Outline), mirroring <c>kubuno-views-ls/src/symbols.rs</c>'s own <c>DocumentSymbol</c> shape
    /// field-for-field: <see cref="Name"/> is <c>x:Name</c> when the element has one, else its tag name
    /// (<see cref="Detail"/> then carries the tag name, <see langword="null"/> otherwise - "when named,
    /// show the tag as the detail", that module's own comment). This type carries no stable id of its
    /// own - <see cref="DocumentSymbolTreeBuilder"/> computes one from each node's ordinal POSITION in
    /// the response tree (the same order <c>symbols.rs</c> walks <c>Element::children()</c> in), not
    /// from anything this DTO stores.
    /// </summary>
    public sealed class DocumentSymbolDto
    {
        public DocumentSymbolDto(string name, string? detail, LspRange range, IReadOnlyList<DocumentSymbolDto> children)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Detail = detail;
            Range = range;
            Children = children ?? throw new ArgumentNullException(nameof(children));
        }

        public string Name { get; }

        public string? Detail { get; }

        public LspRange Range { get; }

        public IReadOnlyList<DocumentSymbolDto> Children { get; }
    }
}
