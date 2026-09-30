using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Rust.TestAdapter.Containers;
using Kubuno.Core.Logging;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.VSIntegration.Contracts;

namespace Kubuno.Rust.Workspace
{
    /// <summary>
    /// The real, VS-backed implementation of <c>Kubuno.Rust.TestAdapter.Containers.ICargoWorkspaceSource</c>
    /// (see <c>src/Rust/Kubuno.Rust.TestAdapter/INTEGRATION.md</c> &sect;3.3) - the seam that lets
    /// <c>KubunoTestContainerDiscoverer</c> (MEF-composed by Visual Studio itself) learn which
    /// <c>Cargo.toml</c> manifests exist in the current Open Folder workspace, and when that set
    /// might have changed, without Kubuno.Rust.TestAdapter referencing any Visual Studio service
    /// directly.
    ///
    /// Manifest enumeration uses <see cref="IWorkspace4.GetFilesAsync(string, bool, string, CancellationToken)"/>
    /// (a pattern-filtered recursive listing added on top of the base <c>IWorkspace.GetFilesAsync</c>
    /// overload, which has no search-pattern parameter) rather than an <c>IFileFinder</c>/
    /// <c>GetFileFinder()</c> API: that shape does not exist in
    /// <c>Microsoft.VisualStudio.Workspace</c> 17.12.19 (the version this VSIX already pins - see
    /// <c>Kubuno.VisualStudio.csproj</c>), verified by reflecting over the installed package
    /// assembly. <c>IWorkspace4</c> is what actually ships the recursive, pattern-filtered file
    /// listing this needs.
    ///
    /// Change notification combines two sources: <see cref="IVsFolderWorkspaceService.OnActiveWorkspaceChanged"/>
    /// (workspace opened/closed/reopened - the same event <see cref="KubunoPackage"/> already
    /// subscribes to for launch-target regeneration) and, for edits within an already-open
    /// workspace, <see cref="IFileWatcherService.OnFileSystemChanged"/> filtered to files named
    /// <c>Cargo.toml</c> (add/remove/edit all matter - see INTEGRATION.md &sect;3.3).
    /// </summary>
    [Export(typeof(ICargoWorkspaceSource))]
    internal sealed class CargoWorkspaceSource : ICargoWorkspaceSource, IDisposable
    {
        private readonly IVsFolderWorkspaceService? _workspaceService;
        private IFileWatcherService? _fileWatcherService;
        private bool _disposed;

        [ImportingConstructor]
        public CargoWorkspaceSource([Import(typeof(SVsServiceProvider))] IServiceProvider serviceProvider)
        {
            // VS's MEF composition does not guarantee ITestContainerDiscoverer construction happens
            // on the UI thread; switch explicitly rather than assume it (unlike KubunoPackage.InitializeAsync,
            // which already runs post-SwitchToMainThreadAsync).
            _workspaceService = ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                if (serviceProvider?.GetService(typeof(SComponentModel)) is IComponentModel componentModel)
                {
                    return componentModel.GetService<IVsFolderWorkspaceService>();
                }

                return null;
            });

            if (_workspaceService != null)
            {
                _workspaceService.OnActiveWorkspaceChanged += OnActiveWorkspaceChangedAsync;
                HookFileWatcher(_workspaceService.CurrentWorkspace);
            }
        }

        public event EventHandler? Changed;

        public IReadOnlyList<string> GetManifestPaths()
        {
            var workspace = _workspaceService?.CurrentWorkspace;
            if (workspace is not IWorkspace4 workspace4)
            {
                return Array.Empty<string>();
            }

            try
            {
                return ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    var results = new List<string>();
                    // No .ConfigureAwait(false): Microsoft.VisualStudio.Threading's own AwaitExtensions.ConfigureAwait
                    // overload shadows System.Runtime.CompilerServices.TaskAsyncEnumerableExtensions.ConfigureAwait
                    // for an IAsyncEnumerable<T> receiver here (both namespaces are in scope), which breaks
                    // `await foreach`'s GetAsyncEnumerator() lookup - this whole call already runs inside
                    // JoinableTaskFactory.Run, so the continuation context does not need pinning anyway.
                    await foreach (var file in workspace4.GetFilesAsync(
                        string.Empty, recursive: true, Constants.CargoManifestFileName, CancellationToken.None))
                    {
                        results.Add(file);
                    }

                    return (IReadOnlyList<string>)results;
                });
            }
            catch (Exception exception)
            {
                KubunoLog.WriteException("Kubuno: failed to enumerate Cargo.toml manifests for Test Explorer", exception);
                return Array.Empty<string>();
            }
        }

        private async System.Threading.Tasks.Task OnActiveWorkspaceChangedAsync(object? sender, EventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            HookFileWatcher(_workspaceService?.CurrentWorkspace);
            RaiseChanged();
        }

        private void HookFileWatcher(Microsoft.VisualStudio.Workspace.IWorkspace? workspace)
        {
            UnhookFileWatcher();

            if (workspace is null)
            {
                return;
            }

            try
            {
                _fileWatcherService = WorkspaceServiceHelper.GetFileWatcherService(workspace);
                if (_fileWatcherService != null)
                {
                    _fileWatcherService.OnFileSystemChanged += OnFileSystemChangedAsync;
                }
            }
            catch (Exception exception)
            {
                // Best-effort: without this, Test Explorer simply misses a hand-edited Cargo.toml
                // until the next workspace reopen (OnActiveWorkspaceChangedAsync still fires then).
                KubunoLog.WriteException("Kubuno: failed to watch Cargo.toml changes for Test Explorer", exception);
            }
        }

        private void UnhookFileWatcher()
        {
            if (_fileWatcherService != null)
            {
                _fileWatcherService.OnFileSystemChanged -= OnFileSystemChangedAsync;
                _fileWatcherService = null;
            }
        }

        private System.Threading.Tasks.Task OnFileSystemChangedAsync(object? sender, FileSystemEventArgs e)
        {
            var name = e.Name ?? e.FullPath;
            if (!string.IsNullOrEmpty(name) &&
                string.Equals(Path.GetFileName(name), Constants.CargoManifestFileName, StringComparison.OrdinalIgnoreCase))
            {
                RaiseChanged();
            }

            return System.Threading.Tasks.Task.CompletedTask;
        }

        private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            UnhookFileWatcher();

            if (_workspaceService != null)
            {
                _workspaceService.OnActiveWorkspaceChanged -= OnActiveWorkspaceChangedAsync;
            }
        }
    }

    /// <summary>
    /// Creates a <see cref="CargoFolderActivation"/> for every Open Folder workspace, as soon as it is initialized.
    /// </summary>
    [ExportWorkspaceServiceFactory(WorkspaceServiceFactoryOptions.CreateOnWorkspaceInitialize | WorkspaceServiceFactoryOptions.IsCreateAsync, typeof(CargoFolderActivation))]
    internal sealed class CargoFolderActivationFactory : IWorkspaceServiceFactory, IAsyncWorkspaceServiceFactory
    {
        // The export's contract is IWorkspaceServiceFactory, whatever the options: without it, MEF rejects this part
        // and, with it, the whole workspace service-factory import (found live - IVsFolderWorkspaceService then
        // disappears for every consumer, rust-analyzer's language client included). IsCreateAsync makes the
        // workspace call CreateServiceAsync instead; this synchronous entry point is only a fallback.
        public object? CreateService(IWorkspace workspaceContext) =>
            ThreadHelper.JoinableTaskFactory.Run(() => CreateServiceAsync(workspaceContext));

        public async System.Threading.Tasks.Task<object?> CreateServiceAsync(IWorkspace workspaceContext)
        {
            var activation = new CargoFolderActivation(workspaceContext?.Location);
            await activation.ApplyAsync();
            return activation;
        }
    }

    /// <summary>
    /// Turns <see cref="PackageGuids.CargoFolderUIContext"/> on while the open folder is a Cargo workspace, which
    /// is what loads <see cref="KubunoPackage"/> in Open Folder mode (its activation rule, see
    /// <see cref="PackageGuids.KubunoActivationUIContextString"/>): UI context rules can test a project's
    /// capabilities or the active editor, not what a folder contains. A handful of <c>File.Exists</c> calls on a
    /// background thread, then one UI context update - nothing else, so opening any other folder stays free.
    /// </summary>
    internal sealed class CargoFolderActivation
    {
        private readonly string? _root;

        public CargoFolderActivation(string? root) => _root = root;

        public bool IsCargo { get; private set; }

        public async System.Threading.Tasks.Task ApplyAsync()
        {
            await TaskScheduler.Default;
            IsCargo = IsCargoFolder(_root);
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            UIContext.FromUIContextGuid(PackageGuids.CargoFolderUIContext).IsActive = IsCargo;
        }

        /// <summary>See <see cref="Kubuno.Rust.Logic.CargoWorkspaceLocator.IsRustFolder"/>; an unreadable folder is not a Rust one.</summary>
        internal static bool IsCargoFolder(string? root)
        {
            try
            {
                return Kubuno.Rust.Logic.CargoWorkspaceLocator.IsRustFolder(
                    root,
                    File.Exists,
                    folder => Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly),
                    folder => Directory.EnumerateDirectories(folder));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
            {
                return false;
            }
        }
    }
}
