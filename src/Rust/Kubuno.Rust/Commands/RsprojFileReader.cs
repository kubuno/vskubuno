using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Kubuno.Rust.Commands
{
    /// <summary>
    /// Reads just enough of a <c>.rsproj</c> file's own XML to find the Cargo.toml it names - a
    /// plain <see cref="XDocument"/> read, not a full MSBuild evaluation (the same "cheap, read-only
    /// text peek" spirit as <c>WorkspaceManifestScanner</c>'s own Cargo.toml scan). Used by
    /// <see cref="AddProjectReferenceCommand"/> to resolve every *other* <c>.rsproj</c> in the
    /// solution without loading each one as a project.
    /// </summary>
    internal static class RsprojFileReader
    {
        /// <summary>
        /// The Cargo.toml a <c>.rsproj</c> at <paramref name="rsprojPath"/> names - its own explicit
        /// <c>&lt;CargoManifestPath&gt;</c> property when set (resolved relative to the
        /// <c>.rsproj</c>'s own directory, matching <c>Kubuno.Rust.Sdk\Sdk.props</c>), otherwise the
        /// sibling <c>Cargo.toml</c> (the SDK's own default). <see langword="null"/> when neither exists.
        /// </summary>
        public static string? TryReadManifestPath(string rsprojPath)
        {
            var projectDirectory = Path.GetDirectoryName(rsprojPath) ?? string.Empty;

            try
            {
                var document = XDocument.Load(rsprojPath);
                var explicitPath = document.Root?
                    .Descendants()
                    .FirstOrDefault(element => string.Equals(element.Name.LocalName, "CargoManifestPath", StringComparison.Ordinal))?
                    .Value;

                if (!string.IsNullOrWhiteSpace(explicitPath))
                {
                    var resolved = Path.IsPathRooted(explicitPath) ? explicitPath : Path.GetFullPath(Path.Combine(projectDirectory, explicitPath!));
                    if (File.Exists(resolved))
                    {
                        return resolved;
                    }
                }
            }
            catch (Exception)
            {
                // Malformed/unreadable .rsproj: fall through to the sibling-manifest default below,
                // the same fallback RsprojProjectContext itself uses for a loaded project.
            }

            var sibling = Path.Combine(projectDirectory, "Cargo.toml");
            return File.Exists(sibling) ? sibling : null;
        }
    }
}
