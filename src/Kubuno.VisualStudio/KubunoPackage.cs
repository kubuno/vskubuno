using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Core;
using Kubuno.Core.Extensibility;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

// Assembly resolution for this VSIX's private assemblies: ONE binding path ([ProvideBindingPath] on
// KubunoPackage below), deliberately NOT one [assembly: ProvideCodeBase] per assembly.
//
// Several of our assemblies are resolved by NAME rather than by path, which cannot see an extension
// folder on its own: Kubuno.Rust.ProjectSystem and Kubuno.Desktop.ProjectSystem (RustProject.imagemanifest and
// KubunoControls.imagemanifest point at their WPF resources by assembly name, and Visual Studio's image
// service loads them with Assembly.Load), their dependencies Kubuno.Core, Kubuno.Rust.Logic, Kubuno.Rust.Launch
// and Kubuno.Rust.Cargo (MEF composes the CPS exports such as RustDebugLaunchProvider before this package ever
// runs), the layer assemblies every one of them loads by reference (Kubuno.Core, Kubuno.Core.Logic,
// Kubuno.Core.Mcp.Bridge...), and the two template wizards (Kubuno.Rust.TemplateWizard,
// Kubuno.Desktop.TemplateWizard: the "Create a new project" wizard's own Assembly.Load).
//
// Why not ProvideCodeBase (docs/RSPROJ.md, addendum "Solution Explorer icons in a regular install"):
// none of these assemblies is strong-named. When VSIXInstaller (or `devenv /updateconfiguration`)
// regenerates the hive's devenv.exe.config, each ProvideCodeBase becomes a
// <codeBase href="...\Extensions\<id>\X.dll"/> for a publicKeyToken="" identity, and the CLR refuses a
// codeBase outside the application base for an assembly without a strong name: every by-name
// Assembly.Load then fails hard with FileLoadException 0x80131041 ("the private assembly was located
// outside the appbase directory") - even while the very same assembly is already loaded - and
// AssemblyResolve is never raised, so no resolver can rescue it (reproduced standalone; also visible in
// the regular hive's ComponentModelCache\*.err for Kubuno.Rust.Cargo). The image service's by-name load of
// the icons' resource assembly failed that way, so every project/file/element icon went blank in a
// normal install, while the Toolbox icons (WPF Application.LoadComponent, which first looks at the
// assemblies already loaded) kept working. The experimental instance hid it: MSBuild's dev deployment
// does not regenerate devenv.exe.config, so the entries never reached it.
//
// A binding path writes nothing into devenv.exe.config: the by-name load misses the application base,
// AssemblyResolve is raised, and Visual Studio's own resolver loads the assembly from this folder - the
// same file, hence the same loaded copy, that MEF and the package loader use. Every layer assembly ships in this
// one folder, so this single binding path covers all of them.

namespace Kubuno.VisualStudio
{
    /// <summary>
    /// The Kubuno package - the extension's composition root (docs/ARCHITECTURE.md, "Layers (as built)"). It owns
    /// every registration Visual Studio reads from the pkgdef (the attributes below: options pages, editor factory,
    /// tool windows, UI context rules, menus) and lists the product layers; what each layer does when the package
    /// loads lives in the layer (<see cref="KubunoLayer"/>), sequenced by <see cref="KubunoLayerHost"/> in Kubuno.Core.
    /// The Rust language client and the other MEF components are exported by the layer assemblies independently of
    /// this package and activated by Visual Studio when a matching document is opened.
    ///
    /// <para><b>When it loads</b>: only in a Rust context, never
    /// for a C#-only solution or the start window - Visual Studio otherwise names the extension in its "you can
    /// improve startup performance by disabling..." info bar. It background-loads on one UI context rule
    /// (<see cref="PackageGuids.KubunoActivationUIContextString"/>): a solution holding a <c>.rsproj</c>, the active
    /// project being one, a Rust or <c>.kbview</c> editor, or an Open Folder Cargo workspace (a context
    /// <c>Kubuno.Rust.Workspace.CargoFolderActivation</c> turns on - rules have no "folder contains" term). Everything
    /// else loads it on demand: its commands (menu clicks), the .kbview editor factory, its options pages.</para>
    ///
    /// <para><b>Adding a layer</b> (Web, Mobile...): reference its assembly from this project (it then ships in the
    /// VSIX folder, covered by the binding path), add its <see cref="KubunoLayer"/> to <see cref="CreateLayers"/>,
    /// declare its registrations below and its commands in <c>KubunoCommands.vsct</c>. Core, Rust and Desktop do not
    /// change.</para>
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
            "{" + Kubuno.Rust.PackageGuids.CargoFolderUIContextString + "}",
        })]
    // ---- Core layer (Kubuno.Core): the remote Linux host (dev database tunnel, later the Linux builds), docs/WEB.md ----
    [ProvideOptionPage(typeof(Kubuno.Core.Remote.RemoteHostOptionsPage), KubunoConstants.OptionsCategoryName, "Remote Linux host", 0, 0, supportsAutomation: true, IsInUnifiedSettings = true, UnifiedSettingsCategoryMoniker = "kubuno.remote")]
    [ProvideProfile(typeof(Kubuno.Core.Remote.RemoteHostOptionsPage), KubunoConstants.OptionsCategoryName, "Remote Linux host", 0, 0, isToolsOptionPage: true)]
    // ---- Rust layer (Kubuno.Rust) ----
    [ProvideOptionPage(typeof(Kubuno.Rust.Options.RustOptionsPage), KubunoConstants.OptionsCategoryName, Kubuno.Rust.Constants.OptionsRustPageName, 0, 0, supportsAutomation: true, IsInUnifiedSettings = true, UnifiedSettingsCategoryMoniker = "kubuno.rust")]
    [ProvideProfile(typeof(Kubuno.Rust.Options.RustOptionsPage), KubunoConstants.OptionsCategoryName, Kubuno.Rust.Constants.OptionsRustPageName, 0, 0, isToolsOptionPage: true)]
    [ProvideOptionPage(typeof(Kubuno.Rust.Options.DebuggingOptionsPage), KubunoConstants.OptionsCategoryName, "Debugging", 0, 0, supportsAutomation: true, IsInUnifiedSettings = true, UnifiedSettingsCategoryMoniker = "kubuno.debugging")]
    // The crate manager (NuGet-like, one per .rsproj, in the document well): CrateManager/CrateManagerToolWindow.cs.
    [ProvideToolWindow(typeof(Kubuno.Rust.CrateManager.CrateManagerToolWindow), MultiInstances = true, Style = VsDockStyle.MDI, Transient = true)]
    // Extended "Ajouter" submenu on a .rsproj project node (KubunoCommands.vsct's
    // VisibilityConstraints, guidRustProjectUIContext): the CPS-native way to scope a menu group to
    // one project type, rather than DynamicVisibility+BeforeQueryStatus (already used for the
    // Solution Explorer item-context menu above, but that pattern re-evaluates on every query and
    // only fits a single-item selection check - a project-node submenu is better served by a rule
    // the shell evaluates once per active-project-capability change). "RustProjectSystem" here is
    // Kubuno.Rust.ProjectSystem.RustProjectCapabilities.RustProjectSystem's literal value (that assembly is compiled
    // against the installed Visual Studio, not referenced here - see the assembly resolution remark at the top of this
    // file - so the string is repeated rather than shared via a type reference).
    [ProvideUIContextRule(
        Kubuno.Rust.PackageGuids.RustProjectUIContextString,
        name: "RustProjectSystem",
        expression: "RustProjectSystem",
        termNames: new[] { "RustProjectSystem" },
        termValues: new[] { "ActiveProjectCapability:RustProjectSystem" })]
    // ---- Desktop layer (Kubuno.Desktop) ----
    [ProvideOptionPage(typeof(Kubuno.Desktop.Views.Options.KbviewOptionsPage), KubunoConstants.OptionsCategoryName, "Views", 0, 0, supportsAutomation: true, IsInUnifiedSettings = true, UnifiedSettingsCategoryMoniker = "kubuno.views")]
    [ProvideProfile(typeof(Kubuno.Desktop.Views.Options.KbviewOptionsPage), KubunoConstants.OptionsCategoryName, "Views", 0, 0, isToolsOptionPage: true)]
    // Kubuno.Desktop's Designer\INTEGRATION.md section 3: the split Design|XML editor for .kbview
    // files, registered alongside - never instead of - kbview-languages.pkgdef's plain core text editor
    // (that pkgdef entry's own comment: "the HIGHEST value wins the double-click default", 0x64 there
    // vs. DesignerConstants.EditorExtensionPriority's 0x60 here, so the plain editor stays default).
    [ProvideEditorFactory(typeof(Kubuno.Desktop.Designer.EditorFactory.KbviewEditorFactory), 110)]
    // Fixed 2026-09-28 (docs/RSPROJ.md work package 6): these two GUIDs were swapped one slot too far
    // (7651a703/7651a704, actually LOGVIEWID_TextView/LOGVIEWID_UserChooseView) - VSConstants.LOGVIEWID_Designer
    // is really {...a702...}, verified live by reflecting Microsoft.VisualStudio.Shell.15.0.dll's
    // VSConstants fields. The mislabeled registration meant "Open With -> Kubuno View Designer" never
    // actually reached KbviewEditorFactory for the real Designer logical view (confirmed live: even a
    // never-opened .kbview opened with IVsUIShellOpenDocument.OpenSpecificEditor against the true
    // LOGVIEWID_Designer fell back to the plain text editor before this fix).
    [ProvideEditorLogicalView(typeof(Kubuno.Desktop.Designer.EditorFactory.KbviewEditorFactory), "{7651a702-06e5-11d1-8ebd-00a0c90f26ea}")] // LOGVIEWID_Designer
    [ProvideEditorLogicalView(typeof(Kubuno.Desktop.Designer.EditorFactory.KbviewEditorFactory), "{7651a703-06e5-11d1-8ebd-00a0c90f26ea}")] // LOGVIEWID_TextView
    // LOGVIEWID_Code (verified by reflecting VSConstants.LOGVIEWID_Code): "View Code" (F7) opens the XML in a
    // plain code window of this factory (KbviewEditorFactory.CodePhysicalView), like WinForms' Form1.cs.
    [ProvideEditorLogicalView(typeof(Kubuno.Desktop.Designer.EditorFactory.KbviewEditorFactory), "{7651a701-06e5-11d1-8ebd-00a0c90f26ea}")] // LOGVIEWID_Code
    [ProvideEditorExtension(typeof(Kubuno.Desktop.Designer.EditorFactory.KbviewEditorFactory), Kubuno.Desktop.Views.KbviewConstants.FileExtension, Kubuno.Desktop.Designer.DesignerConstants.EditorExtensionPriority)]
    // The .kbres resource editor (Kubuno.Desktop\Resources\Editor): the default editor of .kbres files everywhere (no
    // other editor is registered for the extension; 0x60 wins). Same logical views as the .kbview designer: Primary and
    // Designer -> the grid/thumbnail editor, Code and TextView -> a plain code window on the same XML buffer.
    [ProvideEditorFactory(typeof(Kubuno.Desktop.Resources.Editor.KbresEditorFactory), 111)]
    [ProvideEditorLogicalView(typeof(Kubuno.Desktop.Resources.Editor.KbresEditorFactory), "{7651a702-06e5-11d1-8ebd-00a0c90f26ea}")] // LOGVIEWID_Designer
    [ProvideEditorLogicalView(typeof(Kubuno.Desktop.Resources.Editor.KbresEditorFactory), "{7651a703-06e5-11d1-8ebd-00a0c90f26ea}")] // LOGVIEWID_TextView
    [ProvideEditorLogicalView(typeof(Kubuno.Desktop.Resources.Editor.KbresEditorFactory), "{7651a701-06e5-11d1-8ebd-00a0c90f26ea}")] // LOGVIEWID_Code
    [ProvideEditorExtension(typeof(Kubuno.Desktop.Resources.Editor.KbresEditorFactory), ".kbres", 0x60)]
    [ProvideOptionPage(typeof(Kubuno.Desktop.Designer.Options.KbviewDesignerOptionsPage), KubunoConstants.OptionsCategoryName, Kubuno.Desktop.Designer.DesignerConstants.OptionsPageName, 0, 0, supportsAutomation: true, IsInUnifiedSettings = true, UnifiedSettingsCategoryMoniker = "kubuno.designer")]
    [ProvideProfile(typeof(Kubuno.Desktop.Designer.Options.KbviewDesignerOptionsPage), KubunoConstants.OptionsCategoryName, Kubuno.Desktop.Designer.DesignerConstants.OptionsPageName, 0, 0, isToolsOptionPage: true)]
    // The View Outline tool window (Tools menu, KubunoCommands.vsct). The former fallback "Kubuno
    // Toolbox"/"Kubuno Properties" tool windows were removed: the designer fills Visual Studio's own
    // Toolbox and Properties window (docs/DESIGNER.md section 11). Their GUIDs are no longer registered, so a
    // persisted window layout that still names them cannot recreate them.
    [ProvideToolWindow(typeof(Kubuno.Desktop.Designer.ToolWindows.OutlineToolWindow))]
    // docs/DATA.md DATA-5: the Data Explorer (docked left, tabbed with the Toolbox {B1E99781-AB81-11D0-B683-00AA00A3EE26}, where
    // Server Explorer lives - tabbing with Server Explorer itself left it floating in a profile that never opened it) and its
    // query windows (in the document well, one per query, not restored at the next start; their own key binding scope
    // for F5 / Ctrl+Shift+E, KubunoCommands.vsct KeyBindings), and the Data options page.
    [ProvideToolWindow(typeof(Kubuno.Desktop.DataExplorer.DataExplorerToolWindow), Style = VsDockStyle.Tabbed, Window = "B1E99781-AB81-11D0-B683-00AA00A3EE26")]
    [ProvideToolWindow(typeof(Kubuno.Desktop.DataExplorer.QueryToolWindow), MultiInstances = true, Style = VsDockStyle.MDI, Transient = true)]
    [ProvideKeyBindingTable(Kubuno.Desktop.PackageGuidStrings.QueryToolWindow, 120)]
    // docs/DATA.md DATA-6: the Data Sources window, tabbed with Solution Explorer {3AE79031-E1BC-11D0-8F78-00A0C9110057} like Windows Forms'.
    [ProvideToolWindow(typeof(Kubuno.Desktop.DataSources.DataSourcesToolWindow), Style = VsDockStyle.Tabbed, Window = "3AE79031-E1BC-11D0-8F78-00A0C9110057")]
    [ProvideOptionPage(typeof(Kubuno.Desktop.Options.DataOptionsPage), KubunoConstants.OptionsCategoryName, "Data", 0, 0, supportsAutomation: true, IsInUnifiedSettings = true, UnifiedSettingsCategoryMoniker = "kubuno.data")]
    [ProvideProfile(typeof(Kubuno.Desktop.Options.DataOptionsPage), KubunoConstants.OptionsCategoryName, "Data", 0, 0, isToolsOptionPage: true)]
    // ---- Web layer (Kubuno.Web) ----
    // SPIKE (docs/WEB-VIEWS.md, WV-9a): the WebView2 design surface in a document pane, the default (and only) editor of
    // .kbwebspike files; Primary/Designer -> the surface, Code/TextView -> a code window on the same buffer (F7).
    [ProvideEditorFactory(typeof(Kubuno.Web.WebDesigner.Spike.WebDesignSpikeEditorFactory), 112)]
    [ProvideEditorLogicalView(typeof(Kubuno.Web.WebDesigner.Spike.WebDesignSpikeEditorFactory), "{7651a702-06e5-11d1-8ebd-00a0c90f26ea}")] // LOGVIEWID_Designer
    [ProvideEditorLogicalView(typeof(Kubuno.Web.WebDesigner.Spike.WebDesignSpikeEditorFactory), "{7651a703-06e5-11d1-8ebd-00a0c90f26ea}")] // LOGVIEWID_TextView
    [ProvideEditorLogicalView(typeof(Kubuno.Web.WebDesigner.Spike.WebDesignSpikeEditorFactory), "{7651a701-06e5-11d1-8ebd-00a0c90f26ea}")] // LOGVIEWID_Code
    [ProvideEditorExtension(typeof(Kubuno.Web.WebDesigner.Spike.WebDesignSpikeEditorFactory), Kubuno.Web.WebDesigner.Spike.WebDesignSpikeConstants.FileExtension, 0x60)]
    // ---- Mobile layer (Kubuno.Mobile): no registration yet. ----
    // ---- Every layer ----
    [ProvideSettingsManifest(PackageRelativeManifestFile = @"UnifiedSettings\kubuno.registration.json")]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [Guid(KubunoGuids.PackageString)]
    public sealed class KubunoPackage : AsyncPackage
    {
        private KubunoLayerHost? _host;

        /// <summary>
        /// The product layers, in initialization order: Rust first (the base every Kubuno target builds on), then the
        /// targets. Core's own services (Output pane, MCP bridge, dialog gallery) are run by <see cref="KubunoLayerHost"/>
        /// before them.
        /// </summary>
        internal static KubunoLayer[] CreateLayers() => new KubunoLayer[]
        {
            new Kubuno.Rust.RustLayer(),
            new Kubuno.Desktop.DesktopLayer(),
            new Kubuno.Web.WebLayer(),
            new Kubuno.Mobile.MobileLayer(),
        };

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            _host = new KubunoLayerHost(this, RegisterEditorFactory, CreateLayers());
            await _host.InitializeAsync(cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            // disposing is false when called from a finalizer, off the UI thread; skip the layers' own cleanup in that
            // case rather than let it throw - COM registrations are released by the process exiting anyway when the
            // deterministic Dispose() path was never reached.
            if (disposing && ThreadHelper.CheckAccess())
            {
#pragma warning disable VSTHRD010 // guarded by the ThreadHelper.CheckAccess() check above, not a ThrowIfNotOnUIThread() call the analyzer can see.
                _host?.Dispose();
#pragma warning restore VSTHRD010
                _host = null;
            }

            base.Dispose(disposing);
        }
    }
}
