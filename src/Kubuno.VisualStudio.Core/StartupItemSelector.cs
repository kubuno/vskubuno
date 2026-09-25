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
    }
}
