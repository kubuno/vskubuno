using System;

namespace Kubuno.VisualStudio.Core.ProjectGeneration
{
    /// <summary>
    /// Recognizes a <c>[workspace]</c> Cargo.toml without a full TOML parser - the same
    /// "just enough" text scanning already used elsewhere in this codebase (e.g.
    /// <c>CargoWorkspaceLocator</c> only ever checks for the manifest's existence). Used by the
    /// "Generate Visual Studio Projects" context menu command (docs/RSPROJ.md work package 5) to
    /// only show itself on a workspace-root <c>Cargo.toml</c>, not on an arbitrary member's.
    /// </summary>
    public static class WorkspaceManifestScanner
    {
        /// <returns>
        /// <see langword="true"/> when <paramref name="manifestText"/> has a top-level
        /// <c>[workspace]</c> (or <c>[workspace.*]</c>) table header on its own line - a plain
        /// <c>[package]</c>-only manifest (even one belonging to a workspace as a member) returns
        /// <see langword="false"/>.
        /// </returns>
        public static bool IsWorkspaceManifest(string? manifestText)
        {
            if (string.IsNullOrEmpty(manifestText))
            {
                return false;
            }

            foreach (var rawLine in manifestText!.Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                if (line.Equals("[workspace]", StringComparison.Ordinal) ||
                    line.StartsWith("[workspace.", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
