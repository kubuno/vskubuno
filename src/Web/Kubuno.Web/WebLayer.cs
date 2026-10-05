using Kubuno.Shared.Extensibility;
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

            WebStartupProject.Initialize();

            // docs/WEB-VIEWS.md WV-9b: the views of web projects open in the shared Kubuno View Designer with a WebView2
            // surface (the project's dev server, else the bundled design host); every other view keeps the desktop's.
            Kubuno.Views.Designer.DesignSurface.DesignSurfaceHostFactoryHost.AddTargetFactory(new WebDesigner.WebDesignSurfaceHostFactory());

            // The dialog gallery (docs/ARCHITECTURE.md, "Themed dialogs"): built only when the gallery opens.
            Kubuno.Shared.UI.DialogGallery.Register("Kubuno Web: Multi-Repository Solution (RepositoryPickerDialog)", () => new RepositoryPickerDialog(@"Z:\src", new[]
            {
                ("core", "Kubuno Core (the server)", true),
                ("calendar", "module calendar", false),
                ("drive", "module drive", true),
            }).ShowModal());

            // The "Kubuno Web Module" template's wizard is loaded by the template engine with a plain Assembly.Load:
            // have it in the AppDomain already (same reason as the Rust and Desktop layers' wizards).
            try
            {
                _ = typeof(Kubuno.Web.TemplateWizard.ModuleWizard).Assembly.GetName();
            }
            catch (System.IO.FileNotFoundException exception)
            {
                Kubuno.Shared.Logging.KubunoLog.WriteLine("Kubuno web: could not preload Kubuno.Web.TemplateWizard.dll (" + exception.Message + ").");
            }
        }
    }
}
