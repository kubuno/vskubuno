using System;
using System.IO;

namespace Kubuno.Desktop.Views.Locating
{
    /// <summary>
    /// Finds the Cargo workspace root to hand <c>kubuno-views-ls</c> as its project root: the
    /// directory containing the nearest <c>Cargo.toml</c> walking up from a starting file or folder,
    /// falling back to the starting folder itself when no <c>Cargo.toml</c> is found on the way up.
    /// <c>.kbview</c> files live inside the same Cargo crates as the Rust code-behind that consumes
    /// them (see <c>docs/XML_VIEWS.md</c>), so this is the same root a Rust file in the same crate
    /// would resolve to.
    ///
    /// Takes file-system access as delegates, exactly like the sibling VSIX project's
    /// <c>Kubuno.Rust.Logic.CargoWorkspaceLocator</c>, so the traversal logic can be
    /// unit-tested without touching disk. Deliberately duplicated here rather than referenced from
    /// that project: this library will itself be referenced BY the VSIX once integrated (see
    /// INTEGRATION.md), so a reference the other way would be circular, and the two are developed
    /// independently in parallel.
    /// </summary>
    public static class KbviewWorkspaceLocator
    {
        /// <param name="startPath">
        /// A file path (e.g. the <c>.kbview</c> document being opened) or a directory path (e.g. an
        /// already-known workspace root) to start searching from.
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
                if (fileExists(Path.Combine(directory!, KbviewConstants.CargoManifestFileName)))
                {
                    return directory;
                }
            }

            return startDirectory;
        }
    }
}
