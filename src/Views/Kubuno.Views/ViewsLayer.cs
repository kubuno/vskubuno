using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Shared.Extensibility;
using Kubuno.Shared.Logging;
using Kubuno.Shared.UI;
using Kubuno.Views.Designer.ToolWindows;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace Kubuno.Views
{
    /// <summary>
    /// The views layer's part of the package (docs/ARCHITECTURE.md, "Layers (as built)"; docs/WEB-VIEWS.md WV-8): what
    /// every Kubuno target that has <c>.kbview</c> views shares - the <c>.kbview</c> language client's host seams, the
    /// Kubuno View Designer (editor factory, F7 switch, Properties events and data bindings, Toolbox, Outline) and the
    /// <c>.kbres</c> resource editor. The renderer of the Design pane is not here: a target layer plugs its
    /// <see cref="Designer.DesignSurface.IDesignSurfaceHostFactory"/> into
    /// <see cref="Designer.DesignSurface.DesignSurfaceHostFactoryHost"/> (the desktop layer, the Rust <c>view_embed</c>
    /// surface; the web layer, a WebView2 page). Listed before the targets by <c>KubunoPackage.CreateLayers</c>, so the
    /// designer's seams are set before a target's own initialization.
    /// </summary>
    public sealed class ViewsLayer : KubunoLayer
    {
        private IVsRegisterPriorityCommandTarget? _priorityTargets;
        private object? _priorityTargetsService;
        private uint _viewSwitchCookie;
        private uint _bindingDefinitionCookie;

        public override string Name => "Views";

        public override async Task InitializeAsync(KubunoLayerContext context, CancellationToken cancellationToken)
        {
            // The .kbview language client and the designer log through IKubunoLog (their own seam, kept for tests);
            // it must be set before any .kbview document can open.
            Logging.KubunoViewsLogHost.Current = new KubunoLogAdapter();

            // Fetched here, cast (a COM QueryInterface) on the UI thread.
            _priorityTargetsService = await context.GetServiceAsync(typeof(SVsRegisterPriorityCommandTarget));
        }

        public override void InitializeOnUIThread(KubunoLayerContext context)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var package = context.Package;
            var priorityTargets = _priorityTargetsService as IVsRegisterPriorityCommandTarget;

            Options.KubunoViewsOptionsHost.Current = context.GetDialogPage<Options.KbviewOptionsPage>();

            // Designer\INTEGRATION.md sections 3/4/6: the split Design|XML editor factory (a classic, package-registered
            // IVsEditorFactory - [ProvideEditorFactory] only emits pkgdef metadata, VS still needs a live instance
            // handed to it via RegisterEditorFactory) and its options page's static gateway. Must happen before any
            // .kbview document can open through "Open With... > Kubuno View Designer" (which itself loads the package
            // on demand, and waits for its initialization).
            context.RegisterEditorFactory(new Designer.EditorFactory.KbviewEditorFactory());
            // The .kbres resource editor (docs/RESOURCES.md).
            context.RegisterEditorFactory(new Resources.Editor.KbresEditorFactory());
            // The .kbsettings settings editor (docs/STORAGE-COMPONENTS.md).
            context.RegisterEditorFactory(new Settings.Editor.KbsettingsEditorFactory());
            Designer.Options.DesignerOptionsHost.Current = context.GetDialogPage<Designer.Options.KbviewDesignerOptionsPage>();

            // The Properties window resolves the IEventBindingService behind a double-click on an event
            // row through its own service chain, which ends at Visual Studio's global services - found
            // live that the chain does not always reach the active designer's surface (the row then did
            // nothing). Proffered globally too; it only knows .kbview elements, so it is inert for every
            // other designer (whose own designer host answers first anyway).
            context.AddService(
                typeof(System.ComponentModel.Design.IEventBindingService),
                Designer.PropertyBrowser.KbviewEventBindingService.Instance,
                promote: true);

            // docs/DESIGNER.md section 11: F7/Shift+F7 switch between a .kbview's designer and its XML.
            if (priorityTargets is not null)
            {
                var viewSwitch = new Designer.EditorFactory.DesignerViewSwitchCommandTarget(package);
                ErrorHandler.ThrowOnFailure(priorityTargets.RegisterPriorityCommandTarget(0, viewSwitch, out _viewSwitchCookie));
                // docs/DESIGNER.md "Data bindings": F12 on a bound row of the Properties window.
                ErrorHandler.ThrowOnFailure(priorityTargets.RegisterPriorityCommandTarget(0, new Designer.Bindings.BindingCommands.GoToDefinitionTarget(), out _bindingDefinitionCookie));
                _priorityTargets = priorityTargets;
            }

            if (context.CommandService is { } commandService)
            {
                DesignerToolWindowCommands.Initialize(package, commandService);
                // docs/DESIGNER.md "Data bindings": the Properties window's binding entries.
                Designer.Bindings.BindingCommands.Initialize(commandService);
            }

            // A provider: built (the designer assemblies loaded) only when the gallery opens, never during the package load.
            DialogGallery.Register(DialogGalleryEntries);
        }

        public override void InitializeOnIdle(KubunoLayerContext context)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // docs/DESIGNER.md section 11: the Kubuno Toolbox tabs exist only while a .kbview designer is
            // active; drop any a previous session (or version) left in the persisted toolbox.
            Designer.Toolbox.NativeToolboxInstaller.UninstallIfLeftBehind();
        }

        public override void Dispose(KubunoLayerContext context)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_priorityTargets is not null)
            {
                _priorityTargets.UnregisterPriorityCommandTarget(_viewSwitchCookie);
                _priorityTargets.UnregisterPriorityCommandTarget(_bindingDefinitionCookie);
                _priorityTargets = null;
            }
        }

        /// <summary>This layer's dialogs, with sample data, for the dialog gallery (docs/ARCHITECTURE.md, "Themed dialogs").</summary>
        private static IEnumerable<KeyValuePair<string, Func<bool?>>> DialogGalleryEntries()
        {
            var entries = new List<KeyValuePair<string, Func<bool?>>>();
            entries.AddRange(Designer.UI.DesignerDialogGallery.Entries);
            entries.AddRange(Resources.Editor.ResourceDialogGallery.Entries);
            return entries;
        }

        /// <summary>
        /// Forwards <see cref="Logging.IKubunoLog"/> calls to the "Kubuno" pane (<see cref="KubunoLog"/>) - the seam the
        /// views language client and the designer log through (INTEGRATION.md section 5).
        /// </summary>
        private sealed class KubunoLogAdapter : Logging.IKubunoLog
        {
            public void WriteLine(string message) => KubunoLog.WriteLine(message);

            public void WriteException(string context, Exception exception) => KubunoLog.WriteException(context, exception);
        }
    }
}
