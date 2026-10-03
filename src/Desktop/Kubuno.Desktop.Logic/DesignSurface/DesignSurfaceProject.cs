using System;
using System.Collections.Generic;
using System.IO;

namespace Kubuno.Desktop.Logic.DesignSurface
{
    /// <summary>
    /// The cargo build of one <c>.rsproj</c> configuration, exactly as <c>Kubuno.Rust.Sdk</c>'s
    /// <c>CargoBuild</c> task runs it (same manifest, package, bin, profile, extra arguments and
    /// <c>CARGO_TARGET_DIR</c>): the design build re-runs that very command to learn the artifacts of
    /// the project's dependency graph (docs/DESIGNER.md section 15). Any difference - a feature, a
    /// profile - would make cargo build other variants of the crates than the ones the project's
    /// application links, and the surface would no longer render with the project's own build.
    /// </summary>
    public sealed class DesignSurfaceProject
    {
        public DesignSurfaceProject(string manifestPath, string profile)
        {
            if (string.IsNullOrWhiteSpace(manifestPath))
            {
                throw new ArgumentException("A manifest path is required.", nameof(manifestPath));
            }

            ManifestPath = Path.GetFullPath(manifestPath);
            Profile = string.IsNullOrWhiteSpace(profile) ? "debug" : profile;
        }

        /// <summary><c>$(CargoManifestPath)</c>.</summary>
        public string ManifestPath { get; }

        /// <summary><c>$(CargoProfile)</c>: "debug", "release" or a custom profile name.</summary>
        public string Profile { get; }

        /// <summary><c>$(CargoProfileDirName)</c>, the profile's folder under the target directory.</summary>
        public string? ProfileDirectoryName { get; set; }

        /// <summary><c>$(CargoPackage)</c> (<c>-p</c>), if any.</summary>
        public string? Package { get; set; }

        /// <summary><c>$(CargoBin)</c> (<c>--bin</c>), if any.</summary>
        public string? Bin { get; set; }

        /// <summary><c>$(CargoTargetDir)</c>: exported as <c>CARGO_TARGET_DIR</c> when set, like the SDK does.</summary>
        public string? TargetDirectory { get; set; }

        /// <summary>
        /// Where the target directory is expected before cargo was asked (<c>$(_KubunoEffectiveTargetDir)</c>):
        /// used only to find an earlier design build without running cargo. Cargo's own artifact
        /// paths are authoritative once it ran (a <c>.cargo/config.toml</c> may move the directory).
        /// </summary>
        public string? ExpectedTargetDirectory { get; set; }

        /// <summary><c>$(CargoExtraArgs)</c>, split on <c>;</c> like MSBuild passes it to the task.</summary>
        public IReadOnlyList<string> ExtraArgs { get; set; } = Array.Empty<string>();

        public string ManifestDirectory => Path.GetDirectoryName(ManifestPath) ?? ManifestPath;

        /// <summary>The profile's folder name: <see cref="ProfileDirectoryName"/>, else <see cref="Profile"/>.</summary>
        public string EffectiveProfileDirectoryName =>
            string.IsNullOrWhiteSpace(ProfileDirectoryName) ? Profile : ProfileDirectoryName!;

        /// <summary><see cref="ExpectedTargetDirectory"/>, else <see cref="TargetDirectory"/>, else <c>&lt;manifest dir&gt;\target</c>.</summary>
        public string EffectiveExpectedTargetDirectory =>
            !string.IsNullOrWhiteSpace(ExpectedTargetDirectory) ? ExpectedTargetDirectory!
            : !string.IsNullOrWhiteSpace(TargetDirectory) ? TargetDirectory!
            : Path.Combine(ManifestDirectory, "target");

        /// <summary>Splits an MSBuild <c>$(CargoExtraArgs)</c> value the way an <c>ITaskItem[]</c>/<c>string[]</c> task parameter receives it.</summary>
        public static IReadOnlyList<string> SplitExtraArgs(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return Array.Empty<string>();
            }

            var result = new List<string>();
            foreach (var part in value!.Split(';'))
            {
                var trimmed = part.Trim();
                if (trimmed.Length > 0)
                {
                    result.Add(trimmed);
                }
            }

            return result;
        }

        /// <summary>A stable identity (manifest + profile + target dir) for sharing one design build between documents.</summary>
        public string Identity =>
            string.Join("|", ManifestPath.ToUpperInvariant(), Profile, TargetDirectory ?? string.Empty, Package ?? string.Empty, Bin ?? string.Empty, string.Join(";", ExtraArgs));
    }
}
