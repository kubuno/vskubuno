using System.Collections.Generic;
using Kubuno.Views.Designer.Selection;

namespace Kubuno.Views.Tests.Designer.Selection.Fakes
{
    internal sealed class FakeOutlineSelectionTarget : IOutlineSelectionTarget
    {
        public List<string?> Selections { get; } = new List<string?>();

        public string? LastSelection => Selections.Count > 0 ? Selections[Selections.Count - 1] : null;

        public void Select(string? elementId) => Selections.Add(elementId);
    }
}
