using System;
using System.Collections.Generic;

namespace Kubuno.Desktop.Designer.Toolbox
{
    /// <summary>One toolbox group (docs/DESIGNER.md §1: "grouped by family - the module grouping already used in the crate ... plus the five components declared directly in registry/components.rs as a 'Core' group").</summary>
    public sealed class ToolboxFamilyViewModel
    {
        public ToolboxFamilyViewModel(string family, IReadOnlyList<ToolboxItemViewModel> items)
        {
            Family = family ?? throw new ArgumentNullException(nameof(family));
            Items = items ?? throw new ArgumentNullException(nameof(items));
        }

        public string Family { get; }

        public IReadOnlyList<ToolboxItemViewModel> Items { get; }
    }
}
