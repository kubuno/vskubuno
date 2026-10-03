using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Shared.Extensibility;
using Kubuno.Shared.Logging;
using Kubuno.Shared.UI;
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
    /// The desktop layer's part of the package (docs/ARCHITECTURE.md, "Layers (as built)"): the Design pane's renderer of
    /// the Kubuno View Designer (the Rust view_embed surface, rendering with the project's own kubuno_desktop_ui - plugged into the
    /// views layer's designer through DesignSurfaceHostFactoryHost), the Toolbox icons of the Kubuno controls, the data
    /// tooling and paint debug commands, the view and control entries of the "Ajouter" submenu, and the rest of what a
    /// Kubuno desktop application needs from the package. Its MEF parts (the Rust-side .kbview IntelliSense, SQL in Rust
    /// strings, override members) are composed by Visual Studio. The designer itself, the .kbview language client and the
    /// .kbres editor are the views layer's (Kubuno.Views.ViewsLayer, initialized before this one).
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
            "use kubuno_desktop_views::prelude::*;\n\n#[derive(Component, Default)]\n#[kubuno(extends = Button)]\npub struct RoundButton {\n    base: Button,\n}\n";

        private string? _surfaceExePath;
        private object? _buildManagerService;
        private ProjectDesignSurfaceRuntimeProvider? _designSurfaceRuntimes;

        public override string Name => "Desktop";

        public override async Task InitializeAsync(KubunoLayerContext context, CancellationToken cancellationToken)
        {
            // The Toolbox icons of the Kubuno controls (Kubuno.Desktop.ProjectSystem's KubunoControls.imagemanifest).
            Kubuno.Views.Designer.Toolbox.NativeToolboxInstaller.IconName =
                tag => ControlIcons.IdFor(tag) == ControlIcons.FallbackId ? "Control" : tag;
            Kubuno.Views.Designer.Toolbox.NativeToolboxInstaller.IconAssembly = "Kubuno.Desktop.ProjectSystem";

            _surfaceExePath = KubunoViewsSurfaceLocator.Locate(context.ExtensionDirectory, devBuildDirectory: @"C:\kubuno-build\agent-dsgint\release\examples");

            // Fetched here, cast (a COM QueryInterface) on the UI thread.
            _buildManagerService = _surfaceExePath is null ? null : await context.GetServiceAsync(typeof(SVsSolutionBuildManager));
        }

        public override void InitializeOnUIThread(KubunoLayerContext context)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var package = context.Package;
            var buildManager = _buildManagerService as IVsSolutionBuildManager2;

            // docs/DESIGNER.md section 15 (DSG-7): the Design pane of the views layer's designer renders with the Rust
            // view_embed surface - this layer's IDesignSurfaceHostFactory, plugged in through DesignSurfaceHostFactoryHost.
            if (_surfaceExePath is not null)
            {
                var oleServiceProvider = (Microsoft.VisualStudio.OLE.Interop.IServiceProvider)package;
                // docs/DESIGNER.md section 15: each designer renders with its project's own kubuno_desktop_ui build
                // (a design build against the project); the bundled surface is the fallback.
                _designSurfaceRuntimes = new ProjectDesignSurfaceRuntimeProvider(_surfaceExePath, context.JoinableTaskFactory);
                if (buildManager is not null)
                {
                    _designSurfaceRuntimes.Advise(buildManager);
                }

                Kubuno.Views.Designer.DesignSurface.DesignSurfaceHostFactoryHost.Current =
                    new Kubuno.Desktop.Designer.DesignSurface.RustDesignSurfaceHostFactory(_surfaceExePath, oleServiceProvider: oleServiceProvider, runtimeProvider: _designSurfaceRuntimes);
                KubunoLog.WriteLine($"Kubuno: bundled design surface exe resolved at '{_surfaceExePath}'.");
            }
            else
            {
                KubunoLog.WriteLine("Kubuno: kubuno-views-surface.exe not found - the Kubuno View Designer's Design pane will show its placeholder. Build it: cd Z:\\projects\\kubuno\\desktop\\windows ; $env:CARGO_TARGET_DIR='C:\\kubuno-build\\agent-dsgint' ; cargo build --release --example view_embed -p kubuno-desktop-views -j 1");
            }

            if (context.CommandService is { } commandService)
            {
                PaintDebugCommand.Initialize(commandService);
                AddProjectItemCommands.Initialize(commandService, ItemTemplates);
                Kubuno.Desktop.DataExplorer.ShowDataExplorerCommand.Initialize(commandService);
                Kubuno.Desktop.Migrations.MigrationCommands.Initialize(commandService);
                Kubuno.Desktop.DataSources.ShowDataSourcesCommand.Initialize(commandService);
            }

            RegisterDialogGalleryEntries();
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

            entries.AddRange(TemplateWizardDialogGallery.Entries);
            return entries;
        }
    }
}
