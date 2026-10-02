using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Rust.Cargo.Metadata;
using Kubuno.Rust.Cargo.Processes;
using Kubuno.Rust.Cargo.Registry;
using Kubuno.Rust.Cargo.Toolchain;
using Kubuno.Rust.Logic.SolutionExplorer;
using Kubuno.Shared.Logging;

namespace Kubuno.Rust.SolutionExplorer
{
    /// <summary>
    /// The slow parts of the Dependencies node, always off the UI thread: <c>cargo metadata</c> with the
    /// resolve graph for the host platform (<c>--filter-platform</c>, like a build) (offline first, so an up-to-date project never touches the network), the toolchain
    /// (<c>rustc -vV</c> / <c>--print sysroot</c> in the package folder, so <c>rust-toolchain.toml</c>
    /// applies; cached briefly per folder) and the crates.io status of direct registry crates (cached
    /// by <see cref="CratesIoClient"/>).
    /// </summary>
    internal static class DependencyDataLoader
    {
        private static readonly TimeSpan ToolchainCacheLifetime = TimeSpan.FromMinutes(2);
        private static readonly ConcurrentDictionary<string, (DateTime At, RustcVersionInfo? Info)> ToolchainCache =
            new ConcurrentDictionary<string, (DateTime, RustcVersionInfo?)>(StringComparer.OrdinalIgnoreCase);

        private static int _registryFailuresLogged;

        public static async Task<CargoMetadata> ReadDeclaredAsync(string manifestPath, CancellationToken cancellationToken)
        {
            var directory = System.IO.Path.GetDirectoryName(manifestPath)!;
            return await new CargoMetadataReader(new ProcessRunner())
                .ReadAsync(directory, manifestPath, CargoMetadataReadOptions.NoDependencies, null, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>The resolve graph, or why it could not be computed.</summary>
        public static async Task<(CargoMetadata? Metadata, string? Error)> ReadResolvedAsync(string manifestPath, string? hostTriple, CancellationToken cancellationToken)
        {
            var directory = System.IO.Path.GetDirectoryName(manifestPath)!;
            var reader = new CargoMetadataReader(new ProcessRunner());
            try
            {
                return (await reader.ReadAsync(directory, manifestPath, CargoMetadataReadOptions.IncludeDependencies | CargoMetadataReadOptions.Offline, null, cancellationToken, hostTriple).ConfigureAwait(false), null);
            }
            catch (CargoMetadataException)
            {
                // Missing downloads or a stale lock file: let cargo fetch what it needs.
            }

            try
            {
                return (await reader.ReadAsync(directory, manifestPath, CargoMetadataReadOptions.IncludeDependencies, null, cancellationToken, hostTriple).ConfigureAwait(false), null);
            }
            catch (CargoMetadataException exception)
            {
                return (null, Summarize(exception.Message));
            }
        }

        public static async Task<RustcVersionInfo?> GetToolchainAsync(string directory, CancellationToken cancellationToken)
        {
            if (ToolchainCache.TryGetValue(directory, out var cached) && DateTime.UtcNow - cached.At < ToolchainCacheLifetime)
            {
                return cached.Info;
            }

            RustcVersionInfo? info = null;
            try
            {
                var runner = new ProcessRunner();
                var version = await runner.RunAsync(new ProcessRunRequest("rustc", "-vV") { WorkingDirectory = directory }, null, cancellationToken).ConfigureAwait(false);
                info = version.Succeeded ? RustcVersionInfo.Parse(version.StandardOutputLines) : null;
                if (info != null)
                {
                    var sysroot = await runner.RunAsync(new ProcessRunRequest("rustc", "--print sysroot") { WorkingDirectory = directory }, null, cancellationToken).ConfigureAwait(false);
                    info.Sysroot = sysroot.Succeeded ? sysroot.StandardOutputLines.FirstOrDefault(l => l.Trim().Length > 0)?.Trim() : null;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                KubunoLog.WriteException("Dependencies: rustc -vV", exception);
            }

            ToolchainCache[directory] = (DateTime.UtcNow, info);
            return info;
        }

        /// <summary>Marks outdated/yanked direct crates.io crates; the network is optional (failures only lose the markers).</summary>
        public static async Task<bool> ApplyRegistryStatusAsync(IEnumerable<DependencyItem> items, CancellationToken cancellationToken)
        {
            var changed = false;
            var crates = items.Where(i => i.ItemKind == DependencyItemKind.Crate && !i.IsTransitive && i.IsCratesIo && i.ResolvedVersion != null).ToList();
            var tasks = crates.Select(async item =>
            {
                try
                {
                    var versions = await CratesIoClient.Shared.GetVersionsAsync(item.Name, cancellationToken).ConfigureAwait(false);
                    if (versions.Count > 0)
                    {
                        item.ApplyRegistryStatus(CrateVersionStatus.Compute(item.ResolvedVersion, versions));
                        changed = true;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    // Offline or crates.io unreachable: log once per session, keep the tree as it is.
                    if (Interlocked.Exchange(ref _registryFailuresLogged, 1) == 0)
                    {
                        KubunoLog.WriteLine($"Dependencies: crates.io is unreachable, update/yanked markers are skipped ({exception.Message}).");
                    }
                }
            });
            await Task.WhenAll(tasks).ConfigureAwait(false);
            return changed;
        }

        /// <summary>cargo's error without its "Caused by:" noise: the "error:" lines, else the first lines.</summary>
        internal static string Summarize(string message)
        {
            var lines = (message ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            var errors = lines.Where(l => l.StartsWith("error", StringComparison.OrdinalIgnoreCase)).ToList();
            var chosen = errors.Count > 0 ? errors : lines.Take(3).ToList();
            var text = string.Join(" ", chosen);
            var causeIndex = lines.FindIndex(l => l.StartsWith("Caused by:", StringComparison.Ordinal));
            if (causeIndex >= 0 && causeIndex + 1 < lines.Count)
            {
                text += " - " + lines[lines.Count - 1];
            }

            return text.Length > 0 ? text : message ?? string.Empty;
        }
    }
}
