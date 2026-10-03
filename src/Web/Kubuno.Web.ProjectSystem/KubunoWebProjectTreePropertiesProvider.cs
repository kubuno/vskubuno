using System;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.ProjectSystem;

namespace Kubuno.Web.ProjectSystem
{
    /// <summary>
    /// Tells the Kubuno Core projects apart in Solution Explorer (docs/WEB.md, "Solutions"): the server project of the
    /// core (<c>KubunoWebRole=Core</c>, capability <c>KubunoWebCore</c>) shows a web server icon, a module's backend
    /// (<c>KubunoWebModule</c>) a web application icon, instead of the Rust "R" every <c>.rsproj</c> gets - the same
    /// project type GUID, only the capability added by Kubuno.Web.Sdk differs. Desktop apps and libraries keep theirs.
    /// Ordered after the Rust layer's provider so it has the last word on the project node.
    /// </summary>
    [Export(typeof(IProjectTreePropertiesProvider))]
    [AppliesTo("RustProjectSystem & (KubunoWebCore | KubunoWebModule)")]
    [Order(1100)]
    internal sealed class KubunoWebProjectTreePropertiesProvider : IProjectTreePropertiesProvider
    {
        /// <summary>KnownImageIds.ImageCatalogGuid.</summary>
        private static readonly Guid ImageCatalog = new Guid("AE27A6B0-E345-4288-96DF-5EAF394EE369");

        /// <summary>KnownImageIds.WebServer and KnownImageIds.WebApplication.</summary>
        private const int WebServer = 3503;
        private const int WebApplication = 3483;

        [ImportingConstructor]
        public KubunoWebProjectTreePropertiesProvider(UnconfiguredProject project)
        {
            Project = project;
        }

        private UnconfiguredProject Project { get; }

        public void CalculatePropertyValues(IProjectTreeCustomizablePropertyContext propertyContext, IProjectTreeCustomizablePropertyValues propertyValues)
        {
            if (!propertyValues.Flags.Contains(ProjectTreeFlags.ProjectRoot))
            {
                return;
            }

            var isCore = Project.Capabilities.AppliesTo("KubunoWebCore");
            var icon = new ProjectImageMoniker(ImageCatalog, isCore ? WebServer : WebApplication);
            propertyValues.Icon = icon;
            propertyValues.ExpandedIcon = icon;
        }
    }
}
