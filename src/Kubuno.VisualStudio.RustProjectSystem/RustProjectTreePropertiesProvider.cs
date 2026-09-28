using System.ComponentModel.Composition;
using Microsoft.VisualStudio.ProjectSystem;

namespace Kubuno.VisualStudio.RustProjectSystem
{
    /// <summary>
    /// Gives the <c>.rsproj</c> project node in Solution Explorer its Rust icon. CPS asks every
    /// exported <see cref="IProjectTreePropertiesProvider"/> whose <see cref="AppliesToAttribute"/>
    /// expression matches the project's capabilities to adjust each tree node's properties; only
    /// the project root is touched here (file icons keep coming from their extensions).
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
            }
        }
    }
}
