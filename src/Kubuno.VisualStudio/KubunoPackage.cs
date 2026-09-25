using System;
using System.ComponentModel.Design;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using Kubuno.VisualStudio.Debugging;
using Kubuno.VisualStudio.Infrastructure;
using Kubuno.VisualStudio.Logging;
using Kubuno.VisualStudio.Options;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Workspace.VSIntegration.Contracts;

namespace Kubuno.VisualStudio
{
    /// <summary>
    /// The Kubuno package: registers the Tools &gt; Options &gt; Kubuno &gt; Rust page and wires up
    /// the format-on-save hook. The Rust language client itself (<see cref="LanguageService.RustLanguageClient"/>)
    /// and the content type it targets are separate MEF components, exported independently of this
    /// package and activated by VS when a matching document is opened - they do not need this
    /// package to be loaded first.
    ///
    /// Background-loads on a regular solution, no solution, and - importantly, and easy to miss -
    /// Open Folder (its own dedicated UICONTEXT.FolderOpened, distinct from NoSolution/SolutionExists;
    /// omitting it means the package silently never loads when a folder is opened, which is the
    /// primary scenario here), since the format-on-save hook and the "Kubuno" Output pane (created
    /// here, see KubunoLog.Initialize) should be active as early as possible.
    /// </summary>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration("Kubuno for Visual Studio", "Rust language support (rust-analyzer, TextMate coloring, rustfmt) for Kubuno development.", "1.0")]
    [ProvideAutoLoad(VSConstants.UICONTEXT.NoSolution_string, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.SolutionExists_string, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.FolderOpened_string, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideOptionPage(typeof(RustOptionsPage), Constants.OptionsCategoryName, Constants.OptionsRustPageName, 0, 0, supportsAutomation: true)]
    [ProvideProfile(typeof(RustOptionsPage), Constants.OptionsCategoryName, Constants.OptionsRustPageName, 0, 0, isToolsOptionPage: true)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [Guid(PackageGuidStrings.Package)]
    public sealed class KubunoPackage : AsyncPackage
    {
        private FormatOnSaveDocumentEvents? _formatOnSaveEvents;
        private IVsFolderWorkspaceService? _workspaceService;

        /// <summary>
        /// Set once the package is sited, so MEF components (which are not package-owned and would
        /// otherwise have no way to reach it) can read options via <see cref="Package.GetDialogPage"/>.
        /// </summary>
        internal static KubunoPackage? Instance { get; private set; }

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            Instance = this;

            if (await GetServiceAsync(typeof(SVsOutputWindow)) is IVsOutputWindow outputWindow)
            {
                var paneGuid = PackageGuids.KubunoOutputPane;
                var hr = outputWindow.CreatePane(ref paneGuid, Constants.OutputPaneTitle, fInitVisible: 1, fClearWithSolution: 0);
                if (ErrorHandler.Succeeded(hr) &&
                    ErrorHandler.Succeeded(outputWindow.GetPane(ref paneGuid, out var pane)) &&
                    pane != null)
                {
                    KubunoLog.Initialize(pane);
                }
            }

            var runningDocumentTable = new RunningDocumentTable(this);
            _formatOnSaveEvents = new FormatOnSaveDocumentEvents(
                runningDocumentTable,
                () =>
                {
                    ThreadHelper.ThrowIfNotOnUIThread();
                    return (RustOptionsPage)GetDialogPage(typeof(RustOptionsPage));
                },
                () =>
                {
                    ThreadHelper.ThrowIfNotOnUIThread();
                    return GetService(typeof(DTE)) as DTE;
                });
            _formatOnSaveEvents.Advise();

            if (await GetServiceAsync(typeof(SComponentModel)) is IComponentModel componentModel)
            {
                _workspaceService = componentModel.GetService<IVsFolderWorkspaceService>();
            }

            if (_workspaceService != null)
            {
                _workspaceService.OnActiveWorkspaceChanged += OnActiveWorkspaceChangedAsync;
                // The package can finish loading after a folder is already open (e.g. the user
                // reopens the same folder next session): regenerate for whatever is open right now too.
                await RegenerateLaunchTargetsForCurrentWorkspaceAsync();
            }

            if (await GetServiceAsync(typeof(IMenuCommandService)) is OleMenuCommandService commandService)
            {
                DebugRustTestAtCursorCommand.Initialize(this, commandService);
            }
        }

        private async Task OnActiveWorkspaceChangedAsync(object? sender, EventArgs e) =>
            await RegenerateLaunchTargetsForCurrentWorkspaceAsync();

        /// <summary>
        /// Regenerates <c>.vs\launch.vs.json</c> (see <see cref="RustLaunchTargetsGenerator"/>)
        /// for the folder currently open in Open Folder mode, if any, and if it (or a subfolder)
        /// has a <c>Cargo.toml</c>. Best-effort: a workspace with no Cargo project, or any
        /// failure resolving the toolchain, simply results in no file being written (see the
        /// generator's own try/catch and logging).
        /// </summary>
        private async Task RegenerateLaunchTargetsForCurrentWorkspaceAsync()
        {
            var workspaceRoot = _workspaceService?.CurrentWorkspace?.Location;
            if (string.IsNullOrEmpty(workspaceRoot))
            {
                return;
            }

            var manifestPath = Kubuno.VisualStudio.Core.CargoWorkspaceLocator.FindWorkspaceRoot(workspaceRoot, Directory.Exists, File.Exists);
            if (manifestPath is null)
            {
                return;
            }

            var cargoToml = Path.Combine(manifestPath, Constants.CargoManifestFileName);
            if (!File.Exists(cargoToml))
            {
                return;
            }

            await TaskScheduler.Default;
            await RustLaunchTargetsGenerator.GenerateAsync(manifestPath, cargoToml, CancellationToken.None);
        }

        protected override void Dispose(bool disposing)
        {
            // disposing is false when called from a finalizer, off the UI thread; skip the RDT
            // unadvise in that case rather than let it throw - COM registrations are released by
            // the process exiting anyway when the deterministic Dispose() path was never reached.
            if (disposing && ThreadHelper.CheckAccess())
            {
#pragma warning disable VSTHRD010 // guarded by the ThreadHelper.CheckAccess() check above, not a ThrowIfNotOnUIThread() call the analyzer can see.
                _formatOnSaveEvents?.Dispose();
#pragma warning restore VSTHRD010
                _formatOnSaveEvents = null;

                if (_workspaceService != null)
                {
                    _workspaceService.OnActiveWorkspaceChanged -= OnActiveWorkspaceChangedAsync;
                    _workspaceService = null;
                }

                if (Instance == this)
                {
                    Instance = null;
                }
            }

            base.Dispose(disposing);
        }
    }
}
