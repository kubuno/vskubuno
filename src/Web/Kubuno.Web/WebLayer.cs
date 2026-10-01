using Kubuno.Core.Extensibility;
using Kubuno.Web.Commands;
using Microsoft.VisualStudio.Shell;

namespace Kubuno.Web
{
    /// <summary>
    /// The web layer's part of the package (docs/WEB.md): the Tools menu commands of the Kubuno core and web modules -
    /// web solution generation (single repository and multi-repository), the version tools, module packaging. F5 is
    /// Kubuno.Web.ProjectSystem's debug launch provider (a MEF part, no package code); the build is Kubuno.Web.Sdk's.
    /// </summary>
    public sealed class WebLayer : KubunoLayer
    {
        public override string Name => "Web";

        public override void InitializeOnUIThread(KubunoLayerContext context)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (context.CommandService is { } commandService)
            {
                WebSolutionCommands.Initialize(context.Package, commandService);
                VersionCommands.Initialize(context.Package, commandService);
                PackModuleCommand.Initialize(context.Package, commandService);
                DevDatabaseTunnelCommand.Initialize(context.Package, commandService);
            }

            // SPIKE (docs/WEB-VIEWS.md, WV-9a): the WebView2 design surface in a document pane, for .kbwebspike files
            // (registered by KubunoPackage's [ProvideEditorFactory]; Visual Studio needs the live instance too).
            context.RegisterEditorFactory(new WebDesigner.Spike.WebDesignSpikeEditorFactory());

            // The dialog gallery (docs/ARCHITECTURE.md, "Themed dialogs"): built only when the gallery opens.
            Kubuno.Core.UI.DialogGallery.Register("Kubuno Core Web: Multi-Repository Solution (RepositoryPickerDialog)", () => new RepositoryPickerDialog(@"Z:\src", new[]
            {
                ("core", "Kubuno Core Web (the server)", true),
                ("calendar", "module calendar", false),
                ("drive", "module drive", true),
            }).ShowModal());

            // The "Kubuno Core Web Module" template's wizard is loaded by the template engine with a plain Assembly.Load:
            // have it in the AppDomain already (same reason as the Rust and Desktop layers' wizards).
            try
            {
                _ = typeof(Kubuno.Web.TemplateWizard.ModuleWizard).Assembly.GetName();
            }
            catch (System.IO.FileNotFoundException exception)
            {
                Kubuno.Core.Logging.KubunoLog.WriteLine("Kubuno web: could not preload Kubuno.Web.TemplateWizard.dll (" + exception.Message + ").");
            }
        }
    }
}
