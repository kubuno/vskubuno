using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Shared.Logging;
using Kubuno.Views.Designer.Handlers;
using Kubuno.Views.Designer.Registry;
using Kubuno.Web.Logic.WebDesigner;

namespace Kubuno.Web.WebDesigner
{
    /// <summary>
    /// What the web surface answers instead of <c>kubuno-views-ls</c>'s desktop profile (<see cref="Kubuno.Views.Designer.DesignSurface.IDesignSurfaceLanguageServices"/>):
    /// the web registry of the project - host elements + the project's own controls - for a language server without the web
    /// profile (WV-7; the designer asks <c>kubuno/registry {uri}</c> first and uses this only when the server answered with the
    /// desktop registry), and the code-behind file; the TypeScript handlers are the language server's.
    /// </summary>
    internal sealed partial class WebDesignSurfaceHost
    {
        private (string Stamp, ComponentRegistry Registry)? _registryCache;

        /// <summary>The last registry built (null before the designer asked for it).</summary>
        internal ComponentRegistry? Registry => _registryCache?.Registry;

        /// <summary>Null: the language server's web profile creates the TypeScript handlers (<c>kubuno/createHandler</c>, WV-7).</summary>
        public IKubunoViewsLanguageServerClient? HandlerClient => null;

        public string? CodeBehindPath => Project.CodeBehindOf(DocumentPath);

        public Task<ComponentRegistry?> FetchRegistryAsync(CancellationToken cancellationToken)
        {
            var files = new List<string>();
            if (Project.HostRegistryPath is { } host)
            {
                files.Add(host);
            }

            files.AddRange(Project.RegistryPaths);
            var stamp = string.Join("|", files.Select(f => f + "@" + Stamp(f)));
            if (_registryCache is { } cached && cached.Stamp == stamp)
            {
                return Task.FromResult<ComponentRegistry?>(cached.Registry);
            }

            var web = WebRegistry.Load(Project);
            if (web.HostCount == 0)
            {
                KubunoLog.WriteLine($"[web-designer] no host element registry found for {Project.Root} (kubuno.views.json \"hostRegistry\", or node_modules/@kubuno/ui/kbview-registry.web.json): the language server's registry is used.");
                return Task.FromResult<ComponentRegistry?>(null);
            }

            var registry = ComponentRegistry.FromJson(web.ComponentsJson);
            registry.Version = web.Version;
            registry.Target = "web";
            registry.ProjectComponentsJson = web.ProjectComponentsJson;
            _registryCache = (stamp, registry);
            KubunoLog.WriteLine($"[web-designer] web registry of {Path.GetFileName(Project.Root)}: {web.HostCount} host element(s), {web.ProjectCount} project control(s) (version {web.Version}).");
            return Task.FromResult<ComponentRegistry?>(registry);
        }

        private static string Stamp(string file)
        {
            try
            {
                return File.Exists(file) ? File.GetLastWriteTimeUtc(file).Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) : "missing";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return "error";
            }
        }
    }
}
