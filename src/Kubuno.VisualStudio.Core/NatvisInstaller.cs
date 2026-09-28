using System;
using System.Collections.Generic;
using System.IO;

namespace Kubuno.VisualStudio.Core
{
    /// <summary>
    /// Makes the active Rust toolchain's standard-library <c>.natvis</c> files
    /// (<c>Kubuno.Launch.RustToolchain.FindNatvisFiles</c> - <c>liballoc.natvis</c>,
    /// <c>libcore.natvis</c>, <c>libstd.natvis</c>, <c>intrinsic.natvis</c> on a current stable
    /// toolchain) visible to Visual Studio's native debugger, so <c>String</c>/<c>Vec</c>/
    /// <c>Option</c>/... show a readable value instead of raw struct fields.
    ///
    /// Evaluated and rejected alternatives (see the phase 1c report for the full reasoning):
    /// - **PDB-embedded natvis** (the debugger's first, highest-priority source): rustc does not
    ///   embed the standard library's `.natvis` into a crate's own PDB - libstd is prebuilt and
    ///   distributed separately by rustup, so this alone leaves `std` types unvisualized.
    /// - **VSIX-registered natvis** (`&lt;Asset Type="NativeVisualizer"&gt;` in
    ///   source.extension.vsixmanifest, per Microsoft's "Create custom views of native objects"
    ///   doc): requires the `.natvis` file to be a build-time-known asset inside this repo. The
    ///   real files are toolchain/version-specific and live on each developer's machine (rustup
    ///   sysroot), so vendoring a copy here would go stale and diverge from whatever `rustc`
    ///   version is actually in use.
    /// This leaves the third, lowest-priority but simplest documented source: the **per-user
    /// Natvis directory**. Files here apply globally, are auto-discovered with no VS restart, and
    /// can be safely overwritten on every regeneration (see <see cref="EnsureInstalled"/>) to
    /// track whichever toolchain last produced a launch configuration.
    ///
    /// **Which folder name VS 18 (2026) actually reads was left unresolved after a live
    /// investigation** (see CHANGELOG.md): the current Microsoft Learn doc
    /// ("create-custom-views-of-native-objects", moniker range covering the latest/2026 version,
    /// last updated 2026-08) still gives <c>...\Visual Studio 2022\Visualizers</c> as the example
    /// path even under the "latest" moniker, suggesting the folder name did not change - but a
    /// live F5 test with a marker `.natvis` file per candidate folder was inconclusive (no
    /// startup item was selected, so the debugger never actually launched) and was not repeated,
    /// to stop burning time on a question the docs already lean one way on. Rather than gamble on
    /// a single folder name, <see cref="EnsureInstalled"/> installs into **both**
    /// <c>Documents\Visual Studio 2022\Visualizers</c> and <c>Documents\Visual Studio 18\Visualizers</c>
    /// - installing into a folder VS does not read is harmless (an unused file), while installing
    /// into only the wrong one silently breaks natvis for every VS 18 user. Revisit once someone
    /// can actually confirm which one loads (Natvis diagnostic messages set to Verbose, Tools >
    /// Options > Debugging > General, then read at a real breakpoint) and drop the other.
    /// </summary>
    public static class NatvisInstaller
    {
        private static readonly string[] PersonalVisualizersSubPaths =
        {
            @"Visual Studio 2022\Visualizers",
            @"Visual Studio 18\Visualizers",
        };

        /// <param name="natvisFiles">The toolchain's natvis files.</param>
        /// <param name="log">Receives one line per installed file or failure; <see langword="null"/> to stay silent.</param>
        public static void EnsureInstalled(IReadOnlyList<string> natvisFiles, Action<string>? log = null)
        {
            if (natvisFiles.Count == 0)
            {
                return;
            }

            try
            {
                var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (string.IsNullOrEmpty(documents))
                {
                    return;
                }

                foreach (var subPath in PersonalVisualizersSubPaths)
                {
                    InstallInto(Path.Combine(documents, subPath), natvisFiles, log);
                }
            }
            catch (Exception exception)
            {
                // Natvis is a debugging nicety, not something that should block a build/launch.
                log?.Invoke($"Kubuno: failed to install toolchain natvis files: {exception}");
            }
        }

        /// <summary>
        /// Idempotent for a single destination directory - <see cref="IsUpToDate"/> skips a file
        /// that is already an up-to-date copy, so calling this (twice, once per candidate folder)
        /// on every launch-target regeneration is cheap once both folders are populated.
        /// </summary>
        private static void InstallInto(string destinationDirectory, IReadOnlyList<string> natvisFiles, Action<string>? log)
        {
            Directory.CreateDirectory(destinationDirectory);

            foreach (var sourcePath in natvisFiles)
            {
                var destinationPath = Path.Combine(destinationDirectory, Path.GetFileName(sourcePath));
                if (IsUpToDate(sourcePath, destinationPath))
                {
                    continue;
                }

                File.Copy(sourcePath, destinationPath, overwrite: true);
                log?.Invoke($"Kubuno: installed natvis '{destinationPath}' for the native debugger.");
            }
        }

        private static bool IsUpToDate(string sourcePath, string destinationPath)
        {
            if (!File.Exists(destinationPath))
            {
                return false;
            }

            var source = new FileInfo(sourcePath);
            var destination = new FileInfo(destinationPath);
            return source.Length == destination.Length && source.LastWriteTimeUtc <= destination.LastWriteTimeUtc;
        }
    }
}
