using System;
using Microsoft.VisualStudio.ProjectSystem;

namespace Kubuno.Rust.ProjectSystem
{
    /// <summary>
    /// Image monikers declared by <c>Resources\RustProject.imagemanifest</c> (shipped in the VSIX,
    /// discovered by Visual Studio's image service from the extension folder). The ids must match
    /// the manifest's <c>ID</c> symbols; languages.pkgdef's <c>ShellFileAssociations</c> keys repeat
    /// them for Open Folder.
    /// </summary>
    public static class RustProjectImages
    {
        /// <summary>The <c>RustProjectImagesGuid</c> symbol of the image manifest.</summary>
        public static readonly Guid ImagesGuid = new Guid("7d2b8c4e-5a1f-4e63-9b0d-3c6f2a8e1b57");

        /// <summary>The <c>RustProject</c> image id of the image manifest.</summary>
        public const int RustProjectId = 1;

        /// <summary>The <c>RustFile</c> image id (a source page with the Rust badge).</summary>
        public const int RustFileId = 2;

        /// <summary>
        /// The <c>KbviewFile</c> image id (a form-like window). The image stays in this manifest so the moniker never
        /// changes; the desktop layer applies it to <c>.kbview</c> items (Kubuno.Desktop.ProjectSystem).
        /// </summary>
        public const int KbviewFileId = 3;

        /// <summary>The <c>CargoManifest</c> image id (Cargo's crate).</summary>
        public const int CargoManifestId = 4;

        /// <summary>
        /// The <c>KbcontrolFile</c> image id: a user control view (<c>.kbcontrol</c>, docs/VIEWS-SPEC.md "File kinds") - a
        /// dashed control outline holding the toolbox UserControl layout, unlike the form-like window of <c>.kbview</c>.
        /// </summary>
        public const int KbcontrolFileId = 5;

        /// <summary>The project node icon of a <c>.rsproj</c>.</summary>
        public static readonly ProjectImageMoniker RustProject = new ProjectImageMoniker(ImagesGuid, RustProjectId);

        public static readonly ProjectImageMoniker RustFile = new ProjectImageMoniker(ImagesGuid, RustFileId);

        public static readonly ProjectImageMoniker KbviewFile = new ProjectImageMoniker(ImagesGuid, KbviewFileId);

        public static readonly ProjectImageMoniker CargoManifest = new ProjectImageMoniker(ImagesGuid, CargoManifestId);

        public static readonly ProjectImageMoniker KbcontrolFile = new ProjectImageMoniker(ImagesGuid, KbcontrolFileId);

        /// <summary>The file icon for <paramref name="fileName"/>, or <see langword="null"/> to keep Visual Studio's own.</summary>
        public static ProjectImageMoniker? ForFile(string? fileName)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                return null;
            }

            if (string.Equals(fileName, "Cargo.toml", StringComparison.OrdinalIgnoreCase))
            {
                return CargoManifest;
            }

            var extension = System.IO.Path.GetExtension(fileName);
            if (string.Equals(extension, ".rs", StringComparison.OrdinalIgnoreCase))
            {
                return RustFile;
            }

            // .kbview and .kbcontrol items get KbviewFile/KbcontrolFile from the desktop layer (Kubuno.Desktop.ProjectSystem.KbviewTreePropertiesProvider).
            return null;
        }
    }
}
