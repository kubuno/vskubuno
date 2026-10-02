using System;
using System.IO;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;

namespace Kubuno.Core
{
    /// <summary>
    /// The running Kubuno package, for code the package does not construct - MEF parts (language clients, completion
    /// sources, taggers) are built by the MEF container, possibly before the package has loaded - and that needs its
    /// options pages or its <see cref="JoinableTaskFactory"/>. Set by <see cref="Extensibility.KubunoLayerHost"/> as the
    /// package starts; <see langword="null"/> until then and after it is disposed.
    /// </summary>
    public static class KubunoHost
    {
        /// <summary>The package, once it has started loading.</summary>
        public static AsyncPackage? Package { get; private set; }

        /// <summary>The package's factory when it is loaded, Visual Studio's otherwise.</summary>
        public static JoinableTaskFactory JoinableTaskFactory => Package?.JoinableTaskFactory ?? ThreadHelper.JoinableTaskFactory;

        /// <summary>An options page of the package (loading its values on first use), or <see langword="null"/> before the package loads.</summary>
        public static T? GetDialogPage<T>()
            where T : DialogPage => Package?.GetDialogPage(typeof(T)) as T;

        internal static void Attach(AsyncPackage package) => Package = package;

        internal static void Detach(AsyncPackage package)
        {
            if (ReferenceEquals(Package, package))
            {
                Package = null;
            }
        }
    }

    /// <summary>Where the extension is installed, and the tools it bundles.</summary>
    public static class KubunoExtension
    {
        /// <summary>
        /// The extension's install directory: the folder of the VSIX, where every layer assembly and the
        /// <c>tools\</c> folder live (<see langword="null"/> when it cannot be determined).
        /// </summary>
        public static string? InstallDirectory { get; } = ComputeInstallDirectory();

        /// <summary>
        /// A tool bundled in the VSIX's <c>tools\</c> folder (<paramref name="relativePath"/>, e.g.
        /// <c>kubuno-views-ls.exe</c> or <c>surface\view_embed.exe</c>), or <see langword="null"/> when it is not there.
        /// </summary>
        public static string? FindBundledTool(string relativePath)
        {
            if (InstallDirectory is null)
            {
                return null;
            }

            try
            {
                var path = Path.Combine(InstallDirectory, "tools", relativePath);
                return File.Exists(path) ? path : null;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private static string? ComputeInstallDirectory()
        {
            try
            {
                var location = typeof(KubunoExtension).Assembly.Location;
                return string.IsNullOrEmpty(location) ? null : Path.GetDirectoryName(location);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
