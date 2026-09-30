using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace Kubuno.VisualStudio.Commands
{
    /// <summary>
    /// The <c>.rsproj</c> project node currently selected/right-clicked in Solution Explorer, resolved
    /// for the extended "Ajouter" submenu commands (<see cref="AddProjectItemCommands"/>,
    /// <see cref="AddProjectReferenceCommand"/>, <see cref="AddCargoDependencyCommand"/>).
    /// </summary>
    internal sealed class RsprojProjectContext
    {
        public RsprojProjectContext(IVsHierarchy hierarchy, EnvDTE.Project project, string manifestPath, string? packageName)
        {
            Hierarchy = hierarchy;
            Project = project;
            ManifestPath = manifestPath;
            PackageName = packageName;
        }

        public IVsHierarchy Hierarchy { get; }

        public EnvDTE.Project Project { get; }

        /// <summary>Absolute path to this project's own <c>Cargo.toml</c> (its <c>CargoManifestPath</c> MSBuild property, or the sibling <c>Cargo.toml</c> fallback).</summary>
        public string ManifestPath { get; }

        /// <summary>The <c>CargoPackage</c> MSBuild property, when set explicitly (a workspace manifest may need it to disambiguate).</summary>
        public string? PackageName { get; }

        public string ProjectDirectory => Path.GetDirectoryName(ManifestPath) ?? string.Empty;

        /// <summary>
        /// Resolves a context directly from an already-known hierarchy (the Dependencies node's own
        /// context menu, <see cref="Kubuno.VisualStudio.SolutionExplorer.DependenciesNodeCommandTarget"/>,
        /// already has the <see cref="IVsHierarchy"/> it was constructed with - no need to go back
        /// through <see cref="IVsMonitorSelection"/> the way <see cref="RsprojSelection.TryGetCurrent"/> does).
        /// </summary>
        public static RsprojProjectContext? TryCreate(IVsHierarchy hierarchy)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (hierarchy.GetProperty((uint)VSConstants.VSITEMID.Root, (int)__VSHPROPID.VSHPROPID_ExtObject, out var extObject) != VSConstants.S_OK
                || extObject is not EnvDTE.Project project)
            {
                return null;
            }

            var manifest = RsprojSelection.GetBuildProperty(hierarchy, "CargoManifestPath");
            if (string.IsNullOrEmpty(manifest))
            {
                if (hierarchy.GetCanonicalName((uint)VSConstants.VSITEMID.Root, out var projectFile) != VSConstants.S_OK || projectFile is null)
                {
                    return null;
                }

                manifest = Path.Combine(Path.GetDirectoryName(projectFile) ?? string.Empty, "Cargo.toml");
            }

            if (!File.Exists(manifest))
            {
                return null;
            }

            var packageName = RsprojSelection.GetBuildProperty(hierarchy, "CargoPackage");
            return new RsprojProjectContext(hierarchy, project, manifest!, packageName);
        }
    }

    /// <summary>
    /// Resolves <see cref="RsprojProjectContext"/> from <see cref="IVsMonitorSelection"/>'s current
    /// selection - the shell already syncs this to the right-clicked project node before a project
    /// context-menu command fires, the standard VS SDK pattern for a project-node command (DTE's
    /// own <c>SelectedItems</c>, used by <see cref="GenerateRustProjectsCommand"/> for a single
    /// selected *item*, would also work here but ties the caller to EnvDTE for something the shell
    /// already exposes more directly for a *project* node).
    /// </summary>
    internal static class RsprojSelection
    {
        public static RsprojProjectContext? TryGetCurrent()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var hierarchy = TryGetSelectedHierarchy();
            return hierarchy is null ? null : RsprojProjectContext.TryCreate(hierarchy);
        }

        private static IVsHierarchy? TryGetSelectedHierarchy()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (ServiceProvider.GlobalProvider.GetService(typeof(SVsShellMonitorSelection)) is not IVsMonitorSelection monitorSelection)
            {
                return null;
            }

            var hierPtr = IntPtr.Zero;
            var containerPtr = IntPtr.Zero;
            try
            {
                if (ErrorHandler.Failed(monitorSelection.GetCurrentSelection(out hierPtr, out _, out _, out containerPtr)) || hierPtr == IntPtr.Zero)
                {
                    return null;
                }

                return Marshal.GetObjectForIUnknown(hierPtr) as IVsHierarchy;
            }
            finally
            {
                if (hierPtr != IntPtr.Zero)
                {
                    Marshal.Release(hierPtr);
                }

                if (containerPtr != IntPtr.Zero)
                {
                    Marshal.Release(containerPtr);
                }
            }
        }

        /// <summary>Mirrors <c>Kubuno.VisualStudio.SolutionExplorer.CollectionSources.ProjectDependenciesSource.GetProperty</c>.</summary>
        internal static string? GetBuildProperty(IVsHierarchy hierarchy, string name)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return hierarchy is IVsBuildPropertyStorage storage
                && storage.GetPropertyValue(name, null, (uint)_PersistStorageType.PST_PROJECT_FILE, out var value) == VSConstants.S_OK
                && !string.IsNullOrWhiteSpace(value)
                ? value
                : null;
        }
    }
}
