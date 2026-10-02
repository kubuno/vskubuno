using System;
using System.Collections.Generic;
using Kubuno.Views.Designer.Editing;

namespace Kubuno.Views.Designer.Outline
{
    /// <summary>One node of the Document Outline's tree, after <see cref="DocumentSymbolTreeBuilder"/> has assigned it a stable element id - the pure data <see cref="OutlineNodeViewModel"/> wraps for WPF binding.</summary>
    public sealed class OutlineNode
    {
        public OutlineNode(string elementId, string name, string? detail, LspRange range, IReadOnlyList<OutlineNode> children)
        {
            ElementId = elementId ?? throw new ArgumentNullException(nameof(elementId));
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Detail = detail;
            Range = range;
            Children = children ?? throw new ArgumentNullException(nameof(children));
        }

        /// <summary>The stable id (docs/DESIGNER.md §8) - what a click sends to <c>Selection.SelectionSyncService.SelectFromOutlineAsync</c>.</summary>
        public string ElementId { get; }

        /// <summary><c>x:Name</c> when the element has one, its tag name otherwise (mirrors <c>symbols.rs</c>'s own <c>DocumentSymbol.name</c> - see <see cref="DocumentSymbolDto"/>'s own doc).</summary>
        public string Name { get; }

        /// <summary>The tag name when <see cref="Name"/> is an <c>x:Name</c>, <see langword="null"/> otherwise.</summary>
        public string? Detail { get; }

        public LspRange Range { get; }

        public IReadOnlyList<OutlineNode> Children { get; }
    }
}
