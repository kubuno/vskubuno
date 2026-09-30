using System;
using System.ComponentModel.Composition;
using Kubuno.Rust.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem;

namespace Kubuno.Desktop.ProjectSystem
{
    /// <summary>
    /// Gives the <c>.kbview</c> items of a <c>.rsproj</c> their "form-like window" icon (docs/RSPROJ.md lot 8), next to
    /// <see cref="RustProjectTreePropertiesProvider"/>, which keeps the Rust ones (project node, <c>.rs</c> files,
    /// <c>Cargo.toml</c>). The image itself stays in <c>RustProject.imagemanifest</c>
    /// (<see cref="RustProjectImages.KbviewFile"/>) so its moniker - also used by <c>kbview-languages.pkgdef</c> for Open
    /// Folder - is unchanged.
    /// </summary>
    [Export(typeof(IProjectTreePropertiesProvider))]
    [AppliesTo(RustProjectCapabilities.RustProjectSystem)]
    [Order(1000)]
    internal sealed class KbviewTreePropertiesProvider : IProjectTreePropertiesProvider
    {
        public void CalculatePropertyValues(IProjectTreeCustomizablePropertyContext propertyContext, IProjectTreeCustomizablePropertyValues propertyValues)
        {
            if (propertyContext.IsFolder || propertyValues.Flags.Contains(ProjectTreeFlags.ProjectRoot))
            {
                return;
            }

            if (string.Equals(System.IO.Path.GetExtension(propertyContext.ItemName), ".kbview", StringComparison.OrdinalIgnoreCase))
            {
                propertyValues.Icon = RustProjectImages.KbviewFile;
                propertyValues.ExpandedIcon = RustProjectImages.KbviewFile;
            }
        }
    }
}
