using System.Collections.Generic;
using Kubuno.VisualStudio.Designer.Selection;

namespace Kubuno.VisualStudio.Designer.Tests.Selection.Fakes
{
    internal sealed class FakeDesignSurfaceSelectionTarget : IDesignSurfaceSelectionTarget
    {
        public List<string?> Selections { get; } = new List<string?>();

        public string? LastSelection => Selections.Count > 0 ? Selections[Selections.Count - 1] : null;

        public void Select(string? elementId) => Selections.Add(elementId);
    }
}
