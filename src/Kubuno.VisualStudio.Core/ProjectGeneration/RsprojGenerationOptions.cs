using System;
using Kubuno.Cargo.Metadata;

namespace Kubuno.VisualStudio.Core.ProjectGeneration
{
    /// <summary>
    /// Options for <see cref="RsprojGenerationPlanner.Plan"/> ("Generate Visual Studio projects" -
    /// docs/RSPROJ.md work package 5).
    /// </summary>
    public sealed class RsprojGenerationOptions
    {
        /// <summary>
        /// <c>Kubuno.Rust.Sdk</c> version to pin in every generated <c>.rsproj</c>'s
        /// <c>&lt;Project Sdk="Kubuno.Rust.Sdk/&lt;version&gt;"&gt;</c> - the source of truth is
        /// <c>sdk/Kubuno.Rust.Sdk/Kubuno.Rust.Sdk.csproj</c>'s own <c>&lt;Version&gt;</c>; the
        /// caller passes it in rather than this pure library reading a file, so version
        /// resolution stays entirely the VSIX command's responsibility (§6 risk: "design-time
        /// evaluation only", same discipline as the SDK itself never shelling out).
        /// </summary>
        public string SdkVersion { get; }

        /// <summary>
        /// Generate a <c>.rsproj</c> for a workspace member that has no <c>[[bin]]</c> target at
        /// all (a pure library crate) - off by default (docs/RSPROJ.md work package 5: "optionally
        /// setting off by default"). Such a project has nothing for F5/Ctrl+F5 to run, but still
        /// lets the crate's files show up in the solution and its own Cargo/Debug property pages
        /// build/clean it like any other member.
        /// </summary>
        public bool IncludeLibraryOnlyMembers { get; }

        /// <summary>
        /// Where to place a member's generated <c>.rsproj</c> - by default, right next to its
        /// <c>Cargo.toml</c> (<see cref="CargoPackage.ManifestPath"/>'s own directory), which is
        /// the normal, real "Generate Visual Studio projects" outcome. A caller may resolve a
        /// different directory (e.g. a scratch mirror of a workspace that must not be written to
        /// directly - see docs/RSPROJ.md work package 5's live-test note); when the resolved
        /// directory differs from the manifest's own directory, the generated file sets
        /// <c>&lt;CargoManifestPath&gt;</c> explicitly so the project still points at the real
        /// manifest instead of assuming a sibling one.
        /// </summary>
        public Func<CargoPackage, string> ResolveProjectDirectory { get; }

        public RsprojGenerationOptions(
            string sdkVersion,
            bool includeLibraryOnlyMembers = false,
            Func<CargoPackage, string>? resolveProjectDirectory = null)
        {
            if (string.IsNullOrWhiteSpace(sdkVersion))
            {
                throw new ArgumentException("SDK version must not be empty.", nameof(sdkVersion));
            }

            SdkVersion = sdkVersion;
            IncludeLibraryOnlyMembers = includeLibraryOnlyMembers;
            ResolveProjectDirectory = resolveProjectDirectory ?? DefaultProjectDirectory;
        }

        private static string DefaultProjectDirectory(CargoPackage package) =>
            System.IO.Path.GetDirectoryName(package.ManifestPath) ?? string.Empty;
    }
}
