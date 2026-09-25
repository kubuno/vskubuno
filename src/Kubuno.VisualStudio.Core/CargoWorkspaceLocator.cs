using System;
using System.IO;

namespace Kubuno.VisualStudio.Core
{
    /// <summary>
    /// Finds the Cargo workspace root to hand rust-analyzer as its project root: the directory
    /// containing the nearest <c>Cargo.toml</c> walking up from a starting file or folder, falling
    /// back to the starting folder itself (typically the Open Folder workspace root) when no
    /// <c>Cargo.toml</c> is found on the way up. Takes file-system access as delegates so the
    /// traversal logic can be unit-tested without touching disk.
    /// </summary>
    public static class CargoWorkspaceLocator
    {
        private const string ManifestFileName = "Cargo.toml";

        /// <param name="startPath">
        /// A file path (e.g. the document being opened) or a directory path (e.g. an already-known
        /// workspace root) to start searching from.
        /// </param>
        /// <param name="isDirectory">
        /// Returns <see langword="true"/> when the given path is a directory. Needed because the
        /// search starts at <paramref name="startPath"/> itself when it is a directory, or at its
        /// parent when it is a file.
        /// </param>
        /// <param name="fileExists">Returns <see langword="true"/> when a file exists at the given full path.</param>
        /// <returns>
        /// The directory containing the nearest ancestor <c>Cargo.toml</c>; if none is found, the
        /// starting directory (i.e. <paramref name="startPath"/> itself if it is a directory,
        /// otherwise its parent). <see langword="null"/> only when <paramref name="startPath"/> is
        /// null/empty or has no resolvable parent (e.g. a bare file name).
        /// </returns>
        public static string? FindWorkspaceRoot(string? startPath, Func<string, bool> isDirectory, Func<string, bool> fileExists)
        {
            if (isDirectory is null)
            {
                throw new ArgumentNullException(nameof(isDirectory));
            }

            if (fileExists is null)
            {
                throw new ArgumentNullException(nameof(fileExists));
            }

            if (string.IsNullOrWhiteSpace(startPath))
            {
                return null;
            }

            var normalizedStart = startPath!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var startDirectory = isDirectory(normalizedStart) ? normalizedStart : Path.GetDirectoryName(normalizedStart);
            if (string.IsNullOrEmpty(startDirectory))
            {
                return null;
            }

            for (var directory = startDirectory; !string.IsNullOrEmpty(directory); directory = Path.GetDirectoryName(directory))
            {
                if (fileExists(Path.Combine(directory!, ManifestFileName)))
                {
                    return directory;
                }
            }

            return startDirectory;
        }
    }
}
