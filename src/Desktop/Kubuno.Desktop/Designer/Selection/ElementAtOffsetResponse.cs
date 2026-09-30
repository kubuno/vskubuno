using System;
using Kubuno.VisualStudio.Designer.Editing;

namespace Kubuno.VisualStudio.Designer.Selection
{
    /// <summary>A <c>kubuno/elementAtOffset</c> result (docs/DESIGNER.md §8): the innermost element containing the requested position, and its own full range.</summary>
    public sealed class ElementAtOffsetResponse
    {
        public ElementAtOffsetResponse(string elementId, LspRange range)
        {
            ElementId = elementId ?? throw new ArgumentNullException(nameof(elementId));
            Range = range;
        }

        public string ElementId { get; }

        public LspRange Range { get; }
    }
}
