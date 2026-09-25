using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using Kubuno.VisualStudio.Infrastructure;
using Kubuno.VisualStudio.Logging;
using Kubuno.VisualStudio.Options;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

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
    [Guid(PackageGuidStrings.Package)]
    public sealed class KubunoPackage : AsyncPackage
    {
        private FormatOnSaveDocumentEvents? _formatOnSaveEvents;

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
                if (Instance == this)
                {
                    Instance = null;
                }
            }

            base.Dispose(disposing);
        }
    }
}
