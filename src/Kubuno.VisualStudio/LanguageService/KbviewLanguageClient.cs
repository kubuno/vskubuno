using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.LanguageServer.Client;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Utilities;
using StreamJsonRpc;

namespace Kubuno.VisualStudio.LanguageService
{
    /// <summary>
    /// Re-exports <see cref="Kubuno.VisualStudio.Views.LanguageService.KubunoViewsLanguageClient"/>'s
    /// <see cref="ILanguageClient"/> from THIS assembly (Kubuno.VisualStudio.dll) instead of
    /// Kubuno.VisualStudio.Views.dll.
    ///
    /// Root-caused live in an experimental instance: opening a .kbview file was claimed by VS's
    /// own XML editor (schema errors on the undeclared "x" namespace prefix that this format
    /// deliberately allows), and the "Kubuno" Output pane never logged a single kubuno-views-ls
    /// line - i.e. <c>KubunoViewsLanguageClient.ActivateAsync</c> was never invoked. Duplicating
    /// this assembly's <see cref="ContentDefinition"/> (file-extension -&gt; content-type mapping)
    /// fixed the content type (no more XML errors afterwards), but the <c>ILanguageClient</c>
    /// export itself - declared in Kubuno.VisualStudio.Views.dll, a project-reference assembly
    /// merely copied alongside this VSIX's primary one, registered as its own
    /// <c>Microsoft.VisualStudio.MefComponent</c> VSIX asset (confirmed present and correctly
    /// resolved to a real path in the packaged manifest) - still never activated, even though
    /// Kubuno.TestAdapter.dll's own <c>ITestContainerDiscoverer</c> MEF part, registered the exact
    /// same way, composes and runs correctly. This narrows the problem to
    /// <c>Microsoft.VisualStudio.LanguageServer.Client</c>'s own <c>ILanguageClient</c> discovery
    /// specifically, not general MEF/VSIX-asset wiring (both of which are independently proven
    /// fine here). <see cref="RustLanguageClient"/> - identical attribute shape, identical base
    /// content type, identical two-interface implementation - works reliably from THIS assembly,
    /// so this wrapper moves the export here rather than digging further into the LSP client
    /// host's own assembly-discovery internals. It does no work of its own beyond forwarding: all
    /// actual behavior (locating/starting kubuno-views-ls.exe, logging, options) stays owned by
    /// <see cref="Kubuno.VisualStudio.Views.LanguageService.KubunoViewsLanguageClient"/> - nothing
    /// in Kubuno.VisualStudio.Views/ was changed to make this work, per that library's own
    /// INTEGRATION.md ("what NOT to change on this library's side").
    /// </summary>
    [ContentType(Kubuno.VisualStudio.Views.KbviewConstants.ContentType)]
    [Export(typeof(ILanguageClient))]
    public sealed class KbviewLanguageClient : ILanguageClient, ILanguageClientCustomMessage2
    {
        private readonly Views.LanguageService.KubunoViewsLanguageClient _inner = new();

        public KbviewLanguageClient()
        {
            _inner.StartAsync += async (sender, e) =>
            {
                if (StartAsync is not null)
                {
                    await StartAsync.InvokeAsync(this, e);
                }
            };
            _inner.StopAsync += async (sender, e) =>
            {
                if (StopAsync is not null)
                {
                    await StopAsync.InvokeAsync(this, e);
                }
            };
        }

        public string Name => _inner.Name;

        public IEnumerable<string> ConfigurationSections => _inner.ConfigurationSections;

        public object? InitializationOptions => _inner.InitializationOptions;

        public IEnumerable<string>? FilesToWatch => _inner.FilesToWatch;

        public object? MiddleLayer => _inner.MiddleLayer;

        public object? CustomMessageTarget => _inner.CustomMessageTarget;

        public bool ShowNotificationOnInitializeFailed => _inner.ShowNotificationOnInitializeFailed;

        public JsonRpc? Rpc
        {
            get => _inner.Rpc;
            set => _inner.Rpc = value;
        }

        public event AsyncEventHandler<EventArgs>? StartAsync;

        public event AsyncEventHandler<EventArgs>? StopAsync;

        public Task<Connection?> ActivateAsync(CancellationToken token) => _inner.ActivateAsync(token);

        public Task OnLoadedAsync() => _inner.OnLoadedAsync();

        public Task OnServerInitializedAsync() => _inner.OnServerInitializedAsync();

        public Task<InitializationFailureContext?> OnServerInitializeFailedAsync(ILanguageClientInitializationInfo initializationState) =>
            _inner.OnServerInitializeFailedAsync(initializationState);

        public Task AttachForCustomMessageAsync(JsonRpc rpc) => _inner.AttachForCustomMessageAsync(rpc);

        public Task StopServerAsync() => _inner.StopServerAsync();
    }
}
