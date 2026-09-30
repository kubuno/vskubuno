using System;
using System.ComponentModel.Design;
using System.Diagnostics;
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

// Assembly resolution for this VSIX's private assemblies: ONE binding path ([ProvideBindingPath] on
// KubunoPackage below), deliberately NOT one [assembly: ProvideCodeBase] per assembly.
//
// Several of our assemblies are resolved by NAME rather than by path, which cannot see an extension
// folder on its own: Kubuno.VisualStudio.RustProjectSystem (RustProject.imagemanifest and
// KubunoControls.imagemanifest point at its WPF resources by assembly name, and Visual Studio's image
// service loads them with Assembly.Load), its dependencies Kubuno.Launch, Kubuno.VisualStudio.Core and
// Kubuno.Cargo (MEF composes the CPS exports such as RustDebugLaunchProvider before this package ever
// runs), Kubuno.Mcp.Bridge (StartMcpBridgeAsync) and Kubuno.VisualStudio.TemplateWizard (the
// "Create a new project" wizard's own Assembly.Load).
//
// Why not ProvideCodeBase (docs/RSPROJ.md, addendum "Solution Explorer icons in a regular install"):
// none of these assemblies is strong-named. When VSIXInstaller (or `devenv /updateconfiguration`)
// regenerates the hive's devenv.exe.config, each ProvideCodeBase becomes a
// <codeBase href="...\Extensions\<id>\X.dll"/> for a publicKeyToken="" identity, and the CLR refuses a
// codeBase outside the application base for an assembly without a strong name: every by-name
// Assembly.Load then fails hard with FileLoadException 0x80131041 ("the private assembly was located
// outside the appbase directory") - even while the very same assembly is already loaded - and
// AssemblyResolve is never raised, so no resolver can rescue it (reproduced standalone; also visible in
// the regular hive's ComponentModelCache\*.err for Kubuno.Cargo). The image service's by-name load of
// the icons' resource assembly failed that way, so every project/file/element icon went blank in a
// normal install, while the Toolbox icons (WPF Application.LoadComponent, which first looks at the
// assemblies already loaded) kept working. The experimental instance hid it: MSBuild's dev deployment
// does not regenerate devenv.exe.config, so the entries never reached it.
//
// A binding path writes nothing into devenv.exe.config: the by-name load misses the application base,
// AssemblyResolve is raised, and Visual Studio's own resolver loads the assembly from this folder - the
// same file, hence the same loaded copy, that MEF and the package loader use.

namespace Kubuno.VisualStudio
{
    /// <summary>
    /// The Kubuno package: registers the Tools &gt; Options &gt; Kubuno pages, the .kbview designer, the
    /// commands and the format-on-save hook. The Rust language client itself (<see cref="LanguageService.RustLanguageClient"/>)
    /// and the content type it targets are separate MEF components, exported independently of this
    /// package and activated by VS when a matching document is opened - they do not need this
    /// package to be loaded first.
    ///
    /// <para><b>When it loads</b>: only in a Rust context, never
    /// for a C#-only solution or the start window - Visual Studio otherwise names the extension in its "you can
    /// improve startup performance by disabling..." info bar. It background-loads on one UI context rule
    /// (<see cref="PackageGuids.KubunoActivationUIContextString"/>): a solution holding a <c>.rsproj</c>, the active
    /// project being one, a Rust or <c>.kbview</c> editor, or an Open Folder Cargo workspace (a context
    /// <see cref="Workspace.CargoFolderActivation"/> turns on - rules have no "folder contains" term). Everything
    /// else loads it on demand: its commands (menu clicks), the .kbview editor factory, its options pages.</para>
    ///
    /// <para><b>What it does when it loads</b>: file probing and service lookups on a background thread, then a
    /// short UI-thread section for what must exist before the first Kubuno command, editor or F7 (editor factory,
    /// commands, priority command target, format on save, option hosts); the rest - Output pane, MCP bridge, Toolbox
    /// cleanup, template wizard preload, Open Folder launch targets - runs once the solution has finished loading,
    /// off the UI thread or on UI idle (<see cref="InitializeDeferredAsync"/>). Load time and UI-thread time are
    /// written to the "Kubuno" pane.</para>
    /// </summary>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    // Probes this VSIX's folder for by-name loads of our private assemblies - see the comment at the top
    // of this file for why this replaces the former per-assembly ProvideCodeBase entries.
    [ProvideBindingPath]
    [InstalledProductRegistration("Kubuno for Visual Studio", "Rust language support (rust-analyzer, TextMate coloring, rustfmt) for Kubuno development.", "1.0")]
    // The ONLY auto-load. Formerly NoSolution + SolutionExists + FolderOpened: the package (and its Toolbox
    // cleanup, which loads the whole Toolbox) then ran on every start of Visual Studio, for every user.
    [ProvideAutoLoad(PackageGuids.KubunoActivationUIContextString, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideUIContextRule(
        PackageGuids.KubunoActivationUIContextString,
        name: "Kubuno activation",
        expression: "RustSolution | RustProject | RustEditor | KbviewEditor | CargoFolder",
        termNames: new[] { "RustSolution", "RustProject", "RustEditor", "KbviewEditor", "CargoFolder" },
        termValues: new[]
        {
            "SolutionHasProjectCapability:RustProjectSystem",
            "ActiveProjectCapability:RustProjectSystem",
            "ActiveEditorContentType:rust",
            "ActiveEditorContentType:kbview",
            "{" + PackageGuids.CargoFolderUIContextString + "}",
        })]
    [ProvideOptionPage(typeof(RustOptionsPage), Constants.OptionsCategoryName, Constants.OptionsRustPageName, 0, 0, supportsAutomation: true, IsInUnifiedSettings = true, UnifiedSettingsCategoryMoniker = "kubuno.rust")]
    [ProvideProfile(typeof(RustOptionsPage), Constants.OptionsCategoryName, Constants.OptionsRustPageName, 0, 0, isToolsOptionPage: true)]
    [ProvideOptionPage(typeof(Kubuno.VisualStudio.Views.Options.KbviewOptionsPage), Constants.OptionsCategoryName, "Views", 0, 0, supportsAutomation: true, IsInUnifiedSettings = true, UnifiedSettingsCategoryMoniker = "kubuno.views")]
    [ProvideProfile(typeof(Kubuno.VisualStudio.Views.Options.KbviewOptionsPage), Constants.OptionsCategoryName, "Views", 0, 0, isToolsOptionPage: true)]
    [ProvideOptionPage(typeof(DebuggingOptionsPage), Constants.OptionsCategoryName, "Debugging", 0, 0, supportsAutomation: true, IsInUnifiedSettings = true, UnifiedSettingsCategoryMoniker = "kubuno.debugging")]
    // Kubuno.VisualStudio.Designer's own INTEGRATION.md Â§3: the split Design|XML editor for .kbview
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
    // LOGVIEWID_Code (verified by reflecting VSConstants.LOGVIEWID_Code): "View Code" (F7) opens the XML in a
    // plain code window of this factory (KbviewEditorFactory.CodePhysicalView), like WinForms' Form1.cs.
    [ProvideEditorLogicalView(typeof(Kubuno.VisualStudio.Designer.EditorFactory.KbviewEditorFactory), "{7651a701-06e5-11d1-8ebd-00a0c90f26ea}")] // LOGVIEWID_Code
    [ProvideEditorExtension(typeof(Kubuno.VisualStudio.Designer.EditorFactory.KbviewEditorFactory), Kubuno.VisualStudio.Views.KbviewConstants.FileExtension, Kubuno.VisualStudio.Designer.DesignerConstants.EditorExtensionPriority)]
    [ProvideOptionPage(typeof(Kubuno.VisualStudio.Designer.Options.KbviewDesignerOptionsPage), Constants.OptionsCategoryName, Kubuno.VisualStudio.Designer.DesignerConstants.OptionsPageName, 0, 0, supportsAutomation: true, IsInUnifiedSettings = true, UnifiedSettingsCategoryMoniker = "kubuno.designer")]
    [ProvideProfile(typeof(Kubuno.VisualStudio.Designer.Options.KbviewDesignerOptionsPage), Constants.OptionsCategoryName, Kubuno.VisualStudio.Designer.DesignerConstants.OptionsPageName, 0, 0, isToolsOptionPage: true)]
    // The View Outline tool window (Tools menu, KubunoCommands.vsct). The former fallback "Kubuno
    // Toolbox"/"Kubuno Properties" tool windows were removed: the designer fills Visual Studio's own
    // Toolbox and Properties window (docs/DESIGNER.md Â§11). Their GUIDs are no longer registered, so a
    // persisted window layout that still names them cannot recreate them.
    [ProvideToolWindow(typeof(Kubuno.VisualStudio.Designer.ToolWindows.OutlineToolWindow))]
    // The crate manager (NuGet-like, one per .rsproj, in the document well): CrateManager/CrateManagerToolWindow.cs.
    [ProvideToolWindow(typeof(Kubuno.VisualStudio.CrateManager.CrateManagerToolWindow), MultiInstances = true, Style = VsDockStyle.MDI, Transient = true)]
    // docs/DATA.md DATA-5: the Data Explorer (docked with Server Explorer, {74946827-37a0-11d2-a273-00c04f8ef4ec}) and its
    // query windows (in the document well, one per query, not restored at the next start; their own key binding scope
    // for F5 / Ctrl+Shift+E, KubunoCommands.vsct KeyBindings), and the Data options page.
    [ProvideToolWindow(typeof(Kubuno.VisualStudio.DataExplorer.DataExplorerToolWindow), Style = VsDockStyle.Tabbed, Window = "74946827-37a0-11d2-a273-00c04f8ef4ec")]
    [ProvideToolWindow(typeof(Kubuno.VisualStudio.DataExplorer.QueryToolWindow), MultiInstances = true, Style = VsDockStyle.MDI, Transient = true)]
    [ProvideKeyBindingTable(PackageGuidStrings.QueryToolWindow, 120)]
    // docs/DATA.md DATA-6: the Data Sources window (docked with Server Explorer, like the Data Explorer).
    [ProvideToolWindow(typeof(Kubuno.VisualStudio.DataSources.DataSourcesToolWindow), Style = VsDockStyle.Tabbed, Window = "74946827-37a0-11d2-a273-00c04f8ef4ec")]
    [ProvideOptionPage(typeof(DataOptionsPage), Constants.OptionsCategoryName, "Data", 0, 0, supportsAutomation: true, IsInUnifiedSettings = true, UnifiedSettingsCategoryMoniker = "kubuno.data")]
    [ProvideProfile(typeof(DataOptionsPage), Constants.OptionsCategoryName, "Data", 0, 0, isToolsOptionPage: true)]
    [ProvideSettingsManifest(PackageRelativeManifestFile = @"UnifiedSettings\kubuno.registration.json")]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    // Extended "Ajouter" submenu on a .rsproj project node (KubunoCommands.vsct's
    // VisibilityConstraints, guidRustProjectUIContext): the CPS-native way to scope a menu group to
    // one project type, rather than DynamicVisibility+BeforeQueryStatus (already used for the
    // Solution Explorer item-context menu above, but that pattern re-evaluates on every query and
    // only fits a single-item selection check - a project-node submenu is better served by a rule
    // the shell evaluates once per active-project-capability change). "RustProjectSystem" here is
    // Kubuno.VisualStudio.RustProjectSystem.RustProjectCapabilities.RustProjectSystem's literal
    // value (that assembly is MEF-composed, not project-referenced here - see the assembly
    // resolution remark at the top of this file - so the string is repeated rather than shared via a type reference).
    [ProvideUIContextRule(
        PackageGuids.RustProjectUIContextString,
        name: "RustProjectSystem",
        expression: "RustProjectSystem",
        termNames: new[] { "RustProjectSystem" },
        termValues: new[] { "ActiveProjectCapability:RustProjectSystem" })]
    [Guid(PackageGuidStrings.Package)]
    public sealed class KubunoPackage : AsyncPackage
    {
        private FormatOnSaveDocumentEvents? _formatOnSaveEvents;
        private IVsFolderWorkspaceService? _workspaceService;
        private Kubuno.Mcp.Bridge.PipeProtocol.VsMcpBridgeHost? _mcpBridgeHost;
        private IVsRegisterPriorityCommandTarget? _priorityTargets;
        private uint _viewSwitchCookie;
        private ProjectDesignSurfaceRuntimeProvider? _designSurfaceRuntimes;

        /// <summary>
        /// Set once the package is sited, so MEF components (which are not package-owned and would
        /// otherwise have no way to reach it) can read options via <see cref="Package.GetDialogPage"/>.
        /// </summary>
        internal static KubunoPackage? Instance { get; private set; }

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            var loadTime = Stopwatch.StartNew();
            var uiThreadTime = new Stopwatch();

            // ---- Background thread (a background-loaded AsyncPackage starts here): file probing, service lookups.
            // Deliberately no SwitchToMainThreadAsync yet: everything up to the UI section below must stay off it.
            await TaskScheduler.Default;
            Instance = this;

            // Kubuno.VisualStudio.Views (the .kbview language client) never references
            // Kubuno.VisualStudio.Logging.KubunoLog directly (that would be a circular reference -
            // see that library's Logging\IKubunoLog.cs remarks and its own INTEGRATION.md &sect;5);
            // this adapter is the seam instead. Must happen before any .kbview document can open.
            Kubuno.VisualStudio.Views.Logging.KubunoViewsLogHost.Current = new KubunoLogAdapter();
            Kubuno.VisualStudio.Designer.Toolbox.NativeToolboxInstaller.IconName =
                tag => Kubuno.VisualStudio.Core.SolutionExplorer.ControlIcons.IdFor(tag) == Kubuno.VisualStudio.Core.SolutionExplorer.ControlIcons.FallbackId ? "Control" : tag;

            var extensionInstallDirectory = GetExtensionInstallDirectory();
            // Awaited here rather than deferred: when the package loads because a .rsproj is opening, that
            // project's Sdk="Kubuno.Rust.Sdk/..." must resolve from the bundled feed. Cached (see the class) -
            // on an unchanged NuGet.Config this is a single small stamp-file read.
            RustSdkFeedInstaller.EnsureRegistered(extensionInstallDirectory);
            var surfaceExePath = KubunoViewsSurfaceLocator.Locate(extensionInstallDirectory, devBuildDirectory: @"C:\kubuno-build\agent-dsgint\release\examples");

            // The Project Properties editor's "Manage crates..." / "Reference Manager..." links
            // (Application page, Dependencies category) open this package's own UIs.
            Kubuno.VisualStudio.RustProjectSystem.ProjectProperties.RustProjectPropertiesHost.OpenCrateManagerAsync = async hierarchy =>
            {
                await JoinableTaskFactory.SwitchToMainThreadAsync();
                if (RsprojProjectContext.TryCreate((IVsHierarchy)hierarchy) is { } context)
                {
                    await Kubuno.VisualStudio.CrateManager.CrateManagerToolWindow.ShowAsync(context, crateName: null);
                }
            };
            Kubuno.VisualStudio.RustProjectSystem.ProjectProperties.RustProjectPropertiesHost.OpenReferenceManagerAsync = async hierarchy =>
            {
                await JoinableTaskFactory.SwitchToMainThreadAsync();
                if (RsprojProjectContext.TryCreate((IVsHierarchy)hierarchy) is { } context)
                {
                    await AddProjectReferenceCommand.ShowReferenceManagerAsync(context);
                }
            };

            // Fetched here, cast (a COM QueryInterface for the native ones) on the UI thread below.
            var priorityTargetsService = await GetServiceAsync(typeof(SVsRegisterPriorityCommandTarget));
            var buildManagerService = surfaceExePath is null ? null : await GetServiceAsync(typeof(SVsSolutionBuildManager));
            var commandService = await GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;

            // ---- UI thread: only what must exist before the first Kubuno command, .kbview editor or F7.
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            uiThreadTime.Start();
            var priorityTargets = priorityTargetsService as IVsRegisterPriorityCommandTarget;
            var buildManager = buildManagerService as IVsSolutionBuildManager2;

            Kubuno.VisualStudio.Views.Options.KubunoViewsOptionsHost.Current =
                (Kubuno.VisualStudio.Views.Options.KbviewOptionsPage)GetDialogPage(typeof(Kubuno.VisualStudio.Views.Options.KbviewOptionsPage));

            // Kubuno.VisualStudio.Designer's own INTEGRATION.md Â§3/Â§4/Â§6: the split Design|XML editor
            // factory (a classic, package-registered IVsEditorFactory - [ProvideEditorFactory] only
            // emits pkgdef metadata, VS still needs a live instance handed to it via RegisterEditorFactory),
            // its options page's static gateway, and the DSG-7 design surface factory. Must happen
            // before any .kbview document can open through "Open With... > Kubuno View Designer" (which
            // itself loads this package on demand, and waits for this method).
            RegisterEditorFactory(new Kubuno.VisualStudio.Designer.EditorFactory.KbviewEditorFactory());
            Kubuno.VisualStudio.Designer.Options.DesignerOptionsHost.Current =
                (Kubuno.VisualStudio.Designer.Options.KbviewDesignerOptionsPage)GetDialogPage(typeof(Kubuno.VisualStudio.Designer.Options.KbviewDesignerOptionsPage));

            // The Properties window resolves the IEventBindingService behind a double-click on an event
            // row through its own service chain, which ends at Visual Studio's global services - found
            // live that the chain does not always reach the active designer's surface (the row then did
            // nothing). Proffered globally too; it only knows .kbview elements, so it is inert for every
            // other designer (whose own designer host answers first anyway).
            ((System.ComponentModel.Design.IServiceContainer)this).AddService(
                typeof(System.ComponentModel.Design.IEventBindingService),
                Kubuno.VisualStudio.Designer.PropertyBrowser.KbviewEventBindingService.Instance,
                promote: true);
            // docs/DESIGNER.md Â§11: F7/Shift+F7 switch between a .kbview's designer and its XML.
            if (priorityTargets is not null)
            {
                var viewSwitch = new Kubuno.VisualStudio.Designer.EditorFactory.DesignerViewSwitchCommandTarget(this);
                ErrorHandler.ThrowOnFailure(priorityTargets.RegisterPriorityCommandTarget(0, viewSwitch, out _viewSwitchCookie));
                _priorityTargets = priorityTargets;
            }

            if (surfaceExePath is not null)
            {
                var oleServiceProvider = (Microsoft.VisualStudio.OLE.Interop.IServiceProvider)this;
                // docs/DESIGNER.md section 15: each designer renders with its project's own kubuno_ui.dll
                // (a design build against the project); the bundled surface is the fallback.
                _designSurfaceRuntimes = new ProjectDesignSurfaceRuntimeProvider(surfaceExePath, JoinableTaskFactory);
                if (buildManager is not null)
                {
                    _designSurfaceRuntimes.Advise(buildManager);
                }

                Kubuno.VisualStudio.Designer.DesignSurface.DesignSurfaceHostFactoryHost.Current =
                    new Kubuno.VisualStudio.Designer.DesignSurface.RustDesignSurfaceHostFactory(surfaceExePath, oleServiceProvider: oleServiceProvider, runtimeProvider: _designSurfaceRuntimes);
                KubunoLog.WriteLine($"Kubuno: bundled design surface exe resolved at '{surfaceExePath}'.");
            }
            else
            {
                KubunoLog.WriteLine("Kubuno: kubuno-views-surface.exe not found - the Kubuno View Designer's Design pane will show its placeholder. Build it: cd Z:\\projects\\kubuno\\desktop\\windows ; $env:CARGO_TARGET_DIR='C:\\kubuno-build\\agent-dsgint' ; cargo build --release --example view_embed -p kubuno-views -j 1");
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

            if (commandService is not null)
            {
                DebugRustTestAtCursorCommand.Initialize(this, commandService);
                DesignerToolWindowCommands.Initialize(this, commandService);
                GenerateRustProjectsCommand.Initialize(this, commandService);
                RestartRustAnalyzerCommand.Initialize(this, commandService);
                PaintDebugCommand.Initialize(commandService);
                DialogGalleryCommand.Initialize(commandService);
                AddProjectItemCommands.Initialize(this, commandService);
                AddProjectReferenceCommand.Initialize(this, commandService);
                AddCargoDependencyCommand.Initialize(this, commandService);
                Kubuno.VisualStudio.DataExplorer.ShowDataExplorerCommand.Initialize(commandService);
                Kubuno.VisualStudio.Migrations.MigrationCommands.Initialize(commandService);
                Kubuno.VisualStudio.DataSources.ShowDataSourcesCommand.Initialize(commandService);
            }

            uiThreadTime.Stop();
            KubunoLog.WriteLine($"Kubuno: package loaded in {loadTime.ElapsedMilliseconds} ms ({uiThreadTime.ElapsedMilliseconds} ms on the UI thread); the rest follows once the solution is loaded.");

            // Everything else: after the solution (or folder) has finished loading, off the UI thread or on UI idle.
            JoinableTaskFactory.RunAsync(() => InitializeDeferredAsync(DisposalToken)).FileAndForget("Kubuno/Package/DeferredInitialization");
        }

        /// <summary>
        /// The non-essential part of package initialization, none of which a first Kubuno command or editor
        /// needs: runs once any solution load in progress is over, never on the UI thread except for short idle-time
        /// steps. Every step is idempotent and never throws.
        /// </summary>
        private async Task InitializeDeferredAsync(CancellationToken cancellationToken)
        {
            var uiThreadTime = new Stopwatch();
            try
            {
                await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                var waitForSolution = KnownUIContexts.SolutionOpeningContext.IsActive && !KnownUIContexts.SolutionExistsAndFullyLoadedContext.IsActive;
                if (waitForSolution)
                {
                    var loaded = new TaskCompletionSource<bool>();
                    KnownUIContexts.SolutionExistsAndFullyLoadedContext.WhenActivated(() => loaded.TrySetResult(true));
                    await loaded.Task.WithCancellation(cancellationToken);
                }

                await TaskScheduler.Default;
                PreloadTemplateWizardAssembly();

                // Idle-time UI work: the "Kubuno" Output pane (log lines are buffered until then), the Toolbox
                // cleanup (only when a previous session may have left Kubuno tabs), the MCP bridge's DTE reads,
                // the Open Folder workspace service (looked up on the UI thread, as it always was).
                var componentModel = await GetServiceAsync(typeof(SComponentModel)) as IComponentModel;
                var outputWindowService = await GetServiceAsync(typeof(SVsOutputWindow));
                var dteService = await GetServiceAsync(typeof(SDTE));
                string? visualStudioVersion = null;
                string? solutionOrFolderPath = null;
                DTE2? dte = null;
                await JoinableTaskFactory.StartOnIdle(
                    async () =>
                    {
                        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                        uiThreadTime.Start();
                        if (outputWindowService is IVsOutputWindow outputWindow)
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

                        // docs/DESIGNER.md Â§11: the Kubuno Toolbox tabs exist only while a .kbview designer is
                        // active; drop any a previous session (or version) left in the persisted toolbox.
                        Kubuno.VisualStudio.Designer.Toolbox.NativeToolboxInstaller.UninstallIfLeftBehind();

                        try
                        {
                            _workspaceService = componentModel?.GetService<IVsFolderWorkspaceService>();
                        }
                        catch (Exception exception)
                        {
                            KubunoLog.WriteException("Kubuno: the Open Folder workspace service is unavailable", exception);
                        }

                        dte = dteService as DTE2;
                        if (dte is not null)
                        {
                            visualStudioVersion = dte.Version;
                            solutionOrFolderPath = dte.Solution?.FullName;
                        }

                        uiThreadTime.Stop();
                    },
                    VsTaskRunContext.UIThreadIdlePriority).JoinAsync(cancellationToken);

                await TaskScheduler.Default;
                StartMcpBridge(dte, visualStudioVersion, solutionOrFolderPath);

                if (_workspaceService != null)
                {
                    _workspaceService.OnActiveWorkspaceChanged += OnActiveWorkspaceChangedAsync;
                    // The package can finish loading after a folder is already open (e.g. the user
                    // reopens the same folder next session): regenerate for whatever is open right now too.
                    await RegenerateLaunchTargetsForCurrentWorkspaceAsync();
                    await EnsureWorkspaceSettingsExcludeNonRustProjectsAsync();
                }

                KubunoLog.WriteLine($"Kubuno: deferred initialization done ({uiThreadTime.ElapsedMilliseconds} ms on the UI thread, at idle{(waitForSolution ? ", after the solution load" : string.Empty)}).");
            }
            catch (OperationCanceledException)
            {
                // Visual Studio is closing.
            }
            catch (Exception exception)
            {
                KubunoLog.WriteException("Kubuno: deferred package initialization failed", exception);
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
        /// "Crate-name casing, revisited": live-verified that
        /// Microsoft.VisualStudio.TemplateWizard.Wizard.CreateManagedInstance's own plain
        /// <c>Assembly.Load(AssemblyName)</c> call could not find it even with the (since replaced, see
        /// the comment at the top of this file) per-assembly code base registered - a separate,
        /// undocumented resolution path. The package's binding path should now cover it too; loading
        /// it eagerly (on a background thread, once the solution is loaded - see InitializeDeferredAsync) is kept as a belt-and-braces measure: once an assembly of a given
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
        /// to. Called from <see cref="InitializeDeferredAsync"/> on a background thread, once the solution
        /// is loaded (so the discovery file names it), with the DTE values it read at UI idle: writing the
        /// discovery file and starting the accept loop need no UI thread, and the provider only switches to
        /// it per request. Every failure is caught and logged to the "Kubuno" Output pane rather than surfaced
        /// as an exception - read-only VS context for Claude is a convenience, never something
        /// Rust/Cargo/Views functionality should depend on being available.
        /// </summary>
        private void StartMcpBridge(DTE2? dte, string? visualStudioVersion, string? solutionOrFolderPath)
        {
            try
            {
                if (dte is null)
                {
                    KubunoLog.WriteLine("Kubuno: MCP bridge not started - the DTE service is unavailable.");
                    return;
                }

                if (_mcpBridgeHost is not null)
                {
                    return;
                }

                var provider = new Kubuno.Mcp.Bridge.Dte.DteVsContextProvider(dte);
                var host = new Kubuno.Mcp.Bridge.PipeProtocol.VsMcpBridgeHost(provider);
                host.Start(visualStudioVersion: visualStudioVersion, solutionOrFolderPath: solutionOrFolderPath);
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

#pragma warning disable VSTHRD010 // guarded by ThreadHelper.CheckAccess() above, like the RDT unadvise.
                _designSurfaceRuntimes?.Dispose();
#pragma warning restore VSTHRD010
                _designSurfaceRuntimes = null;

                // docs/DATA.md DATA-5: stop kubuno-data-tool (EOF on its stdin).
                Kubuno.VisualStudio.DataExplorer.DataToolHost.Shutdown();

                if (_priorityTargets is not null)
                {
#pragma warning disable VSTHRD010 // guarded by ThreadHelper.CheckAccess() above, like the RDT unadvise.
                    _priorityTargets.UnregisterPriorityCommandTarget(_viewSwitchCookie);
#pragma warning restore VSTHRD010
                    _priorityTargets = null;
                }

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
