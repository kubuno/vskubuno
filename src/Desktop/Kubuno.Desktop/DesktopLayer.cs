using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Core.Extensibility;
using Kubuno.Core.Logging;
using Kubuno.Core.UI;
using Kubuno.Desktop.Commands;
using Kubuno.Desktop.DesignerIntegration;
using Kubuno.Desktop.Logic.Overrides;
using Kubuno.Desktop.Logic.SolutionExplorer;
using Kubuno.Desktop.TemplateWizard;
using Kubuno.Rust.Commands;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace Kubuno.Desktop
{
    /// <summary>
    /// The desktop layer's part of the package (docs/ARCHITECTURE.md, "Layers (as built)"): the .kbview language client's
    /// host seams, the Kubuno View Designer (editor factory, F7 switch, Properties events, design surface), the data
    /// tooling and paint debug commands, the view and control entries of the "Ajouter" submenu, and the rest of what a
    /// Kubuno desktop application needs from the package. Its MEF parts (the .kbview language client and content type,
    /// the Rust-side .kbview IntelliSense, SQL in Rust strings, override members) are composed by Visual Studio.
    /// </summary>
    public sealed class DesktopLayer : KubunoLayer
    {
        /// <summary>The Kubuno view and control entries of the extended "Ajouter" submenu of a .rsproj.</summary>
        private static readonly ProjectItemTemplate[] ItemTemplates =
        {
            new ProjectItemTemplate(PackageIds.AddKubunoViewCommand, "KubunoView", "Vue Kubuno", "Nom de la vue :", "NewView", ".kbview"),
            // docs/EVENTS.md EVT-7b: the control templates (the wizard names the struct and the file, and declares the module).
            new ProjectItemTemplate(PackageIds.AddKubunoCustomControlCommand, "KubunoCustomControl", "Contrôle personnalisé Kubuno", "Nom du contrôle :", "CustomControl", ".rs", ControlItemNames.ModuleName, intoSourceFolder: true),
            new ProjectItemTemplate(PackageIds.AddKubunoUserControlCommand, "KubunoUserControl", "Contrôle utilisateur Kubuno", "Nom du contrôle utilisateur :", "UserControl", ".kbcontrol", ControlItemNames.ModuleName, intoSourceFolder: true),
            new ProjectItemTemplate(PackageIds.AddKubunoInheritedControlCommand, "KubunoInheritedControl", "Contrôle hérité Kubuno", "Nom du contrôle :", "InheritedControl", ".rs", ControlItemNames.ModuleName, intoSourceFolder: true),
            new ProjectItemTemplate(PackageIds.AddKubunoComponentCommand, "KubunoComponent", "Composant Kubuno", "Nom du composant :", "Component", ".rs", ControlItemNames.ModuleName, intoSourceFolder: true),
        };

        private const string SampleComponent =
            "use kubuno_views::prelude::*;\n\n#[derive(Component, Default)]\n#[kubuno(extends = Button)]\npub struct RoundButton {\n    base: Button,\n}\n";

        private string? _surfaceExePath;
        private IVsRegisterPriorityCommandTarget? _priorityTargets;
        private object? _priorityTargetsService;
        private object? _buildManagerService;
        private uint _viewSwitchCookie;

        private uint _bindingDefinitionCookie;
        private ProjectDesignSurfaceRuntimeProvider? _designSurfaceRuntimes;

        public override string Name => "Desktop";

        public override async Task InitializeAsync(KubunoLayerContext context, CancellationToken cancellationToken)
        {
            // The .kbview language client and the designer log through IKubunoLog (their own seam, kept for tests);
            // it must be set before any .kbview document can open.
            Kubuno.Desktop.Views.Logging.KubunoViewsLogHost.Current = new KubunoLogAdapter();
            Kubuno.Desktop.Designer.Toolbox.NativeToolboxInstaller.IconName =
                tag => ControlIcons.IdFor(tag) == ControlIcons.FallbackId ? "Control" : tag;

            _surfaceExePath = KubunoViewsSurfaceLocator.Locate(context.ExtensionDirectory, devBuildDirectory: @"C:\kubuno-build\agent-dsgint\release\examples");

            // Fetched here, cast (a COM QueryInterface for the native ones) on the UI thread.
            _priorityTargetsService = await context.GetServiceAsync(typeof(SVsRegisterPriorityCommandTarget));
            _buildManagerService = _surfaceExePath is null ? null : await context.GetServiceAsync(typeof(SVsSolutionBuildManager));
        }

        public override void InitializeOnUIThread(KubunoLayerContext context)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var package = context.Package;
            var priorityTargets = _priorityTargetsService as IVsRegisterPriorityCommandTarget;
            var buildManager = _buildManagerService as IVsSolutionBuildManager2;

            Kubuno.Desktop.Views.Options.KubunoViewsOptionsHost.Current = context.GetDialogPage<Kubuno.Desktop.Views.Options.KbviewOptionsPage>();

            // Designer\INTEGRATION.md sections 3/4/6: the split Design|XML editor factory (a classic, package-registered
            // IVsEditorFactory - [ProvideEditorFactory] only emits pkgdef metadata, VS still needs a live instance
            // handed to it via RegisterEditorFactory), its options page's static gateway, and the DSG-7 design surface
            // factory. Must happen before any .kbview document can open through "Open With... > Kubuno View Designer"
            // (which itself loads the package on demand, and waits for its initialization).
            context.RegisterEditorFactory(new Kubuno.Desktop.Designer.EditorFactory.KbviewEditorFactory());
            // The .kbres resource editor (docs/RESOURCES.md).
            context.RegisterEditorFactory(new Kubuno.Desktop.Resources.Editor.KbresEditorFactory());
            Kubuno.Desktop.Designer.Options.DesignerOptionsHost.Current = context.GetDialogPage<Kubuno.Desktop.Designer.Options.KbviewDesignerOptionsPage>();

            // The Properties window resolves the IEventBindingService behind a double-click on an event
            // row through its own service chain, which ends at Visual Studio's global services - found
            // live that the chain does not always reach the active designer's surface (the row then did
            // nothing). Proffered globally too; it only knows .kbview elements, so it is inert for every
            // other designer (whose own designer host answers first anyway).
            context.AddService(
                typeof(System.ComponentModel.Design.IEventBindingService),
                Kubuno.Desktop.Designer.PropertyBrowser.KbviewEventBindingService.Instance,
                promote: true);

            // docs/DESIGNER.md section 11: F7/Shift+F7 switch between a .kbview's designer and its XML.
            if (priorityTargets is not null)
            {
                var viewSwitch = new Kubuno.Desktop.Designer.EditorFactory.DesignerViewSwitchCommandTarget(package);
                ErrorHandler.ThrowOnFailure(priorityTargets.RegisterPriorityCommandTarget(0, viewSwitch, out _viewSwitchCookie));
                // docs/DESIGNER.md "Data bindings": F12 on a bound row of the Properties window.
                ErrorHandler.ThrowOnFailure(priorityTargets.RegisterPriorityCommandTarget(0, new Kubuno.Desktop.Designer.Bindings.BindingCommands.GoToDefinitionTarget(), out _bindingDefinitionCookie));
                _priorityTargets = priorityTargets;
            }

            if (_surfaceExePath is not null)
            {
                var oleServiceProvider = (Microsoft.VisualStudio.OLE.Interop.IServiceProvider)package;
                // docs/DESIGNER.md section 15: each designer renders with its project's own kubuno_ui.dll
                // (a design build against the project); the bundled surface is the fallback.
                _designSurfaceRuntimes = new ProjectDesignSurfaceRuntimeProvider(_surfaceExePath, context.JoinableTaskFactory);
                if (buildManager is not null)
                {
                    _designSurfaceRuntimes.Advise(buildManager);
                }

                Kubuno.Desktop.Designer.DesignSurface.DesignSurfaceHostFactoryHost.Current =
                    new Kubuno.Desktop.Designer.DesignSurface.RustDesignSurfaceHostFactory(_surfaceExePath, oleServiceProvider: oleServiceProvider, runtimeProvider: _designSurfaceRuntimes);
                KubunoLog.WriteLine($"Kubuno: bundled design surface exe resolved at '{_surfaceExePath}'.");
            }
            else
            {
                KubunoLog.WriteLine("Kubuno: kubuno-views-surface.exe not found - the Kubuno View Designer's Design pane will show its placeholder. Build it: cd Z:\\projects\\kubuno\\desktop\\windows ; $env:CARGO_TARGET_DIR='C:\\kubuno-build\\agent-dsgint' ; cargo build --release --example view_embed -p kubuno-views -j 1");
            }

            if (context.CommandService is { } commandService)
            {
                DesignerToolWindowCommands.Initialize(package, commandService);
                PaintDebugCommand.Initialize(commandService);
                AddProjectItemCommands.Initialize(commandService, ItemTemplates);
                Kubuno.Desktop.DataExplorer.ShowDataExplorerCommand.Initialize(commandService);
                Kubuno.Desktop.Migrations.MigrationCommands.Initialize(commandService);
                Kubuno.Desktop.DataSources.ShowDataSourcesCommand.Initialize(commandService);
                // docs/DESIGNER.md "Data bindings": the Properties window's binding entries.
                Kubuno.Desktop.Designer.Bindings.BindingCommands.Initialize(commandService);
            }

            RegisterDialogGalleryEntries();
        }

        public override void InitializeOnIdle(KubunoLayerContext context)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // docs/DESIGNER.md section 11: the Kubuno Toolbox tabs exist only while a .kbview designer is
            // active; drop any a previous session (or version) left in the persisted toolbox.
            Kubuno.Desktop.Designer.Toolbox.NativeToolboxInstaller.UninstallIfLeftBehind();
        }

        public override Task InitializeDeferredAsync(KubunoLayerContext context, CancellationToken cancellationToken)
        {
            // Like RustLayer's crate-name wizard: the item template engine loads the control item wizard by name.
            try
            {
                _ = typeof(ControlItemWizard).Assembly.GetName();
            }
            catch (Exception ex)
            {
                KubunoLog.WriteLine($"Kubuno: could not preload Kubuno.Desktop.TemplateWizard.dll - the control item templates will not name their files ({ex.Message}).");
            }

            return Task.CompletedTask;
        }

        public override void Dispose(KubunoLayerContext context)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _designSurfaceRuntimes?.Dispose();
            _designSurfaceRuntimes = null;

            // docs/DATA.md DATA-5: stop kubuno-data-tool (EOF on its stdin).
            Kubuno.Desktop.DataExplorer.DataToolHost.Shutdown();

            if (_priorityTargets is not null)
            {
                _priorityTargets.UnregisterPriorityCommandTarget(_viewSwitchCookie);
                _priorityTargets.UnregisterPriorityCommandTarget(_bindingDefinitionCookie);
                _priorityTargets = null;
            }
        }

        /// <summary>This layer's dialogs, with sample data, for the dialog gallery (docs/ARCHITECTURE.md, "Themed dialogs").</summary>
        private static void RegisterDialogGalleryEntries() =>
            // A provider: built (the override catalog parsed, the designer and wizard assemblies loaded) only when the
            // gallery opens, never during the package load.
            DialogGallery.Register(DialogGalleryEntries);

        private static IEnumerable<KeyValuePair<string, Func<bool?>>> DialogGalleryEntries()
        {
            var entries = new List<KeyValuePair<string, Func<bool?>>>
            {
                // docs/DATA.md DATA-5 (no helper calls from the gallery: Test and OK only validate).
                new KeyValuePair<string, Func<bool?>>(Kubuno.Desktop.Logic.Data.DataText.AddConnectionTitle + " (AddConnectionDialog)", () => new Kubuno.Desktop.DataExplorer.AddConnectionDialog(new[] { "Shop" }, Kubuno.Desktop.Logic.Data.CredentialStoreKind.CredentialManager, test: null, save: null).ShowModal()),
                // docs/DATA.md DATA-7 (no helper calls from the gallery).
                new KeyValuePair<string, Func<bool?>>(Kubuno.Desktop.Logic.Migrations.MigrationText.AddMigrationTitle + " (AddMigrationDialog)", () => new Kubuno.Desktop.Migrations.AddMigrationDialog("shop-app", "shop").ShowModal()),
                new KeyValuePair<string, Func<bool?>>(Kubuno.Desktop.Logic.Migrations.MigrationText.ConnectionDialogTitle + " (ConnectionNameDialog)", () => new Kubuno.Desktop.Migrations.ConnectionNameDialog(
                    Kubuno.Desktop.Logic.Migrations.MigrationText.ConnectionDialogIntro("shop-app"), new[] { "Shop", "Sales" }, "Shop", userSecretsId: null, listExplorer: null, copyToSecrets: null).ShowModal()),
                // docs/DATA.md DATA-6 (sample connections and schema; Finish only validates).
                new KeyValuePair<string, Func<bool?>>(Kubuno.Desktop.Logic.DataSources.DataSourcesText.WizardTitle + " (DataSourceWizardDialog)", () => new Kubuno.Desktop.DataSources.DataSourceWizardDialog(
                    new Kubuno.Desktop.Logic.DataSources.DataSourceWizardModel(Kubuno.Desktop.DataSources.SampleDataSourceWizardBackend.Connections, new[] { "shop" }, moduleSchema: null),
                    new Kubuno.Desktop.DataSources.SampleDataSourceWizardBackend()).ShowModal()),
            };

            OverrideContext? overrides = OverrideAssistant.Analyze(SampleComponent, SampleComponent.IndexOf("base: Button", StringComparison.Ordinal), OverrideCatalog.Default);
            if (overrides != null)
            {
                entries.Add(new KeyValuePair<string, Func<bool?>>("Substituer des membres…", () => new Kubuno.Desktop.LanguageService.Overrides.OverrideMembersDialog(overrides).ShowModal()));
            }

            entries.AddRange(Kubuno.Desktop.Designer.UI.DesignerDialogGallery.Entries);
            entries.AddRange(Kubuno.Desktop.Resources.Editor.ResourceDialogGallery.Entries);
            entries.AddRange(TemplateWizardDialogGallery.Entries);
            return entries;
        }

        /// <summary>
        /// Forwards <c>Kubuno.Desktop.Views.Logging.IKubunoLog</c> calls to the "Kubuno" pane (<see cref="KubunoLog"/>) -
        /// the seam the views language client and the designer log through (Views\INTEGRATION.md section 5).
        /// </summary>
        private sealed class KubunoLogAdapter : Kubuno.Desktop.Views.Logging.IKubunoLog
        {
            public void WriteLine(string message) => KubunoLog.WriteLine(message);

            public void WriteException(string context, Exception exception) => KubunoLog.WriteException(context, exception);
        }
    }
}
