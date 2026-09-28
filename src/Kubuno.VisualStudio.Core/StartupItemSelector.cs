using System.IO;
using System.Linq;
using Kubuno.Cargo.Metadata;

namespace Kubuno.VisualStudio.Core
{
    /// <summary>
    /// Picks a sensible default "Select Startup Item" for a Cargo package that has never had one
    /// chosen yet - mirrors `cargo run`'s own, well-known fallback chain (see
    /// <see cref="CargoPackage.DefaultRun"/>'s remarks) for exactly the same reason CMake
    /// Tools/Makefile Open Folder support pre-select a target: F5 should work immediately after
    /// opening the folder, before the developer has ever touched the toolbar dropdown.
    ///
    /// Only <c>[[bin]]</c> targets are candidates - `cargo run`'s own fallback chain (and so this
    /// one) never considers examples/tests; a developer who wants those keeps using the dropdown
    /// or "Kubuno: Debug Rust Test at Cursor". Pure/VS-SDK-free (see this project's own csproj
    /// comment) so it is unit-tested with plain <c>dotnet test</c> - the actual "apply the
    /// selection" side effects live in <c>Kubuno.VisualStudio.Debugging.RustLaunchTargetsGenerator</c>.
    /// </summary>
    public static class StartupItemSelector
    {
        /// <returns>
        /// The <c>[[bin]]</c> target name to pre-select, or <see langword="null"/> when
        /// <paramref name="package"/> has no bin targets at all (nothing to select).
        /// </returns>
        public static string? SelectDefaultBinTarget(CargoPackage package)
        {
            if (package is null)
            {
                return null;
            }

            var binNames = package.Targets
                .Where(target => target.IsKind(CargoTargetKind.Bin))
                .Select(target => target.Name)
                .ToList();

            if (binNames.Count == 0)
            {
                return null;
            }

            // 1. The package's own `default-run`, if it names a bin that actually exists.
            var defaultRun = package.DefaultRun;
            if (!string.IsNullOrEmpty(defaultRun) && binNames.Contains(defaultRun!))
            {
                return defaultRun;
            }

            // 2. The only [[bin]] - unambiguous regardless of its name.
            if (binNames.Count == 1)
            {
                return binNames[0];
            }

            // 3. A bin named after the package (cargo's own convention for a package's "main" binary).
            if (binNames.Contains(package.Name))
            {
                return package.Name;
            }

            // 4. The first bin, in `cargo metadata`'s own (deterministic, alphabetical-by-target-name) order.
            return binNames[0];
        }

        /// <summary>
        /// Picks a sensible default startup item when the opened folder's own <c>Cargo.toml</c> is a
        /// virtual <c>[workspace]</c> manifest with no <c>[package]</c> of its own - <see
        /// cref="SelectDefaultBinTarget"/> has nothing to work from then, since a virtual manifest never
        /// appears in `cargo metadata`'s <c>packages</c> list (it is not itself a package), which used to
        /// mean F5 was never pre-selected at all for exactly this - very common - workspace shape (see
        /// <c>Kubuno.VisualStudio.Debugging.RustLaunchTargetsGenerator.GenerateAsync</c>'s own remarks).
        /// Falls back, in order:
        /// 1. Restrict to `cargo`'s own <see cref="CargoMetadata.WorkspaceDefaultMembers"/> when that
        ///    narrows anything down (an explicit <c>[workspace] default-members</c> in the manifest) -
        ///    honoring an existing Cargo-level choice beats guessing.
        /// 2. The only <c>[[bin]]</c> across the candidate packages - unambiguous regardless of which
        ///    package it is in.
        /// 3. A package whose own directory is named "shell" - a heuristic (not a Cargo concept) for the
        ///    common convention of a desktop workspace's main GUI entry point living in a `shell/`
        ///    crate (e.g. this workspace's <c>src/shell</c> -&gt; <c>kubuno-desktop</c>); it only fires
        ///    when it finds one; it never assumes a specific package or binary name.
        /// 4. The first bin target, in `cargo metadata`'s own deterministic package/target order - same
        ///    "better than nothing" fallback as <see cref="SelectDefaultBinTarget"/>'s own last resort.
        /// </summary>
        /// <returns>The package owning the chosen target and the target's own name, or <see langword="null"/> when the workspace has no <c>[[bin]]</c> target at all.</returns>
        public static (CargoPackage Package, string BinTarget)? SelectDefaultBinTargetForWorkspace(CargoMetadata metadata)
        {
            if (metadata is null)
            {
                return null;
            }

            var packages = metadata.Packages;
            if (metadata.WorkspaceDefaultMembers.Count > 0 && metadata.WorkspaceDefaultMembers.Count < packages.Count)
            {
                var defaultMemberIds = new System.Collections.Generic.HashSet<string>(metadata.WorkspaceDefaultMembers);
                var restricted = packages.Where(package => defaultMemberIds.Contains(package.Id)).ToList();
                if (restricted.Count > 0)
                {
                    packages = restricted;
                }
            }

            var candidates = packages
                .SelectMany(package => package.Targets
                    .Where(target => target.IsKind(CargoTargetKind.Bin))
                    .Select(target => (Package: package, BinTarget: target.Name)))
                .ToList();

            if (candidates.Count == 0)
            {
                return null;
            }

            if (candidates.Count == 1)
            {
                return candidates[0];
            }

            foreach (var candidate in candidates)
            {
                var packageDirectory = Path.GetDirectoryName(candidate.Package.ManifestPath);
                if (string.Equals(Path.GetFileName(packageDirectory), "shell", System.StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }

            return candidates[0];
        }
    }
}
