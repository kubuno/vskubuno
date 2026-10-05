using System.Threading;
using System.Threading.Tasks;
using Kubuno.Views.Designer.Handlers;
using Kubuno.Views.Designer.Registry;

namespace Kubuno.Views.Designer.DesignSurface
{
    /// <summary>
    /// What a design surface host of another target provides instead of <c>kubuno-views-ls</c>'s desktop answers
    /// (docs/WEB-VIEWS.md section 5): the component registry of the document's target and the creation of event handlers in
    /// its code-behind language. Optional: a host without it (the desktop's) uses the language server for both. Each member may
    /// return <see langword="null"/> to fall back on the language server for that request - which is how a host steps aside
    /// once the language server serves its target (the web profile of <c>kubuno-views-ls</c>, WV-7).
    /// </summary>
    public interface IDesignSurfaceLanguageServices
    {
        /// <summary>The registry of the document's target (Toolbox, Properties window, ⚡ events), or null to ask <c>kubuno/registry</c>.</summary>
        Task<ComponentRegistry?> FetchRegistryAsync(CancellationToken cancellationToken);

        /// <summary>Creates event handlers in the document's code-behind (double-click, ⚡ events), or null to use <c>kubuno/createHandler</c>.</summary>
        IKubunoViewsLanguageServerClient? HandlerClient { get; }

        /// <summary>The document's code-behind file (where a handler is looked for when the registry has none), or null for the default (<c>.rs</c>).</summary>
        string? CodeBehindPath { get; }
    }
}
