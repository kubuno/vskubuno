using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Kubuno.VisualStudio.Designer.Registry;
using Kubuno.VisualStudio.Designer.Registry.Infrastructure;
using Kubuno.VisualStudio.Designer.Toolbox;
using Kubuno.VisualStudio.Views.LanguageService;
using Kubuno.VisualStudio.Views.Logging;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.LanguageServer.Client;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;

namespace Kubuno.VisualStudio.Designer.ToolWindows
{
    /// <summary>
    /// <c>View &gt; Kubuno Toolbox</c> (INTEGRATION.md §7): a thin, package-registered
    /// <see cref="ToolWindowPane"/> around <see cref="Toolbox.ToolboxView"/>. Starts with
    /// <see cref="ComponentRegistry.Empty"/> (that type's own doc: "a safe placeholder") and refreshes
    /// itself from the live <c>kubuno/registry</c> RPC (<see cref="JsonRpcRegistryClient"/>) once the
    /// tool window is actually created (<see cref="OnToolWindowCreated"/>, when <see cref="Package"/> is
    /// first available) - not in the constructor, which VS calls with no services available yet
    /// (<c>ToolWindowPane</c>'s own documented parameterless-constructor contract).
    /// </summary>
    [Guid(DesignerConstants.ToolboxToolWindowGuidString)]
    public sealed class ToolboxToolWindow : ToolWindowPane
    {
        /// <summary>
        /// Retry budget for <see cref="RefreshFromLiveRegistryAsync"/> - mirrors
        /// <see cref="NativeToolboxInstaller"/>'s own icon-retry precedent (20 attempts, 1.5 s apart,
        /// 30 s total): this tool window can be recreated by Visual Studio's persisted window layout
        /// BEFORE any <c>.kbview</c> document (and therefore <see cref="KubunoViewsLanguageClient"/>)
        /// exists for the session - e.g. the developer had it docked open from a previous session, so
        /// <see cref="OnToolWindowCreated"/> fires at startup, well before the language client attaches
        /// or finishes <c>initialize</c>. A single attempt at that moment finds no client/no
        /// <see cref="KubunoViewsLanguageClient.ReadyRpc"/>, gets <see cref="ComponentRegistry.Empty"/>
        /// back, and - because nothing here re-triggers - the tool window is then stuck on its
        /// placeholder text for the rest of the session even once a <c>.kbview</c> becomes the active
        /// document (real bug: reported live as "Kubuno Toolbox" only ever showing "Open a .kbview file
        /// to see its components here." despite a designer being active).
        /// </summary>
        private const int MaxRegistryFetchAttempts = 20;

        private static readonly TimeSpan RegistryFetchRetryDelay = TimeSpan.FromMilliseconds(1500);

        public ToolboxToolWindow()
        {
            Caption = "Kubuno Toolbox";
            Content = new ToolboxView(new ToolboxViewModel(ComponentRegistry.Empty));
        }

        public override void OnToolWindowCreated()
        {
            base.OnToolWindowCreated();
            // VSSDK007 wants a package-owned JoinableTaskFactory (AsyncPackage.JoinableTaskFactory, e.g.
            // KubunoPackage.InitializeAsync's own identical fire-and-forget pattern for the MCP bridge
            // start) rather than the static ThreadHelper.JoinableTaskFactory a ToolWindowPane has no
            // package-instance alternative to - FileAndForget already reports any exception to the
            // "Kubuno" Output pane, so nothing here is silently dropped.
#pragma warning disable VSSDK007
            ThreadHelper.JoinableTaskFactory.RunAsync(RefreshFromLiveRegistryWithRetryAsync).FileAndForget("Kubuno/Designer/ToolboxRegistry");
#pragma warning restore VSSDK007
        }

        /// <summary>
        /// Retries <see cref="RefreshFromLiveRegistryAsync"/> for up to <see cref="MaxRegistryFetchAttempts"/>
        /// (see that constant's own doc for why one attempt is not enough), stopping as soon as one
        /// attempt actually populates the Toolbox. Every attempt is best-effort on its own, so a
        /// transient failure never aborts the retry loop early.
        /// </summary>
        private async System.Threading.Tasks.Task RefreshFromLiveRegistryWithRetryAsync()
        {
            for (var attempt = 1; attempt <= MaxRegistryFetchAttempts; attempt++)
            {
                if (await RefreshFromLiveRegistryAsync().ConfigureAwait(true))
                {
                    return;
                }

                await System.Threading.Tasks.Task.Delay(RegistryFetchRetryDelay).ConfigureAwait(true);
            }

            KubunoViewsLogHost.Current.WriteLine(
                $"[designer] Toolbox: kubuno/registry still empty after {MaxRegistryFetchAttempts} attempt(s); " +
                "the Toolbox keeps its placeholder until a .kbview is opened and this tool window is recreated.");
        }

        /// <summary>
        /// Resolves the live <see cref="KubunoViewsLanguageClient"/>'s <c>Rpc</c> the same way
        /// <see cref="DesignSurface.DesignSurfaceEditingCoordinator"/> does (MEF, via
        /// <see cref="SComponentModel"/>) and rebuilds <see cref="Content"/> from the fetched registry.
        /// Best-effort: any failure (no component model yet, the language client not attached yet, the
        /// RPC call itself failing) leaves the empty toolbox in place rather than throwing - the same
        /// "degrade to no-op" posture every other live-RPC caller in this library follows.
        /// </summary>
        /// <returns><see langword="true"/> once the Toolbox was actually populated (the caller stops retrying).</returns>
        private async System.Threading.Tasks.Task<bool> RefreshFromLiveRegistryAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                if (Package is not IServiceProvider serviceProvider ||
                    serviceProvider.GetService(typeof(SComponentModel)) is not IComponentModel componentModel)
                {
                    return false;
                }

                var client = componentModel.DefaultExportProvider.GetExportedValues<ILanguageClient>().OfType<KubunoViewsLanguageClient>().FirstOrDefault();
                var registry = await JsonRpcRegistryClient.FetchAsync(client?.ReadyRpc, CancellationToken.None);
                if (registry.Components.Count > 0)
                {
                    Content = new ToolboxView(new ToolboxViewModel(registry));
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                KubunoViewsLogHost.Current.WriteException("[designer] Toolbox: failed to load the live kubuno/registry", ex);
                return false;
            }
        }
    }
}
