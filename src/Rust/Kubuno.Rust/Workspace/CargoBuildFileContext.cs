using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.Build;

namespace Kubuno.VisualStudio.Workspace
{
    /// <summary>
    /// MEF entry point for declaring that every <c>Cargo.toml</c> in an Open Folder workspace
    /// supports Build, Rebuild and Clean (see `docs/ARCHITECTURE.md` phase 1b, and Microsoft's
    /// "Workspace build in Visual Studio" extensibility doc). This provider only produces the
    /// <em>data</em> (a <see cref="FileContext"/> per operation); the actual "run cargo" behavior
    /// is <see cref="CargoBuildFileContextAction"/>, created by the separate
    /// <see cref="CargoBuildFileContextActionProviderFactory"/>.
    ///
    /// No <c>IFileScanner</c>/indexing is used here: unlike CMake or MSBuild, a Cargo manifest
    /// always supports the same three build operations regardless of any configuration, so
    /// there is nothing expensive to precompute - <see cref="CargoBuildFileContextProvider"/>
    /// answers every <c>Cargo.toml</c> query synchronously.
    /// </summary>
    [ExportFileContextProvider(
        PackageGuids.CargoBuildFileContextProviderType,
        new[] { BuildContextTypes.BuildContextType, BuildContextTypes.RebuildContextType, BuildContextTypes.CleanContextType })]
    internal sealed class CargoBuildFileContextProviderFactory : IWorkspaceProviderFactory<IFileContextProvider>
    {
        public IFileContextProvider CreateProvider(Microsoft.VisualStudio.Workspace.IWorkspace workspaceContext) =>
            new CargoBuildFileContextProvider();
    }

    internal sealed class CargoBuildFileContextProvider : IFileContextProvider
    {
        public Task<IReadOnlyCollection<FileContext>> GetContextsForFileAsync(string filePath, CancellationToken cancellationToken)
        {
            if (!IsCargoManifest(filePath))
            {
                return Task.FromResult(FileContext.EmptyFileContexts);
            }

            var inputFiles = new[] { filePath };
            var contexts = new[]
            {
                CreateContext(BuildContextTypes.BuildContextTypeGuid, filePath, inputFiles, "Cargo Build"),
                CreateContext(BuildContextTypes.RebuildContextTypeGuid, filePath, inputFiles, "Cargo Rebuild"),
                CreateContext(BuildContextTypes.CleanContextTypeGuid, filePath, inputFiles, "Cargo Clean"),
            };

            return Task.FromResult<IReadOnlyCollection<FileContext>>(contexts);
        }

        private static FileContext CreateContext(Guid contextType, string filePath, IReadOnlyCollection<string> inputFiles, string displayName)
        {
            // Cargo has no notion of a named build configuration (no "Debug|x86"-style string):
            // pass null, exactly as IBuildConfigurationContext's doc allows for "not applicable".
            var buildContext = new BuildConfigurationContext(buildConfiguration: null);
            return new FileContext(
                PackageGuids.CargoBuildFileContextProviderTypeGuid,
                contextType,
                buildContext,
                inputFiles,
                displayName,
                notifyFileContextChanged: null);
        }

        internal static bool IsCargoManifest(string filePath) =>
            !string.IsNullOrEmpty(filePath) &&
            string.Equals(Path.GetFileName(filePath), Constants.CargoManifestFileName, StringComparison.OrdinalIgnoreCase);
    }
}
