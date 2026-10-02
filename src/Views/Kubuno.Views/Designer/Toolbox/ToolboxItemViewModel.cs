using System;
using Kubuno.Views.Designer.Registry;

namespace Kubuno.Views.Designer.Toolbox
{
    /// <summary>One toolbox entry - a thin, display-oriented wrapper around a single <see cref="ComponentMeta"/> (docs/DESIGNER.md §1: "One entry per ComponentMeta from the registry").</summary>
    public sealed class ToolboxItemViewModel
    {
        public ToolboxItemViewModel(ComponentMeta component)
        {
            Component = component ?? throw new ArgumentNullException(nameof(component));
        }

        public ComponentMeta Component { get; }

        public string DisplayName => Component.Name;

        public string? Icon => Component.Icon;

        public string? Doc => Component.LocalizedDoc;
    }
}
