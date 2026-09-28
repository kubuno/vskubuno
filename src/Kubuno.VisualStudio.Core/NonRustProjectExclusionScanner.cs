using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Kubuno.VisualStudio.Core
{
    /// <summary>
    /// Computes the directories a Cargo Open Folder workspace should list in
    /// <c>VSWorkspaceSettings.json</c>'s <c>ExcludedItems</c>: any directory, anywhere under the
    /// workspace root, that VS's OWN native project-file discovery (.csproj/.vbproj/.fsproj/.vcxproj/
    /// .sln/.slnx - entirely independent of Cargo/rust-analyzer) would pick up and offer in the
    /// "Select Startup Item" dropdown. Confirmed live: a <c>tools/winforms-ref/</c> tree of
    /// throwaway WinForms parity-reference projects made that dropdown list only those five
    /// .csproj files and "Active document" - every real Cargo bin/example target
    /// <c>RustLaunchTargetsGenerator</c>'s own <c>launch.vs.json</c> offers was hidden entirely.
    /// Excluding their containing directories fixes that without deleting or moving anything a
    /// developer put there for its own sake.
    ///
    /// Pure/VS-SDK-free (same shape as <see cref="CargoWorkspaceLocator"/>/<see cref="StartupItemSelector"/>)
    /// so it is unit-tested with plain <c>dotnet test</c>; the caller (<c>KubunoPackage</c>) supplies
    /// the real file enumeration and only writes <c>VSWorkspaceSettings.json</c> when it does not
    /// already exist - see that call site's own remarks for why (never override a developer's own
    /// choices there, the same posture <c>RustLaunchTargetsGenerator.EnsureStartupItemSelectedAsync</c>
    /// takes for <c>ProjectSettings.json</c>).
    /// </summary>
    public static class NonRustProjectExclusionScanner
    {
        private static readonly string[] ProjectFileExtensions =
        {
            ".csproj", ".vbproj", ".fsproj", ".vcxproj", ".sln", ".slnx",
        };

        /// <param name="workspaceRoot">The Open Folder workspace root (the directory containing the root Cargo.toml).</param>
        /// <param name="allFilesUnderRoot">Every file path under <paramref name="workspaceRoot"/> (recursive), as the real caller gets from <c>Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)</c>.</param>
        /// <param name="cargoPackageManifestPaths">Every <c>Cargo.toml</c> path from `cargo metadata`'s own <c>packages</c> list - used only as a safety net, so a directory that is itself an ancestor of a real Rust package is never excluded no matter what non-Rust project files also happen to live under it.</param>
        /// <returns>
        /// Workspace-root-relative directories, forward-slash-separated (the <c>ExcludedItems</c>
        /// convention), deduplicated and sorted. Empty when nothing to exclude.
        /// </returns>
        public static IReadOnlyList<string> FindDirectoriesToExclude(
            string workspaceRoot,
            IEnumerable<string> allFilesUnderRoot,
            IEnumerable<string> cargoPackageManifestPaths)
        {
            if (string.IsNullOrEmpty(workspaceRoot))
            {
                throw new ArgumentNullException(nameof(workspaceRoot));
            }

            if (allFilesUnderRoot is null)
            {
                throw new ArgumentNullException(nameof(allFilesUnderRoot));
            }

            if (cargoPackageManifestPaths is null)
            {
                throw new ArgumentNullException(nameof(cargoPackageManifestPaths));
            }

            var root = workspaceRoot.TrimEnd('\\', '/');

            var rustTopLevelDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var manifestPath in cargoPackageManifestPaths)
            {
                var segments = RelativeSegments(root, manifestPath);
                if (segments.Count > 0)
                {
                    rustTopLevelDirs.Add(segments[0]);
                }
            }

            var toExclude = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in allFilesUnderRoot)
            {
                var extension = Path.GetExtension(file);
                if (!ProjectFileExtensions.Any(candidate => string.Equals(candidate, extension, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var segments = RelativeSegments(root, file);
                // A project file needs at least one containing directory below the root to name a
                // sensible exclude candidate; one directly at the root is too rare/odd to handle
                // (excluding the root itself would hide everything, including the workspace's own
                // Cargo.toml).
                if (segments.Count < 2)
                {
                    continue;
                }

                if (rustTopLevelDirs.Contains(segments[0]))
                {
                    // Never exclude a directory that is also an ancestor of a real Cargo package -
                    // a non-Rust project file coincidentally vendored inside Rust source (e.g. a
                    // generated .vcxproj some build script drops next to real crates) must not hide
                    // that source.
                    continue;
                }

                // Two segments deep (e.g. "tools/winforms-ref" rather than either the too-broad
                // "tools" or the too-narrow "tools/winforms-ref/parity/layout-winforms"): a
                // reasonable default granularity for "a reference/vendored tree of foreign
                // projects", confirmed against the live repro.
                var depth = Math.Min(segments.Count - 1, 2);
                toExclude.Add(string.Join("/", segments.Take(depth)));
            }

            return toExclude.ToList();
        }

        private static IReadOnlyList<string> RelativeSegments(string root, string path)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return Array.Empty<string>();
            }

            var relative = path.Substring(root.Length).TrimStart('\\', '/');
            if (relative.Length == 0)
            {
                return Array.Empty<string>();
            }

            return relative.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
        }
    }
}
