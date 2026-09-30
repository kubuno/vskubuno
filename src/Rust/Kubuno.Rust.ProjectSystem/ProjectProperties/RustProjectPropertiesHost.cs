using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace Kubuno.VisualStudio.RustProjectSystem.ProjectProperties
{
    /// <summary>
    /// UIs of the Kubuno package (Kubuno.VisualStudio, which references this assembly - not the other way
    /// round) that Project Properties links open: the crate manager and the Reference Manager of lot 10. The
    /// package fills these in when it initializes; <see cref="EnsurePackageLoadedAsync"/> loads it first when a
    /// link is clicked before anything else did.
    /// </summary>
    public static class RustProjectPropertiesHost
    {
        /// <summary>Kubuno.VisualStudio's KubunoPackage GUID (PackageGuidStrings.Package - keep in sync).</summary>
        private static readonly Guid KubunoPackageGuid = new Guid("5f3a9b2e-9e0c-4f0a-9a7a-6f0f3c9c9d10");

        // The callbacks take the project's IVsHierarchy as object: Kubuno.VisualStudio compiles against the
        // Visual Studio SDK package's interop assemblies, this assembly against the running Visual Studio's.

        /// <summary>Opens the crate manager (NuGet-like) for the project.</summary>
        public static Func<object, Task>? OpenCrateManagerAsync { get; set; }

        /// <summary>Opens the Reference Manager (project references = path dependencies) for the project.</summary>
        public static Func<object, Task>? OpenReferenceManagerAsync { get; set; }

        /// <summary>Loads the Kubuno package (which sets the callbacks above) if it is not loaded yet.</summary>
        public static async Task EnsurePackageLoadedAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (OpenCrateManagerAsync != null && OpenReferenceManagerAsync != null)
            {
                return;
            }

            if (ServiceProvider.GlobalProvider.GetService(typeof(SVsShell)) is IVsShell shell)
            {
                // Returns the already-loaded instance when there is one.
                Guid guid = KubunoPackageGuid;
                ErrorHandler.ThrowOnFailure(shell.LoadPackage(ref guid, out _));
            }
        }
    }
}
