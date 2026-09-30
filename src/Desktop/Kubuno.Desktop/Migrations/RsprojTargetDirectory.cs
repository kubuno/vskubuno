using System;
using System.IO;
using Kubuno.Rust.Commands;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace Kubuno.Desktop.Migrations
{
    /// <summary>
    /// The cargo target directory a crate's <c>.rsproj</c> builds into (its evaluated <c>CargoTargetDir</c>), so
    /// <c>sqlx.prepare</c> (docs/DATA.md DATA-7) builds where the project builds rather than into the
    /// <c>CARGO_TARGET_DIR</c> Visual Studio inherited: a Kubuno application links the <c>kubuno_ui</c> dylib and must
    /// never share the desktop workspace's target directory. <see langword="null"/> for an Open Folder crate or a
    /// project without the property (the helper then keeps cargo's own choice).
    /// </summary>
    internal static class RsprojTargetDirectory
    {
        public static string? Find(string crateDirectory)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (ServiceProvider.GlobalProvider.GetService(typeof(SVsSolution)) is not IVsSolution solution)
            {
                return null;
            }

            var none = Guid.Empty;
            if (solution.GetProjectEnum((uint)__VSENUMPROJFLAGS.EPF_LOADEDINSOLUTION, ref none, out var projects) != VSConstants.S_OK || projects is null)
            {
                return null;
            }

            string wanted = Normalize(crateDirectory);
            var one = new IVsHierarchy[1];
            while (projects.Next(1, one, out uint fetched) == VSConstants.S_OK && fetched == 1)
            {
                var hierarchy = one[0];
                if (hierarchy.GetCanonicalName((uint)VSConstants.VSITEMID.Root, out var path) != VSConstants.S_OK
                    || string.IsNullOrEmpty(path)
                    || !path.EndsWith(".rsproj", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string? directory = Path.GetDirectoryName(path);
                if (directory is not null && string.Equals(Normalize(directory), wanted, StringComparison.OrdinalIgnoreCase))
                {
                    return RsprojSelection.GetBuildProperty(hierarchy, "CargoTargetDir");
                }
            }

            return null;
        }

        private static string Normalize(string directory) => Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
