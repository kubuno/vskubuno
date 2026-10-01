using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Kubuno.Web.Logic.DevCore
{
    /// <summary>
    /// Which core a module's F5 starts (docs/WEB.md, "F5 on a module"): an explicit <c>KubunoDevCoreExecutable</c>; else
    /// the core built from the core repository next to the module (<c>..\core</c>, the polyrepo layout - built in the
    /// multi-repository solution or its own <c>Kubuno.Core.slnx</c>), in the shared <c>CARGO_TARGET_DIR</c> or in the
    /// core's own <c>target</c>; else a Windows installation (<c>C:\Program Files\Kubuno</c>).
    /// </summary>
    public static class DevCoreLocator
    {
        public const string InstalledCoreDirectory = @"C:\Program Files\Kubuno";

        /// <summary>The core repository next to <paramref name="moduleDirectory"/>, or null.</summary>
        public static string? SiblingCoreRepository(string moduleDirectory)
        {
            var parent = Path.GetDirectoryName(Path.GetFullPath(moduleDirectory).TrimEnd(Path.DirectorySeparatorChar));
            if (parent is null)
            {
                return null;
            }

            var core = Path.Combine(parent, "core");
            return File.Exists(Path.Combine(core, "crates", "kubuno-core", "Cargo.toml")) ? core : null;
        }

        /// <summary>The candidates, in order (the first existing one wins).</summary>
        public static IReadOnlyList<string> Candidates(string moduleDirectory, string? explicitExecutable, string? cargoTargetDirectory)
        {
            var candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(explicitExecutable))
            {
                candidates.Add(explicitExecutable!);
                return candidates;
            }

            var core = SiblingCoreRepository(moduleDirectory);
            var targetDirectories = new List<string>();
            if (!string.IsNullOrWhiteSpace(cargoTargetDirectory))
            {
                targetDirectories.Add(cargoTargetDirectory!);
            }

            if (core is not null)
            {
                targetDirectories.Add(Path.Combine(core, "target"));
            }

            foreach (var target in targetDirectories)
            {
                candidates.Add(Path.Combine(target, "debug", "kubuno-core.exe"));
                candidates.Add(Path.Combine(target, "release", "kubuno-core.exe"));
            }

            candidates.Add(Path.Combine(InstalledCoreDirectory, "kubuno-core.exe"));
            return candidates;
        }

        /// <summary>The newest existing debug/release build among the repository candidates, or the installed core; null when none.</summary>
        public static string? FindCoreExecutable(string moduleDirectory, string? explicitExecutable, string? cargoTargetDirectory)
        {
            var candidates = Candidates(moduleDirectory, explicitExecutable, cargoTargetDirectory);
            if (!string.IsNullOrWhiteSpace(explicitExecutable))
            {
                return File.Exists(candidates[0]) ? candidates[0] : null;
            }

            var built = candidates.Where(path => !path.StartsWith(InstalledCoreDirectory, StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            return built ?? candidates.FirstOrDefault(File.Exists);
        }

        /// <summary>The host frontend served with <paramref name="coreExecutable"/>: the repository's <c>frontend\dist</c>, or the installation's <c>frontend</c>.</summary>
        public static string? FindFrontendDist(string? coreRepository, string coreExecutable)
        {
            if (coreRepository is not null)
            {
                var dist = Path.Combine(coreRepository, "frontend", "dist");
                if (File.Exists(Path.Combine(dist, "index.html")))
                {
                    return dist;
                }
            }

            var installed = Path.Combine(Path.GetDirectoryName(coreExecutable) ?? string.Empty, "frontend");
            return File.Exists(Path.Combine(installed, "index.html")) ? installed : null;
        }
    }
}
