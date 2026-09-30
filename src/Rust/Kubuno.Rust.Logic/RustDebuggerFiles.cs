using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Kubuno.VisualStudio.Core
{
    /// <summary>
    /// Keeps the debugger files the extension ships for Rust (docs/DEBUGGING.md) where Visual Studio's native
    /// debugger reads them. Called before every Rust debug launch (the debugger reloads <c>.natjmc</c>/
    /// <c>.natstepfilter</c> files "near the beginning of the debug session") and when the Kubuno debugging
    /// option changes.
    ///
    /// What goes where, and why (each checked against Microsoft Learn's "Create custom views of C++ objects"
    /// and "Just My Code" pages, and live):
    /// - <b>Standard library natvis</b> (<c>String</c>, <c>Vec</c>, <c>Option</c>, <c>HashMap</c>, <c>Rc</c>...):
    ///   nothing to install. On MSVC, rustc links every binary with <c>/NATVIS:</c> for the toolchain's
    ///   <c>lib\rustlib\etc\*.natvis</c>, so they are embedded in each PDB it produces (checked: the PDB of an
    ///   ordinary crate contains <c>intrinsic.natvis</c>/<c>liballoc.natvis</c>/<c>libcore.natvis</c>), always
    ///   matching the toolchain that built it - the first place the debugger looks. The copies an earlier
    ///   version of this extension put into the per-user folder are removed (<see cref="LegacyToolchainNatvis"/>).
    /// - <b>Kubuno natvis</b> (<c>Kubuno.natvis</c>): a <c>NativeVisualizer</c> asset of the VSIX, applied to
    ///   every module (a PDB-embedded natvis would only apply to the types of its own module, and the Kubuno
    ///   types live in <c>kubuno_ui.dll</c> as well as in each application's exe).
    /// - <b>Just My Code and step filters</b> (<c>.natjmc</c>, <c>.natstepfilter</c>): Visual Studio only reads
    ///   them from its installation folder (administrator) or the per-user
    ///   <c>Documents\&lt;Visual Studio version&gt;\Visualizers</c> folder - hence this class. The files are named
    ///   <c>Kubuno.*</c> so nothing else in that folder is ever touched.
    /// </summary>
    public static class RustDebuggerFiles
    {
        /// <summary>The Just My Code and step-filter files for the Rust standard library, always installed.</summary>
        public static readonly IReadOnlyList<string> RustFiles = new[] { "Kubuno.Rust.natjmc", "Kubuno.Rust.natstepfilter" };

        /// <summary>The same for the Kubuno framework, installed while it is treated as external code.</summary>
        public static readonly IReadOnlyList<string> FrameworkFiles = new[] { "Kubuno.Framework.natjmc", "Kubuno.Framework.natstepfilter" };

        /// <summary>
        /// The toolchain natvis files earlier versions copied into the per-user folders, removed on sight when
        /// they are Rust's (see the class remarks): they were redundant with the PDB-embedded copies, and went
        /// stale with every toolchain update.
        /// </summary>
        public static readonly IReadOnlyList<string> LegacyToolchainNatvis = new[] { "intrinsic.natvis", "liballoc.natvis", "libcore.natvis", "libstd.natvis" };

        /// <summary>
        /// The per-user folder names under Documents. Microsoft Learn documents <c>Visual Studio 2022</c> for both
        /// the 2022 and the current (18.x) versions, while Visual Studio 18 creates <c>Visual Studio 18</c> for its
        /// own per-user files: both are written (an unread copy is harmless).
        /// </summary>
        public static readonly IReadOnlyList<string> PersonalVisualizersSubPaths = new[]
        {
            @"Visual Studio 2022\Visualizers",
            @"Visual Studio 18\Visualizers",
        };

        /// <summary>The folder of the VSIX that holds the shipped files (<c>Debugging\Visualizers</c> next to this assembly).</summary>
        public static string ShippedDirectory =>
            Path.Combine(Path.GetDirectoryName(typeof(RustDebuggerFiles).Assembly.Location) ?? string.Empty, "Debugging", "Visualizers");

        /// <summary>The per-user Visualizers folders, or none when Documents cannot be resolved.</summary>
        public static IReadOnlyList<string> PersonalVisualizersDirectories()
        {
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return string.IsNullOrEmpty(documents)
                ? Array.Empty<string>()
                : PersonalVisualizersSubPaths.Select(sub => Path.Combine(documents, sub)).ToArray();
        }

        /// <summary>
        /// Installs the shipped files into the per-user folders. Never throws: debugger niceties must not block a
        /// launch; failures go to <paramref name="log"/>.
        /// </summary>
        public static void EnsureInstalled(bool frameworkIsExternalCode, Action<string>? log = null) =>
            EnsureInstalled(ShippedDirectory, PersonalVisualizersDirectories(), frameworkIsExternalCode, log);

        /// <summary>
        /// Copies <see cref="RustFiles"/> (and <see cref="FrameworkFiles"/> when
        /// <paramref name="frameworkIsExternalCode"/>, otherwise removes them) from <paramref name="sourceDirectory"/>
        /// into each of <paramref name="destinationDirectories"/>, skipping up-to-date copies, and removes the
        /// legacy toolchain natvis copies.
        /// </summary>
        public static void EnsureInstalled(string sourceDirectory, IEnumerable<string> destinationDirectories, bool frameworkIsExternalCode, Action<string>? log = null)
        {
            foreach (var destination in destinationDirectories)
            {
                try
                {
                    Directory.CreateDirectory(destination);
                    foreach (var name in RustFiles)
                    {
                        Copy(sourceDirectory, destination, name, log);
                    }

                    foreach (var name in FrameworkFiles)
                    {
                        if (frameworkIsExternalCode)
                        {
                            Copy(sourceDirectory, destination, name, log);
                        }
                        else
                        {
                            Delete(Path.Combine(destination, name), log);
                        }
                    }

                    foreach (var name in LegacyToolchainNatvis)
                    {
                        var path = Path.Combine(destination, name);
                        if (IsRustToolchainNatvis(path))
                        {
                            Delete(path, log);
                        }
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    log?.Invoke($"Kubuno: could not update the debugger files in '{destination}': {exception.Message}");
                }
            }
        }

        /// <summary>A natvis file of the Rust toolchain (the only kind this class ever deletes by these names).</summary>
        public static bool IsRustToolchainNatvis(string path)
        {
            if (!File.Exists(path))
            {
                return false;
            }

            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return false;
            }

            return text.Contains("http://schemas.microsoft.com/vstudio/debugger/natvis/2010")
                && (text.Contains("alloc::") || text.Contains("core::") || text.Contains("std::collections::hash") || text.Contains("enum2$"));
        }

        private static void Copy(string sourceDirectory, string destinationDirectory, string name, Action<string>? log)
        {
            var source = Path.Combine(sourceDirectory, name);
            if (!File.Exists(source))
            {
                log?.Invoke($"Kubuno: debugger file '{source}' is missing from the extension.");
                return;
            }

            var destination = Path.Combine(destinationDirectory, name);
            if (File.Exists(destination) && FilesEqual(source, destination))
            {
                return;
            }

            File.Copy(source, destination, overwrite: true);
            log?.Invoke($"Kubuno: installed '{destination}' for the native debugger.");
        }

        private static void Delete(string path, Action<string>? log)
        {
            if (!File.Exists(path))
            {
                return;
            }

            File.Delete(path);
            log?.Invoke($"Kubuno: removed '{path}'.");
        }

        private static bool FilesEqual(string a, string b)
        {
            var left = File.ReadAllBytes(a);
            var right = File.ReadAllBytes(b);
            return left.Length == right.Length && left.SequenceEqual(right);
        }
    }
}
