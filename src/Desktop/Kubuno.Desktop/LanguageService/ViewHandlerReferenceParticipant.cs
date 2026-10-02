using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Shared.Logging;
using Kubuno.Views.Designer.Handlers.Infrastructure;
using Kubuno.Views.LanguageService;
using Kubuno.Rust.Extensibility;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using Newtonsoft.Json.Linq;
using StreamJsonRpc;

namespace Kubuno.Desktop.LanguageService
{
    /// <summary>
    /// A view handler renamed or searched from the Rust editor (F2 / Ctrl+R, R, Find All References on a handler method -
    /// docs/EVENTS.md 5.5, EVT-5): rust-analyzer handles the Rust side, <c>kubuno-views-ls</c> adds the <c>.kbview</c>
    /// attributes naming the handler and its <c>handlers!</c> string (<c>kubuno/renameHandler</c> with <c>rustRenamed</c>:
    /// it leaves the Rust code alone), computed on the open editors' texts. For Find All References, a rename to a probe
    /// name reports exactly the places the views use the handler. Plugged into the Rust layer's middle layer as an
    /// <see cref="IRustReferenceParticipant"/>.
    /// </summary>
    [Export(typeof(IRustReferenceParticipant))]
    internal sealed class ViewHandlerReferenceParticipant : IRustReferenceParticipant
    {
        /// <summary>A name <c>kubuno/renameHandler</c> accepts, used to find a handler's view usages without renaming anything.</summary>
        private const string ProbeName = "kubuno_find_references_probe";

        public Task<RustEditContribution?> GetRenameEditAsync(JToken requestParameters, string newName, CancellationToken cancellationToken) =>
            ViewHandlerEditAsync(requestParameters, newName);

        public Task<RustEditContribution?> GetReferencesAsync(JToken requestParameters, CancellationToken cancellationToken) =>
            ViewHandlerEditAsync(requestParameters, ProbeName);

        /// <summary>
        /// Asks <c>kubuno-views-ls</c> what renaming the handler at the request's position to <paramref name="newName"/> changes
        /// in the views (null when the position is not on a view handler, or the views server is not running).
        /// </summary>
        private static async Task<RustEditContribution?> ViewHandlerEditAsync(JToken methodParam, string newName)
        {
            if (KubunoViewsLanguageClient.Current?.ReadyRpc is not { } rpc)
            {
                return null;
            }

            try
            {
                var uri = (string?)methodParam["textDocument"]?["uri"];
                var position = methodParam["position"];
                if (uri is null || position is null || !uri.EndsWith(".rs", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                var folder = Path.GetDirectoryName(new Uri(Uri.UnescapeDataString(uri)).LocalPath) ?? string.Empty;
                var openFiles = new VsWorkspaceFileHost(ServiceProvider.GlobalProvider).OpenTexts(folder, ".rs");
                await TaskScheduler.Default;
                var result = await rpc.InvokeWithParameterObjectAsync<JToken?>("kubuno/renameHandler", new { uri, position, @new = newName, rustRenamed = true, openFiles }).ConfigureAwait(false);
                return result?["edit"] is JObject edit
                    ? new RustEditContribution(edit, $"handler '{(string?)result["oldName"]}' in the views")
                    : null;
            }
            catch (Exception ex) when (ex is RemoteInvocationException or ConnectionLostException or OperationCanceledException or UriFormatException or ArgumentException)
            {
                KubunoLog.WriteLine("Views: kubuno/renameHandler failed: " + ex.Message);
                return null;
            }
        }
    }
}
