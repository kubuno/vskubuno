using System;
using Microsoft.VisualStudio.ProjectSystem;

namespace Kubuno.VisualStudio.RustProjectSystem
{
    /// <summary>
    /// Image monikers declared by <c>Resources\RustProject.imagemanifest</c> (shipped in the VSIX,
    /// discovered by Visual Studio's image service from the extension folder).
    /// </summary>
    public static class RustProjectImages
    {
        /// <summary>The <c>RustProjectImagesGuid</c> symbol of the image manifest.</summary>
        public static readonly Guid ImagesGuid = new Guid("7d2b8c4e-5a1f-4e63-9b0d-3c6f2a8e1b57");

        /// <summary>The <c>RustProject</c> image id of the image manifest.</summary>
        public const int RustProjectId = 1;

        /// <summary>The project node icon of a <c>.rsproj</c>.</summary>
        public static readonly ProjectImageMoniker RustProject = new ProjectImageMoniker(ImagesGuid, RustProjectId);
    }
}
