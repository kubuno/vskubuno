using System.ComponentModel.Composition;
using Microsoft.VisualStudio.ProjectSystem;

namespace Kubuno.VisualStudio.RustProjectSystem
{
    /// <summary>
    /// Gives <c>.rsproj</c> nodes in Solution Explorer their icons: the orange "R" on the project
    /// node, and (docs/RSPROJ.md lot 8) the Rust source, Kubuno view and Cargo manifest file icons -
    /// the counterparts of the C# file and Windows Forms form icons. CPS asks every exported
    /// <see cref="IProjectTreePropertiesProvider"/> whose <see cref="AppliesToAttribute"/> expression
    /// matches the project's capabilities to adjust each tree node's properties.
    /// </summary>
    [Export(typeof(IProjectTreePropertiesProvider))]
    [AppliesTo(RustProjectCapabilities.RustProjectSystem)]
    [Order(1000)]
    internal sealed class RustProjectTreePropertiesProvider : IProjectTreePropertiesProvider
    {
        public void CalculatePropertyValues(IProjectTreeCustomizablePropertyContext propertyContext, IProjectTreeCustomizablePropertyValues propertyValues)
        {
            if (propertyValues.Flags.Contains(ProjectTreeFlags.ProjectRoot))
            {
                propertyValues.Icon = RustProjectImages.RustProject;
                propertyValues.ExpandedIcon = RustProjectImages.RustProject;
                return;
            }

            if (propertyContext.IsFolder)
            {
                return;
            }

            var icon = RustProjectImages.ForFile(propertyContext.ItemName);
            if (icon != null)
            {
                propertyValues.Icon = icon;
                propertyValues.ExpandedIcon = icon;
            }
        }
    }
}
