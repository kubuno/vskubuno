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
            ThreadHelper.JoinableTaskFactory.RunAsync(RefreshFromLiveRegistryAsync).FileAndForget("Kubuno/Designer/ToolboxRegistry");
#pragma warning restore VSSDK007
        }

        /// <summary>
        /// Resolves the live <see cref="KubunoViewsLanguageClient"/>'s <c>Rpc</c> the same way
        /// <see cref="DesignSurface.DesignSurfaceEditingCoordinator"/> does (MEF, via
        /// <see cref="SComponentModel"/>) and rebuilds <see cref="Content"/> from the fetched registry.
        /// Best-effort: any failure (no component model yet, the language client not attached yet, the
        /// RPC call itself failing) leaves the empty toolbox in place rather than throwing - the same
        /// "degrade to no-op" posture every other live-RPC caller in this library follows.
        /// </summary>
        private async System.Threading.Tasks.Task RefreshFromLiveRegistryAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                if (Package is not IServiceProvider serviceProvider ||
                    serviceProvider.GetService(typeof(SComponentModel)) is not IComponentModel componentModel)
                {
                    return;
                }

                var client = componentModel.DefaultExportProvider.GetExportedValues<ILanguageClient>().OfType<KubunoViewsLanguageClient>().FirstOrDefault();
                var registry = await JsonRpcRegistryClient.FetchAsync(client?.ReadyRpc, CancellationToken.None);
                if (registry.Components.Count > 0)
                {
                    Content = new ToolboxView(new ToolboxViewModel(registry));
                }
            }
            catch (Exception ex)
            {
                KubunoViewsLogHost.Current.WriteException("[designer] Toolbox: failed to load the live kubuno/registry", ex);
            }
        }
    }
}
