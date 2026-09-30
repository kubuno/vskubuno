using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Core.Extensibility;
using Kubuno.Core.Logging;
using Kubuno.Core.UI;
using Kubuno.Rust.Commands;
using Kubuno.Rust.Debugging;
using Kubuno.Rust.Infrastructure;
using Kubuno.Rust.Logic;
using Kubuno.Rust.Logic.SolutionExplorer;
using Kubuno.Rust.Options;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Workspace.VSIntegration.Contracts;

namespace Kubuno.Rust
{
    /// <summary>
    /// The Rust layer's part of the package (docs/ARCHITECTURE.md, "Layers (as built)"): the Kubuno.Rust.Sdk feed, the
    /// .rsproj commands and Project Properties links, rustfmt on save, the Open Folder launch targets. Everything else
    /// of the layer (rust-analyzer, IntelliSense, Cargo workspaces, the Test Explorer adapter, the CPS project type) is
    /// MEF-composed by Visual Studio and needs no package code.
    /// </summary>
    public sealed class RustLayer : KubunoLayer
    {
        /// <summary>The item templates of the extended "Ajouter" submenu of a .rsproj that belong to plain Rust.</summary>
        private static readonly ProjectItemTemplate[] ItemTemplates =
        {
            new ProjectItemTemplate(PackageIds.AddRustModuleCommand, "RustModule", "Module Rust", "Nom du module :", "module", ".rs"),
            new ProjectItemTemplate(PackageIds.AddRustIntegrationTestCommand, "RustIntegrationTest", "Test d'intégration", "Nom du test :", "integration_test", ".rs"),
            new ProjectItemTemplate(PackageIds.AddRustExampleCommand, "RustExample", "Exemple", "Nom de l'exemple :", "example", ".rs"),
            new ProjectItemTemplate(PackageIds.AddRustBinaryCommand, "RustBinary", "Binaire", "Nom du binaire :", "tool", ".rs"),
        };

        private FormatOnSaveDocumentEvents? _formatOnSaveEvents;
        private IVsFolderWorkspaceService? _workspaceService;

        public override string Name => "Rust";

        public override Task InitializeAsync(KubunoLayerContext context, CancellationToken cancellationToken)
        {
            // Awaited here rather than deferred: when the package loads because a .rsproj is opening, that
            // project's Sdk="Kubuno.Rust.Sdk/..." must resolve from the bundled feed. Cached (see the class) -
            // on an unchanged NuGet.Config this is a single small stamp-file read.
            RustSdkFeedInstaller.EnsureRegistered(context.ExtensionDirectory);

            // The Project Properties editor's "Manage crates..." / "Reference Manager..." links
            // (Application page, Dependencies category) open this layer's own UIs.
            Kubuno.Rust.ProjectSystem.ProjectProperties.RustProjectPropertiesHost.OpenCrateManagerAsync = async hierarchy =>
            {
                await context.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (RsprojProjectContext.TryCreate((IVsHierarchy)hierarchy) is { } project)
                {
                    await Kubuno.Rust.CrateManager.CrateManagerToolWindow.ShowAsync(project, crateName: null);
                }
            };
            Kubuno.Rust.ProjectSystem.ProjectProperties.RustProjectPropertiesHost.OpenReferenceManagerAsync = async hierarchy =>
            {
                await context.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (RsprojProjectContext.TryCreate((IVsHierarchy)hierarchy) is { } project)
                {
                    await AddProjectReferenceCommand.ShowReferenceManagerAsync(project);
                }
            };
            return Task.CompletedTask;
        }

        public override void InitializeOnUIThread(KubunoLayerContext context)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var package = context.Package;
            var runningDocumentTable = new RunningDocumentTable(package);
            _formatOnSaveEvents = new FormatOnSaveDocumentEvents(
                runningDocumentTable,
                () =>
                {
                    ThreadHelper.ThrowIfNotOnUIThread();
                    return context.GetDialogPage<RustOptionsPage>();
                },
                () =>
                {
                    ThreadHelper.ThrowIfNotOnUIThread();
                    return ((IServiceProvider)package).GetService(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
                });
            _formatOnSaveEvents.Advise();

            if (context.CommandService is { } commandService)
            {
                DebugRustTestAtCursorCommand.Initialize(package, commandService);
                GenerateRustProjectsCommand.Initialize(package, commandService);
                RestartRustAnalyzerCommand.Initialize(package, commandService);
                AddProjectItemCommands.Initialize(commandService, ItemTemplates);
                AddProjectReferenceCommand.Initialize(package, commandService);
                AddCargoDependencyCommand.Initialize(package, commandService);
            }

            RegisterDialogGalleryEntries();
        }

        public override void InitializeOnIdle(KubunoLayerContext context)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                _workspaceService = context.ComponentModel?.GetService<IVsFolderWorkspaceService>();
            }
            catch (Exception exception)
            {
                KubunoLog.WriteException("Kubuno: the Open Folder workspace service is unavailable", exception);
            }
        }

        public override async Task InitializeDeferredAsync(KubunoLayerContext context, CancellationToken cancellationToken)
        {
            PreloadTemplateWizardAssembly();
            if (_workspaceService != null)
            {
                _workspaceService.OnActiveWorkspaceChanged += OnActiveWorkspaceChangedAsync;
                // The package can finish loading after a folder is already open (e.g. the user
                // reopens the same folder next session): regenerate for whatever is open right now too.
                await RegenerateLaunchTargetsForCurrentWorkspaceAsync();
                await EnsureWorkspaceSettingsExcludeNonRustProjectsAsync();
            }
        }

        public override void Dispose(KubunoLayerContext context)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _formatOnSaveEvents?.Dispose();
            _formatOnSaveEvents = null;
            if (_workspaceService != null)
            {
                _workspaceService.OnActiveWorkspaceChanged -= OnActiveWorkspaceChangedAsync;
                _workspaceService = null;
            }
        }

        /// <summary>This layer's dialogs, with sample data, for the dialog gallery (docs/ARCHITECTURE.md, "Themed dialogs").</summary>
        private static void RegisterDialogGalleryEntries()
        {
            DialogGallery.Register(new[]
            {
                new KeyValuePair<string, Func<bool?>>("Ajouter › (NewItemNameDialog)", () => new NewItemNameDialog("Ajouter - Vue Kubuno", "Nom :", "MainView").ShowModal()),
                new KeyValuePair<string, Func<bool?>>(DependenciesText.ReferenceManagerTitle("Sample"), () => new ReferenceManagerDialog("Sample", new[]
                {
                    new ReferenceCandidate("kubuno-ui", @"C:\src\desktop\windows\src\crates\kubuno-ui\Cargo.toml", isReferenced: true),
                    new ReferenceCandidate("kubuno-controls", @"C:\src\desktop\windows\src\crates\kubuno-controls\Cargo.toml", isReferenced: false),
                    new ReferenceCandidate("helper", @"C:\src\helper\Cargo.toml", isReferenced: false),
                }).ShowModal()),
                new KeyValuePair<string, Func<bool?>>(DependenciesText.RemoveUnusedTitle, () => new UnusedDependenciesDialog("cargo-machete", new[] { "itoa", "serde_json", "once_cell" }).ShowModal()),
            });
            DialogGallery.Register(Kubuno.Rust.ProjectSystem.ProjectProperties.RustProjectSystemDialogGallery.Entries);
        }

        /// <summary>
        /// Forces Kubuno.Rust.TemplateWizard.dll into this AppDomain's assembly cache before any "Create a new
        /// project" wizard can run. docs/RSPROJ.md Addendum (lot 7), "Crate-name casing, revisited": live-verified that
        /// Microsoft.VisualStudio.TemplateWizard.Wizard.CreateManagedInstance's own plain
        /// <c>Assembly.Load(AssemblyName)</c> call could not find it even with the (since replaced, see the comment at
        /// the top of KubunoPackage.cs) per-assembly code base registered - a separate, undocumented resolution path.
        /// The package's binding path should now cover it too; loading it eagerly (on a background thread, once the
        /// solution is loaded) is kept as a belt-and-braces measure: once an assembly of a given identity is already
        /// loaded into the AppDomain, the CLR's own assembly-identity cache satisfies any later <c>Assembly.Load</c>
        /// for the same identity without re-resolving it, regardless of which subsystem asks. Never allowed to fail -
        /// a wizard that cannot compute <c>$cratename$</c> still leaves every template usable via VS's own
        /// <c>$safeprojectname$</c> fallback (see each .vstemplate's own remarks).
        /// </summary>
        private static void PreloadTemplateWizardAssembly()
        {
            try
            {
                _ = typeof(Kubuno.Rust.TemplateWizard.CrateNameWizard).Assembly.GetName();
            }
            catch (Exception ex)
            {
                KubunoLog.WriteLine($"Kubuno: could not preload Kubuno.Rust.TemplateWizard.dll - $cratename$/$moduleid$ template substitution will not be available ({ex.Message}).");
            }
        }

        private async Task OnActiveWorkspaceChangedAsync(object? sender, EventArgs e)
        {
            await RegenerateLaunchTargetsForCurrentWorkspaceAsync();
            await EnsureWorkspaceSettingsExcludeNonRustProjectsAsync();
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

            var manifestPath = CargoWorkspaceLocator.FindWorkspaceRoot(workspaceRoot, Directory.Exists, File.Exists);
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
        /// <c>ProjectSettings.json</c>. Best-effort, like every workspace-open step:
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
    }
}
