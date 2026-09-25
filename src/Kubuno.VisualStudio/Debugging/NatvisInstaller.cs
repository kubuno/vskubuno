using System;
using System.Collections.Generic;
using System.IO;
using Kubuno.VisualStudio.Logging;

namespace Kubuno.VisualStudio.Debugging
{
    /// <summary>
    /// Makes the active Rust toolchain's standard-library <c>.natvis</c> files
    /// (<see cref="Kubuno.Launch.RustToolchain.FindNatvisFiles"/> - <c>liballoc.natvis</c>,
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
    /// Natvis directory** (`%USERPROFILE%\Documents\Visual Studio 2022\Visualizers` - the
    /// Microsoft Learn doc's own moniker range keeps this exact folder name even under the
    /// "latest" version range, so it is used as-is rather than guessing at a versioned variant
    /// unverified in the docs). Files here apply globally, are auto-discovered with no VS restart,
    /// and can be safely overwritten on every regeneration (see <see cref="EnsureInstalled"/>) to
    /// track whichever toolchain last produced a launch configuration.
    /// </summary>
    internal static class NatvisInstaller
    {
        private const string PersonalVisualizersSubPath = @"Visual Studio 2022\Visualizers";

        public static void EnsureInstalled(IReadOnlyList<string> natvisFiles)
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

                var destinationDirectory = Path.Combine(documents, PersonalVisualizersSubPath);
                Directory.CreateDirectory(destinationDirectory);

                foreach (var sourcePath in natvisFiles)
                {
                    var destinationPath = Path.Combine(destinationDirectory, Path.GetFileName(sourcePath));
                    if (IsUpToDate(sourcePath, destinationPath))
                    {
                        continue;
                    }

                    File.Copy(sourcePath, destinationPath, overwrite: true);
                    KubunoLog.WriteLine($"Kubuno: installed natvis '{destinationPath}' for the native debugger.");
                }
            }
            catch (Exception exception)
            {
                // Natvis is a debugging nicety, not something that should block a build/launch.
                KubunoLog.WriteException("Kubuno: failed to install toolchain natvis files", exception);
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
