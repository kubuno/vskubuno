using System;
using System.IO;
using Kubuno.Shared.Logging;
using Kubuno.Views.Designer.DesignSurface;
using Kubuno.Web.Logic.WebDesigner;

namespace Kubuno.Web.WebDesigner
{
    /// <summary>
    /// The web layer's design surfaces (docs/WEB-VIEWS.md section 4.1, WV-9b): claims the <c>.kbview</c> / <c>.kbcontrol</c>
    /// files of web projects (<see cref="WebViewProject.Find"/>) - the others stay the desktop's Rust surface - and gives each
    /// pane a <see cref="WebDesignSurfaceHost"/>. Added to <see cref="DesignSurfaceHostFactoryHost"/> by <see cref="WebLayer"/>.
    /// </summary>
    internal sealed class WebDesignSurfaceHostFactory : ITargetDesignSurfaceHostFactory
    {
        /// <summary>Overrides the bundled design host's folder (a development build of <c>@kubuno/host-runtime</c>).</summary>
        public const string BundledFolderVariable = "KUBUNO_WEB_DESIGN_HOST";

        public bool Handles(DesignSurfaceDocument document) => WebViewProject.Find(document.Path) is not null;

        public IDesignSurfaceHost Create(DesignSurfaceDocument? document)
        {
            var project = document is null ? null : WebViewProject.Find(document.Path);
            if (document is null || project is null)
            {
                // Not a web view after all (moved meanwhile): the default surface.
                return DesignSurfaceHostFactoryHost.Current.Create(document);
            }

            KubunoLog.WriteLine($"[web-designer] {Path.GetFileName(document.Path)}: web project {project.Root}.");
            return new WebDesignSurfaceHost(document, project, BundledFolder());
        }

        /// <summary><c>tools\web-design-host\</c> beside the extension's assemblies (the <c>@kubuno/host-runtime</c> build), or the override.</summary>
        internal static string BundledFolder()
        {
            if (Environment.GetEnvironmentVariable(BundledFolderVariable) is { Length: > 0 } folder)
            {
                return folder;
            }

            var dir = Path.GetDirectoryName(typeof(WebDesignSurfaceHostFactory).Assembly.Location) ?? string.Empty;
            return Path.Combine(dir, "tools", "web-design-host");
        }
    }
}
