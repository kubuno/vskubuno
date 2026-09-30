using System;
using System.IO;

namespace Kubuno.Rust.Launch
{
    /// <summary>
    /// Pure helpers for the on-disk layout Cargo produces under a target directory:
    /// `&lt;target-dir&gt;/[&lt;triple&gt;/]&lt;profile-dir&gt;/[examples/]&lt;name&gt;[.exe]`.
    /// </summary>
    public static class CargoLayout
    {
        /// <summary>
        /// Resolves the effective Cargo target directory, honouring `CARGO_TARGET_DIR`.
        /// Mirrors Cargo's own precedence: an explicit override wins outright (Cargo
        /// resolves a relative `CARGO_TARGET_DIR` against the invocation directory, which
        /// this library has no notion of, so callers should pass an absolute value — the
        /// Kubuno desktop workspace always does: `CARGO_TARGET_DIR=C:\kubuno-build\desktop-target`);
        /// otherwise it defaults to `&lt;workspaceRoot&gt;/target`.
        /// </summary>
        /// <param name="workspaceRoot">The Cargo workspace root.</param>
        /// <param name="cargoTargetDirOverride">
        /// The value of the `CARGO_TARGET_DIR` environment variable (or `build.target-dir`
        /// from `.cargo/config.toml`), or null/empty when unset. Reading the actual
        /// environment is the caller's responsibility, so this stays a pure function.
        /// </param>
        public static string ResolveTargetDir(string workspaceRoot, string? cargoTargetDirOverride)
        {
            if (workspaceRoot is null)
            {
                throw new ArgumentNullException(nameof(workspaceRoot));
            }

            if (!string.IsNullOrEmpty(cargoTargetDirOverride))
            {
                return cargoTargetDirOverride!;
            }

            return Path.Combine(workspaceRoot, "target");
        }

        /// <summary>
        /// Maps a Cargo profile name to its output directory name. The two built-in
        /// profiles are special-cased ("dev" builds land under "debug"); any other name
        /// (a custom profile declared in `[profile.&lt;name&gt;]`) is its own directory name
        /// verbatim.
        /// </summary>
        public static string ResolveProfileDirectoryName(string profile)
        {
            if (string.IsNullOrEmpty(profile))
            {
                throw new ArgumentException("Profile must not be null or empty.", nameof(profile));
            }

            return profile switch
            {
                "dev" => "debug",
                "test" => "debug",
                "bench" => "release",
                _ => profile,
            };
        }

        /// <summary>
        /// Combines the target directory, an optional `--target &lt;triple&gt;` subdirectory,
        /// and the profile directory, matching Cargo's own layout
        /// (`&lt;target-dir&gt;/[&lt;triple&gt;/]&lt;profile-dir&gt;`).
        /// </summary>
        public static string ResolveProfileDir(string targetDir, string? targetTriple, string profile)
        {
            var profileDirName = ResolveProfileDirectoryName(profile);

            return string.IsNullOrEmpty(targetTriple)
                ? Path.Combine(targetDir, profileDirName)
                : Path.Combine(targetDir, targetTriple!, profileDirName);
        }
    }
}
