using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kubuno.Rust.Cargo.Metadata;

namespace Kubuno.Rust.Logic.ProjectGeneration
{
    /// <summary>
    /// The pure part of "Generate Visual Studio Projects" (docs/RSPROJ.md work package 5): from an
    /// already-read <c>cargo metadata</c> result, decides one <c>.rsproj</c> per eligible workspace
    /// member and whether each would be created or skipped - no file I/O here (see
    /// <see cref="RsprojGenerationOptions"/>'s delegates for the only file-system knowledge this
    /// class needs, injected so it stays unit-testable with <c>dotnet test</c>, matching every
    /// other "pure logic" class in this project - <c>StartupItemSelector</c>,
    /// <c>CargoWorkspaceLocator</c>).
    /// </summary>
    public static class RsprojGenerationPlanner
    {
        /// <param name="metadata">Already-read <c>cargo metadata --no-deps</c> result for the workspace.</param>
        /// <param name="options">SDK version, library-only opt-in and project-directory resolution.</param>
        /// <param name="projectFileExists">
        /// Returns <see langword="true"/> when a file already exists at the given full path -
        /// idempotency (docs/RSPROJ.md work package 5: "only ever create a .rsproj that does not
        /// already exist") is decided purely from this, never from anything the plan itself wrote.
        /// </param>
        public static IReadOnlyList<RsprojProjectPlanItem> Plan(
            CargoMetadata metadata,
            RsprojGenerationOptions options,
            Func<string, bool> projectFileExists)
        {
            if (metadata is null)
            {
                throw new ArgumentNullException(nameof(metadata));
            }

            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (projectFileExists is null)
            {
                throw new ArgumentNullException(nameof(projectFileExists));
            }

            var memberIds = new HashSet<string>(metadata.WorkspaceMembers, StringComparer.Ordinal);
            var items = new List<RsprojProjectPlanItem>();

            // A Rust dylib shared inside the workspace must be built together
            // with every program that loads it: cargo names a dylib without a hash, and building one package at a time
            // rebuilds it with that package's feature set, breaking the programs built before (docs/RSPROJ.md, "Cargo
            // workspaces"). Such a workspace's projects build the whole workspace instead.
            var workspaceBuild = metadata.WorkspaceMembers.Count > 1 && metadata.Packages
                .Where(package => memberIds.Contains(package.Id))
                .Any(package => package.Targets.Any(target => target.IsKind(CargoTargetKind.Dylib) || target.CrateTypes.Contains(CargoTargetKind.Dylib)));
            if (options.WorkspaceBuildForSeveralPrograms && metadata.Packages
                .Where(package => memberIds.Contains(package.Id))
                .Count(package => package.Targets.Any(target => target.IsKind(CargoTargetKind.Bin))) > 1)
            {
                workspaceBuild = true;
            }

            foreach (var package in metadata.Packages)
            {
                if (memberIds.Count > 0 && !memberIds.Contains(package.Id))
                {
                    // Defensive only: `--no-deps` already restricts `packages` to workspace
                    // members, but a caller-supplied metadata (e.g. a hand-built one in a test, or
                    // a future call site that drops --no-deps) should not silently generate a
                    // project for an external dependency.
                    continue;
                }

                var binNames = package.Targets
                    .Where(target => target.IsKind(CargoTargetKind.Bin))
                    .Select(target => target.Name)
                    .ToList();

                var isLibraryOnly = binNames.Count == 0;
                if (isLibraryOnly && !options.IncludeLibraryOnlyMembers)
                {
                    continue;
                }

                var projectDirectory = options.ResolveProjectDirectory(package);
                if (string.IsNullOrEmpty(projectDirectory))
                {
                    continue;
                }

                var projectName = options.ResolveProjectName(package);
                var projectPath = Path.Combine(projectDirectory, (string.IsNullOrEmpty(projectName) ? package.Name : projectName) + ".rsproj");
                var manifestPathRelativeToProject = ComputeManifestPathForProject(projectDirectory, package.ManifestPath);
                var cargoBin = SelectCargoBinIfNeeded(package, binNames);

                var content = RsprojTemplate.Build(package.Name, options.SdkVersion, cargoBin, manifestPathRelativeToProject, workspaceBuild);
                var action = projectFileExists(projectPath) ? RsprojPlanAction.SkipExisting : RsprojPlanAction.Create;

                items.Add(new RsprojProjectPlanItem(package.Name, projectPath, package.ManifestPath, action, content, isLibraryOnly, hasBin: !isLibraryOnly));
            }

            return items
                .OrderBy(item => item.PackageName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// <c>&lt;CargoBin&gt;</c> only when the package has more than one <c>[[bin]]</c> target, or a single one
        /// not named after the package (<c>drive-app</c>'s <c>drive</c>): the SDK's own default names the executable
        /// after the package, so <c>$(TargetPath)</c> - and F5 - would otherwise look for a file that is never built.
        /// A single bin named after the package (or none) needs nothing written. The pick mirrors
        /// <c>Kubuno.Rust.Logic.StartupItemSelector.SelectDefaultBinTarget</c>'s exact
        /// fallback chain (default-run, then the bin named after the package, then the first bin
        /// in `cargo metadata`'s own order) - duplicated rather than shared because that selector
        /// lives in the same assembly but takes a whole <see cref="CargoPackage"/> and this call
        /// site already has <paramref name="binNames"/> computed; keeping the two in obvious sync
        /// is cheap (both are a handful of lines gated by the same well-known cargo behavior).
        /// </summary>
        private static string? SelectCargoBinIfNeeded(CargoPackage package, IReadOnlyList<string> binNames)
        {
            if (binNames.Count == 0)
            {
                return null;
            }

            if (binNames.Count == 1)
            {
                return string.Equals(binNames[0], package.Name, StringComparison.Ordinal) ? null : binNames[0];
            }

            if (!string.IsNullOrEmpty(package.DefaultRun) && binNames.Contains(package.DefaultRun!))
            {
                return package.DefaultRun;
            }

            if (binNames.Contains(package.Name))
            {
                return package.Name;
            }

            return binNames[0];
        }

        /// <returns>
        /// <see langword="null"/> when <paramref name="projectDirectory"/> already contains the
        /// manifest (the common case: the generated project sits right next to its
        /// <c>Cargo.toml</c>); otherwise a path (relative when possible) from
        /// <paramref name="projectDirectory"/> to <paramref name="manifestPath"/>.
        /// </returns>
        private static string? ComputeManifestPathForProject(string projectDirectory, string manifestPath)
        {
            var manifestDirectory = Path.GetDirectoryName(manifestPath) ?? string.Empty;
            if (PathsEqual(projectDirectory, manifestDirectory))
            {
                return null;
            }

            return MakeRelativePath(projectDirectory, manifestPath);
        }

        private static bool PathsEqual(string a, string b) =>
            string.Equals(
                a.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                b.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Relative path from <paramref name="fromDirectory"/> to <paramref name="toFile"/>, using
        /// forward slashes so the generated XML is diff-friendly regardless of host OS (MSBuild
        /// accepts both separators on Windows; the generator only ever runs on Windows, but the
        /// resulting file is still text a developer may read/diff cross-platform).
        /// </summary>
        private static string MakeRelativePath(string fromDirectory, string toFile)
        {
            var fromUri = new Uri(AppendDirectorySeparator(fromDirectory));
            var toUri = new Uri(toFile);
            var relativeUri = fromUri.MakeRelativeUri(toUri);
            return Uri.UnescapeDataString(relativeUri.ToString());
        }

        private static string AppendDirectorySeparator(string path) =>
            path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ? path : path + Path.DirectorySeparatorChar;
    }
}
