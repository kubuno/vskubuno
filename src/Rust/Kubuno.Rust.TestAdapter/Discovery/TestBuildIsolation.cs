using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Rust.Cargo.Metadata;
using Kubuno.Rust.Cargo.Processes;

namespace Kubuno.Rust.TestAdapter.Discovery
{
    /// <summary>
    /// Which packages of a workspace must build their tests apart from the others (docs/RSPROJ.md, "Cargo workspaces").
    /// cargo names a <c>dylib</c> without a hash, and resolver 2 resolves the dependencies of a proc-macro separately
    /// (host dependencies). A proc-macro whose tests use the workspace's dylib (as the Kubuno desktop workspace's
    /// <c>kubuno-views-macros</c> did while <c>kubuno-ui</c> was a dylib, before 2026-10-03) makes
    /// <c>cargo test --workspace</c> build that dylib twice into the same file - "output filename collision", then a link
    /// failure (LNK1104) - and no test of the workspace can be listed. Such packages are excluded from the workspace
    /// build and built on their own, each in a target directory of its own under the workspace's. And the tests of a
    /// workspace with a dylib are built in a target folder of their own (<see cref="Plan.WorkspaceTestTargetDirectory"/>).
    /// </summary>
    public static class TestBuildIsolation
    {
        /// <summary>The packages to build apart, and where.</summary>
        public sealed class Plan
        {
            public Plan(IReadOnlyList<string> packages, string targetDirectory)
            {
                Packages = packages;
                TargetDirectory = targetDirectory;
            }

            public IReadOnlyList<string> Packages { get; }

            /// <summary>The workspace's own target directory.</summary>
            public string TargetDirectory { get; }

            public string TargetDirectoryFor(string package) => Path.Combine(TargetDirectory, "kubuno-isolated-tests", package);

            /// <summary>
            /// Where the workspace's own test build goes: a folder of its own under the target directory. A test build turns
            /// the dev-dependencies' features on, so it builds the shared dylib with other features than the application
            /// build does; in the same folder, each would overwrite the other's DLL and leave the other's programs linked
            /// against a DLL that no longer matches them ("entry point not found").
            /// </summary>
            public string WorkspaceTestTargetDirectory => Path.Combine(TargetDirectory, "kubuno-tests");
        }

        /// <summary>
        /// Null (build everything at once, as before) unless <paramref name="manifestPath"/> is a workspace root whose
        /// <c>cargo metadata</c> has a package to isolate (<see cref="PackagesToIsolate"/>). Never throws for a manifest it
        /// cannot read: it only ever makes a working build work better.
        /// </summary>
        public static async Task<Plan?> PlanAsync(
            IProcessRunner processRunner,
            string manifestPath,
            string workingDirectory,
            IReadOnlyDictionary<string, string>? environmentVariables,
            CancellationToken cancellationToken)
        {
            try
            {
                if (!File.Exists(manifestPath) || !File.ReadAllText(manifestPath).Contains("[workspace"))
                {
                    return null;
                }

                CargoMetadata metadata = await new CargoMetadataReader(processRunner)
                    .ReadAsync(workingDirectory, manifestPath, environmentVariables, cancellationToken)
                    .ConfigureAwait(false);
                return HasDylibMember(metadata) && !string.IsNullOrEmpty(metadata.TargetDirectory)
                    ? new Plan(PackagesToIsolate(metadata), metadata.TargetDirectory)
                    : null;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CargoMetadataException or System.Text.Json.JsonException)
            {
                return null;
            }
        }

        /// <summary>Whether a member of the workspace builds a <c>dylib</c> (the only case that needs any of this).</summary>
        public static bool HasDylibMember(CargoMetadata metadata)
        {
            var memberIds = new HashSet<string>(metadata.WorkspaceMembers, StringComparer.Ordinal);
            return metadata.Packages.Any(package => memberIds.Contains(package.Id)
                && package.Targets.Any(target => target.IsKind(CargoTargetKind.Dylib) || target.CrateTypes.Contains(CargoTargetKind.Dylib)));
        }

        /// <summary>
        /// The proc-macro members whose dependencies, dev-dependencies included, reach (through other members) a member that
        /// builds a <c>dylib</c>.
        /// </summary>
        public static IReadOnlyList<string> PackagesToIsolate(CargoMetadata metadata)
        {
            if (metadata is null)
            {
                throw new ArgumentNullException(nameof(metadata));
            }

            var memberIds = new HashSet<string>(metadata.WorkspaceMembers, StringComparer.Ordinal);
            var members = metadata.Packages.Where(package => memberIds.Contains(package.Id)).ToList();
            var byName = members.GroupBy(package => package.Name, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

            bool IsDylib(CargoPackage package) =>
                package.Targets.Any(target => target.IsKind(CargoTargetKind.Dylib) || target.CrateTypes.Contains(CargoTargetKind.Dylib));

            bool IsProcMacro(CargoPackage package) => package.Targets.Any(target => target.IsKind(CargoTargetKind.ProcMacro));

            IEnumerable<CargoPackage> MemberDependencies(CargoPackage package) =>
                package.Dependencies
                    .Where(dependency => dependency.Path is not null && byName.ContainsKey(dependency.Name))
                    .Select(dependency => byName[dependency.Name]);

            bool ReachesDylib(CargoPackage start)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal) { start.Name };
                var pending = new Stack<CargoPackage>(MemberDependencies(start));
                while (pending.Count > 0)
                {
                    var package = pending.Pop();
                    if (!seen.Add(package.Name))
                    {
                        continue;
                    }

                    if (IsDylib(package))
                    {
                        return true;
                    }

                    foreach (var next in MemberDependencies(package))
                    {
                        pending.Push(next);
                    }
                }

                return false;
            }

            return members
                .Where(package => IsProcMacro(package) && ReachesDylib(package))
                .Select(package => package.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
        }
    }
}
