using System;
using System.ComponentModel.Design;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using Kubuno.VisualStudio.Commands;
using Kubuno.VisualStudio.Core;
using Kubuno.VisualStudio.Debugging;
using Kubuno.VisualStudio.DesignerIntegration;
using Kubuno.VisualStudio.Infrastructure;
using Kubuno.VisualStudio.Logging;
using Kubuno.VisualStudio.Options;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Workspace.VSIntegration.Contracts;

// Code bases for assemblies that Visual Studio resolves by NAME rather than by path, which cannot see an
// extension folder otherwise:
// - Kubuno.VisualStudio.RustProjectSystem: RustProject.imagemanifest (the .rsproj project node icon)
//   points at its WPF resources by assembly name, loaded by the image service with Assembly.Load;
// - its own dependencies (Kubuno.Launch, Kubuno.VisualStudio.Core, Kubuno.Cargo): Visual Studio's MEF
//   loads the CPS exports (RustDebugLaunchProvider) before this package ever runs, so nothing else has
//   loaded them yet - verified live: F5 failed with "Could not load file or assembly 'Kubuno.Launch'".
[assembly: ProvideCodeBase(AssemblyName = "Kubuno.VisualStudio.RustProjectSystem", CodeBase = @"$PackageFolder$\Kubuno.VisualStudio.RustProjectSystem.dll")]
// Kubuno.Mcp.Bridge (docs/MCP.md "Integration"): KubunoPackage.StartMcpBridgeAsync references it
// directly - same latent gap as the assemblies above, just not hit until this method actually runs
// (verified live: "SetSite failed for package [KubunoPackage]" with a Kubuno.Mcp.Bridge
// FileNotFoundException before this was added).
[assembly: ProvideCodeBase(AssemblyName = "Kubuno.Mcp.Bridge", CodeBase = @"$PackageFolder$\Kubuno.Mcp.Bridge.dll")]
// Deliberately NOT self-registering Kubuno.VisualStudio.dll (this package's own hosting assembly)
// the same way: tried during docs/RSPROJ.md Addendum (lot 7) work to fix
// Microsoft.VisualStudio.TemplateWizard.Wizard.CreateManagedInstance's plain Assembly.Load of it (a
// "Create a new project" wizard names its own assembly, and that loader could not otherwise see this
// VSIX's private folder) - verified live, reproducibly, that a self-referential codeBase entry here
// instead broke this package's OWN load ("SetSite failed for package [KubunoPackage]" again, this
// time from Visual Studio loading a second, differently-probed copy of its own hosting assembly).
// Fixed properly in a follow-up pass with a SEPARATE, small wizard assembly instead of reusing this
// package's own (Kubuno.VisualStudio.TemplateWizard - see its own csproj header): registering ITS
// name below is not self-referential, so it does not reproduce the failure above.
[assembly: ProvideCodeBase(AssemblyName = "Kubuno.VisualStudio.TemplateWizard", CodeBase = @"$PackageFolder$\Kubuno.VisualStudio.TemplateWizard.dll")]
[assembly: ProvideCodeBase(AssemblyName = "Kubuno.Launch", CodeBase = @"$PackageFolder$\Kubuno.Launch.dll")]
[assembly: ProvideCodeBase(AssemblyName = "Kubuno.VisualStudio.Core", CodeBase = @"$PackageFolder$\Kubuno.VisualStudio.Core.dll")]
[assembly: ProvideCodeBase(AssemblyName = "Kubuno.Cargo", CodeBase = @"$PackageFolder$\Kubuno.Cargo.dll")]

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
    [ProvideOptionPage(typeof(Kubuno.VisualStudio.Views.Options.KbviewOptionsPage), Constants.OptionsCategoryName, "Views", 0, 0, supportsAutomation: true)]
    [ProvideProfile(typeof(Kubuno.VisualStudio.Views.Options.KbviewOptionsPage), Constants.OptionsCategoryName, "Views", 0, 0, isToolsOptionPage: true)]
    // Kubuno.VisualStudio.Designer's own INTEGRATION.md §3: the split Design|XML editor for .kbview
    // files, registered alongside - never instead of - languages.pkgdef's plain core text editor
    // (that pkgdef entry's own comment: "the HIGHEST value wins the double-click default", 0x64 there
    // vs. DesignerConstants.EditorExtensionPriority's 0x60 here, so the plain editor stays default).
    [ProvideEditorFactory(typeof(Kubuno.VisualStudio.Designer.EditorFactory.KbviewEditorFactory), 110)]
    // Fixed 2026-09-28 (docs/RSPROJ.md work package 6): these two GUIDs were swapped one slot too far
    // (7651a703/7651a704, actually LOGVIEWID_TextView/LOGVIEWID_UserChooseView) - VSConstants.LOGVIEWID_Designer
    // is really {...a702...}, verified live by reflecting Microsoft.VisualStudio.Shell.15.0.dll's
    // VSConstants fields. The mislabeled registration meant "Open With -> Kubuno View Designer" never
    // actually reached KbviewEditorFactory for the real Designer logical view (confirmed live: even a
    // never-opened .kbview opened with IVsUIShellOpenDocument.OpenSpecificEditor against the true
    // LOGVIEWID_Designer fell back to the plain text editor before this fix).
    [ProvideEditorLogicalView(typeof(Kubuno.VisualStudio.Designer.EditorFactory.KbviewEditorFactory), "{7651a702-06e5-11d1-8ebd-00a0c90f26ea}")] // LOGVIEWID_Designer
    [ProvideEditorLogicalView(typeof(Kubuno.VisualStudio.Designer.EditorFactory.KbviewEditorFactory), "{7651a703-06e5-11d1-8ebd-00a0c90f26ea}")] // LOGVIEWID_TextView
    [ProvideEditorExtension(typeof(Kubuno.VisualStudio.Designer.EditorFactory.KbviewEditorFactory), Kubuno.VisualStudio.Views.KbviewConstants.FileExtension, Kubuno.VisualStudio.Designer.DesignerConstants.EditorExtensionPriority)]
    [ProvideOptionPage(typeof(Kubuno.VisualStudio.Designer.Options.KbviewDesignerOptionsPage), Constants.OptionsCategoryName, Kubuno.VisualStudio.Designer.DesignerConstants.OptionsPageName, 0, 0, supportsAutomation: true)]
    [ProvideProfile(typeof(Kubuno.VisualStudio.Designer.Options.KbviewDesignerOptionsPage), Constants.OptionsCategoryName, Kubuno.VisualStudio.Designer.DesignerConstants.OptionsPageName, 0, 0, isToolsOptionPage: true)]
    // Kubuno.VisualStudio.Designer's own INTEGRATION.md §7: the Toolbox/Properties tool windows, shown
    // via "View > Other Windows > Kubuno Toolbox/Properties" (KubunoCommands.vsct).
    [ProvideToolWindow(typeof(Kubuno.VisualStudio.Designer.ToolWindows.ToolboxToolWindow))]
    [ProvideToolWindow(typeof(Kubuno.VisualStudio.Designer.ToolWindows.PropertiesToolWindow))]
    [ProvideToolWindow(typeof(Kubuno.VisualStudio.Designer.ToolWindows.OutlineToolWindow))]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [Guid(PackageGuidStrings.Package)]
    public sealed class KubunoPackage : AsyncPackage
    {
        private FormatOnSaveDocumentEvents? _formatOnSaveEvents;
        private IVsFolderWorkspaceService? _workspaceService;
        private Kubuno.Mcp.Bridge.PipeProtocol.VsMcpBridgeHost? _mcpBridgeHost;

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

            // Kubuno.VisualStudio.Views (the .kbview language client) never references
            // Kubuno.VisualStudio.Logging.KubunoLog directly (that would be a circular reference -
            // see that library's Logging\IKubunoLog.cs remarks and its own INTEGRATION.md &sect;5);
            // this adapter is the seam instead. Must happen before any .kbview document can open,
            // so it runs unconditionally here rather than being deferred.
            Kubuno.VisualStudio.Views.Logging.KubunoViewsLogHost.Current = new KubunoLogAdapter();
            Kubuno.VisualStudio.Views.Options.KubunoViewsOptionsHost.Current =
                (Kubuno.VisualStudio.Views.Options.KbviewOptionsPage)GetDialogPage(typeof(Kubuno.VisualStudio.Views.Options.KbviewOptionsPage));

            // Kubuno.VisualStudio.Designer's own INTEGRATION.md §3/§4/§6: the split Design|XML editor
            // factory (a classic, package-registered IVsEditorFactory - [ProvideEditorFactory] only
            // emits pkgdef metadata, VS still needs a live instance handed to it via RegisterEditorFactory),
            // its options page's static gateway, and the DSG-7 design surface factory. Must happen
            // before any .kbview document can open through "Open With... > Kubuno View Designer", so
            // this runs unconditionally here, mirroring the Views language client's own logging/options
            // wiring immediately above.
            RegisterEditorFactory(new Kubuno.VisualStudio.Designer.EditorFactory.KbviewEditorFactory());
            Kubuno.VisualStudio.Designer.Options.DesignerOptionsHost.Current =
                (Kubuno.VisualStudio.Designer.Options.KbviewDesignerOptionsPage)GetDialogPage(typeof(Kubuno.VisualStudio.Designer.Options.KbviewDesignerOptionsPage));

            var extensionInstallDirectory = GetExtensionInstallDirectory();
            RustSdkFeedInstaller.EnsureRegistered(extensionInstallDirectory);
            PreloadTemplateWizardAssembly();
            var surfaceExePath = KubunoViewsSurfaceLocator.Locate(extensionInstallDirectory, devBuildDirectory: @"C:\kubuno-build\agent-dsgint\release\examples");
            if (surfaceExePath is not null)
            {
                var oleServiceProvider = (Microsoft.VisualStudio.OLE.Interop.IServiceProvider)this;
                Kubuno.VisualStudio.Designer.DesignSurface.DesignSurfaceHostFactoryHost.Current =
                    new Kubuno.VisualStudio.Designer.DesignSurface.RustDesignSurfaceHostFactory(surfaceExePath, oleServiceProvider: oleServiceProvider);
                KubunoLog.WriteLine($"Kubuno: design surface exe resolved at '{surfaceExePath}'.");
            }
            else
            {
                KubunoLog.WriteLine("Kubuno: kubuno-views-surface.exe not found - the Kubuno View Designer's Design pane will show its placeholder. Build it: cd Z:\\projects\\kubuno\\desktop\\windows ; $env:CARGO_TARGET_DIR='C:\\kubuno-build\\agent-dsgint' ; cargo build --release --example view_embed -p kubuno-views -j 1");
            }

            // Start the MCP bridge (docs/MCP.md "Integration"): async, off the UI thread's critical
            // path, and never allowed to fail package load - a developer not using Claude Code, or
            // a bridge that fails to bind its pipe/write its discovery file, must not affect Rust/
            // Cargo/Views functionality at all. StartAsync's own try/catch logs to the "Kubuno" pane.
            var dte = await GetServiceAsync(typeof(SDTE)) as DTE2;
            JoinableTaskFactory.RunAsync(() => StartMcpBridgeAsync(dte)).FileAndForget("Kubuno/McpBridge/Start");

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
                await EnsureWorkspaceSettingsExcludeNonRustProjectsAsync();
            }

            if (await GetServiceAsync(typeof(IMenuCommandService)) is OleMenuCommandService commandService)
            {
                DebugRustTestAtCursorCommand.Initialize(this, commandService);
                DesignerToolWindowCommands.Initialize(this, commandService);
                GenerateRustProjectsCommand.Initialize(this, commandService);
            }
        }

        private async Task OnActiveWorkspaceChangedAsync(object? sender, EventArgs e)
        {
            await RegenerateLaunchTargetsForCurrentWorkspaceAsync();
            await EnsureWorkspaceSettingsExcludeNonRustProjectsAsync();
        }

        /// <summary>
        /// The directory this VSIX's own assembly is loaded from - the extension's install directory
        /// once shipped, where the <c>tools\surface\kubuno-views-surface.exe</c> candidate is rooted
        /// (see <see cref="KubunoViewsSurfaceLocator"/>). Same shape as
        /// <c>Kubuno.VisualStudio.Views.LanguageService.KubunoViewsLanguageClient</c>'s own private
        /// helper of the same name (that library cannot share this one - see its own csproj comment).
        /// </summary>
        private static string? GetExtensionInstallDirectory()
        {
            try
            {
                var location = System.Reflection.Assembly.GetExecutingAssembly().Location;
                return string.IsNullOrEmpty(location) ? null : Path.GetDirectoryName(location);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Forces Kubuno.VisualStudio.TemplateWizard.dll into this AppDomain's assembly cache before
        /// any "Create a new project"/"Add New Item" wizard can run. docs/RSPROJ.md Addendum (lot 7),
        /// "Crate-name casing, revisited": its own ProvideCodeBase registration (see the
        /// [assembly: ProvideCodeBase] attributes above) makes Visual Studio's package/MEF loaders
        /// resolve that assembly correctly, but live-verified NOT to be consulted by
        /// Microsoft.VisualStudio.TemplateWizard.Wizard.CreateManagedInstance's own plain
        /// <c>Assembly.Load(AssemblyName)</c> call (still a FileNotFoundException there even with the
        /// registration in place) - a separate, undocumented resolution path. Loading it here once,
        /// eagerly, at package initialization, sidesteps that entirely: once an assembly of a given
        /// identity is already loaded into the AppDomain, the CLR's own assembly-identity cache
        /// satisfies any later <c>Assembly.Load</c> for the same identity without re-resolving it,
        /// regardless of which subsystem asks. Never allowed to fail package load - a wizard that
        /// cannot compute <c>$cratename$</c> still leaves every template usable via VS's own
        /// <c>$safeprojectname$</c> fallback (see each .vstemplate's own remarks).
        /// </summary>
        private static void PreloadTemplateWizardAssembly()
        {
            try
            {
                _ = typeof(Kubuno.VisualStudio.TemplateWizard.CrateNameWizard).Assembly.GetName();
            }
            catch (Exception ex)
            {
                KubunoLog.WriteLine($"Kubuno: could not preload Kubuno.VisualStudio.TemplateWizard.dll - $cratename$/$moduleid$ template substitution will not be available ({ex.Message}).");
            }
        }

        /// <summary>
        /// Starts the MCP bridge (see docs/MCP.md "Integration"): the net48 leg of
        /// Kubuno.Mcp.Bridge, loaded in-proc here, hosts a named pipe that <c>kubuno-vs-mcp.exe</c>
        /// (started independently by Claude Code, outside this process - see docs/MCP.md) connects
        /// to. Called fire-and-forget from <see cref="InitializeAsync"/> via <c>JoinableTaskFactory.RunAsync(...).FileAndForget(...)</c>,
        /// so a slow or failing bridge start never delays package load; every failure is caught and
        /// logged to the "Kubuno" Output pane rather than surfaced as an exception - read-only VS
        /// context for Claude is a convenience, never something Rust/Cargo/Views functionality
        /// should depend on being available.
        /// </summary>
        private async Task StartMcpBridgeAsync(DTE2? dte)
        {
            // Explicit switch (rather than ThreadHelper.ThrowIfNotOnUIThread()) per VSTHRD109:
            // this is a Task-returning method, so it must switch to the thread it needs instead of
            // asserting it is already there. In practice this is a no-op resume: InitializeAsync
            // calls this via JoinableTaskFactory.RunAsync(() => StartMcpBridgeAsync(dte)) while
            // already on the main thread.
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                if (dte is null)
                {
                    KubunoLog.WriteLine("Kubuno: MCP bridge not started - the DTE service is unavailable.");
                    return;
                }

                var provider = new Kubuno.Mcp.Bridge.Dte.DteVsContextProvider(dte);
                var host = new Kubuno.Mcp.Bridge.PipeProtocol.VsMcpBridgeHost(provider);
                host.Start(visualStudioVersion: dte.Version, solutionOrFolderPath: dte.Solution?.FullName);
                _mcpBridgeHost = host;
                KubunoLog.WriteLine($"Kubuno: MCP bridge started (pipe '{host.PipeName}').");
            }
            catch (Exception exception)
            {
                KubunoLog.WriteException("Kubuno: failed to start the MCP bridge", exception);
            }
        }

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
            await RustLaunchTargetsGenerator.GenerateAsync(manifestPath, cargoToml, CancellationToken.None, _workspaceService?.CurrentWorkspace);
        }

        /// <summary>
        /// Creates <c>VSWorkspaceSettings.json</c> at the Open Folder workspace root, excluding any
        /// directory that contains a non-Rust project/solution file
        /// (<see cref="NonRustProjectExclusionScanner"/>) - confirmed live that VS's own native
        /// project-file discovery, once it finds even one such file anywhere in the tree, replaces
        /// every Cargo bin/example target <c>launch.vs.json</c> offers in the "Select Startup Item"
        /// dropdown with just that file (plus "Active document"). Only when the file does not
        /// already exist: this is the developer's own file (source-controllable, human-editable),
        /// never overwritten - the same "never override an existing choice" posture
        /// <c>RustLaunchTargetsGenerator.EnsureStartupItemSelectedAsync</c> takes for
        /// <c>ProjectSettings.json</c>. Best-effort, like every workspace-open step in this package:
        /// a workspace with no Cargo project, or any I/O failure, simply results in no file being
        /// written.
        /// </summary>
        private async Task EnsureWorkspaceSettingsExcludeNonRustProjectsAsync()
        {
            var workspaceRoot = _workspaceService?.CurrentWorkspace?.Location;
            // Not string.IsNullOrEmpty: .NET Framework's copy carries no [NotNullWhen(false)], so the
            // compiler would still flag workspaceRoot as maybe-null below (CS8604).
            if (workspaceRoot is null || workspaceRoot.Length == 0)
            {
                return;
            }

            var settingsPath = Path.Combine(workspaceRoot, "VSWorkspaceSettings.json");

            await TaskScheduler.Default;

            try
            {
                if (File.Exists(settingsPath))
                {
                    return;
                }

                var allFiles = Directory.EnumerateFiles(workspaceRoot, "*", SearchOption.AllDirectories);
                var cargoManifests = Directory.EnumerateFiles(workspaceRoot, Constants.CargoManifestFileName, SearchOption.AllDirectories);
                var excluded = NonRustProjectExclusionScanner.FindDirectoriesToExclude(workspaceRoot, allFiles, cargoManifests);

                if (excluded.Count == 0)
                {
                    return;
                }

                var json = "{\n  \"ExcludedItems\": [\n" +
                    string.Join(",\n", excluded.Select(item => $"    \"{item}\"")) +
                    "\n  ]\n}\n";
                File.WriteAllText(settingsPath, json);
                KubunoLog.WriteLine($"Kubuno: wrote {settingsPath}, excluding {excluded.Count} non-Rust project director{(excluded.Count == 1 ? "y" : "ies")} from Folder View: {string.Join(", ", excluded)}.");
            }
            catch (Exception exception)
            {
                KubunoLog.WriteException("Kubuno: failed to generate VSWorkspaceSettings.json", exception);
            }
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

                _mcpBridgeHost?.Dispose();
                _mcpBridgeHost = null;

                if (Instance == this)
                {
                    Instance = null;
                }
            }

            base.Dispose(disposing);
        }

        /// <summary>
        /// Forwards <c>Kubuno.VisualStudio.Views.Logging.IKubunoLog</c> calls to this package's own
        /// <see cref="Logging.KubunoLog"/> "Kubuno" pane - see
        /// <c>src/Kubuno.VisualStudio.Views/INTEGRATION.md</c> &sect;5 for why that library cannot
        /// reference <see cref="Logging.KubunoLog"/> directly.
        /// </summary>
        private sealed class KubunoLogAdapter : Kubuno.VisualStudio.Views.Logging.IKubunoLog
        {
            public void WriteLine(string message) => KubunoLog.WriteLine(message);

            public void WriteException(string context, Exception exception) => KubunoLog.WriteException(context, exception);
        }
    }
}
