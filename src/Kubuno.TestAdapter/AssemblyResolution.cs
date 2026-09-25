using System;
using System.IO;
using System.Reflection;

namespace Kubuno.TestAdapter
{
    /// <summary>
    /// Works around VSTest's out-of-process discovery/execution host not adding this adapter's own
    /// directory to its assembly-probing path - live-verified in an experimental Visual Studio
    /// instance: test discovery for a real Cargo.toml manifest failed with
    /// <c>FileNotFoundException: Could not load file or assembly 'System.Text.Json, Version=8.0.0.0'</c>
    /// the moment <c>Kubuno.TestAdapter</c> called into <c>Kubuno.Cargo</c> (whose JSON parsing
    /// needs it), even though <c>System.Text.Json.dll</c> and its netstandard2.0 polyfill closure
    /// (<c>System.Buffers</c>/<c>System.Memory</c>/<c>System.Numerics.Vectors</c>/
    /// <c>System.Runtime.CompilerServices.Unsafe</c>/<c>System.Threading.Tasks.Extensions</c>/
    /// <c>System.Text.Encodings.Web</c>) sit right next to <c>Kubuno.TestAdapter.dll</c> in the
    /// deployed VSIX's extension folder - the same set of files
    /// <c>Kubuno.VisualStudio.csproj</c> already has to ship as explicit <c>Content</c> items for
    /// the in-proc VSSDK host, for an analogous but distinct reason (see that file's own comment).
    /// VSTest's UnitTestExtension host, unlike the in-proc VSSDK AppDomain, does not automatically
    /// probe an extension assembly's own directory for its dependencies - it expects either a
    /// self-contained adapter or one that resolves its own dependencies. This installs a normal
    /// <see cref="AppDomain.AssemblyResolve"/> handler (net48, not an <c>AssemblyLoadContext</c> -
    /// this project targets net48, see its csproj comment) that looks in this assembly's own
    /// directory - the standard, documented pattern for VSTest adapters with non-trivial
    /// dependency closures.
    /// </summary>
    internal static class AssemblyResolution
    {
        private static readonly object SyncRoot = new();
        private static bool _installed;

        /// <summary>
        /// Idempotent and safe to call from every entry-point type's static constructor (see
        /// <see cref="Discovery.KubunoTestDiscoverer"/>, <see cref="Execution.KubunoTestExecutor"/>,
        /// <see cref="Containers.KubunoTestContainerDiscoverer"/>) - whichever runs first in a given
        /// host process installs the handler for that process.
        /// </summary>
        public static void EnsureInstalled()
        {
            if (_installed)
            {
                return;
            }

            lock (SyncRoot)
            {
                if (_installed)
                {
                    return;
                }

                AppDomain.CurrentDomain.AssemblyResolve += ResolveFromOwnDirectory;
                _installed = true;
            }
        }

        private static Assembly? ResolveFromOwnDirectory(object? sender, ResolveEventArgs args)
        {
            try
            {
                var ownDirectory = Path.GetDirectoryName(typeof(AssemblyResolution).Assembly.Location);
                if (string.IsNullOrEmpty(ownDirectory))
                {
                    return null;
                }

                // args.Name is a full assembly display name ("System.Text.Json, Version=8.0.0.5, ...");
                // only the simple name is needed to find the file next to this adapter.
                var simpleName = new AssemblyName(args.Name).Name;
                if (string.IsNullOrEmpty(simpleName))
                {
                    return null;
                }

                var candidate = Path.Combine(ownDirectory!, simpleName + ".dll");
                return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
            }
            catch (Exception)
            {
                // An assembly-resolve handler must never throw - the CLR would treat that as a
                // hard failure of the whole resolution pipeline, not just "this handler declined".
                return null;
            }
        }
    }
}
