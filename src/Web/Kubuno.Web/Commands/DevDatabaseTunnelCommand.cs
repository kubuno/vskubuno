using System.ComponentModel.Design;
using System.Threading.Tasks;
using Kubuno.Shared;
using Kubuno.Shared.Logging;
using Kubuno.Shared.Remote;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;

namespace Kubuno.Web.Commands
{
    /// <summary>
    /// Tools > "Kubuno Core Web: Open Development Database Tunnel" (docs/WEB.md, "The development database"): opens (or
    /// reuses) the SSH tunnel F5 opens, without starting the core - for the Data Explorer, psql or sqlx on
    /// <c>localhost:55432</c>. Same settings, same info bars; the tunnel ends with Visual Studio.
    /// </summary>
    internal static class DevDatabaseTunnelCommand
    {
        public static void Initialize(AsyncPackage package, OleMenuCommandService commandService)
        {
#pragma warning disable VSTHRD010 // menu command handlers are always invoked on the UI thread.
            commandService.AddCommand(new OleMenuCommand((_, _) => Run(package), new CommandID(KubunoGuids.CommandSet, PackageIds.OpenDevDatabaseTunnelCommand)));
#pragma warning restore VSTHRD010
        }

        private static void Run(AsyncPackage package)
        {
            _ = package.JoinableTaskFactory.RunAsync(async () =>
            {
                await package.JoinableTaskFactory.SwitchToMainThreadAsync();
                var options = RemoteHostOptionsPage.Current;
                var settings = options.ToSettings();
                var localPort = options.EffectiveTunnelLocalPort;
                var remotePort = options.EffectiveRemoteDatabasePort;
                KubunoLog.Activate();
                KubunoLog.WriteLine("Kubuno remote: opening the development database tunnel to " + settings.Destination + "...");
                await TaskScheduler.Default;
                var result = await SshTunnels.EnsureAsync(settings, localPort, remotePort, package.DisposalToken).ConfigureAwait(false);
                await package.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (result.CommandLine.Length > 0)
                {
                    KubunoLog.WriteLine("Kubuno remote: ssh " + result.CommandLine);
                }

                if (result.IsOpen)
                {
                    RemoteHostInfoBar.Close();
                    KubunoLog.WriteLine("Kubuno remote: " + result.Message);
                    return;
                }

                KubunoLog.WriteLine("Kubuno remote: " + result.Message);
                if (result.ErrorOutput.Trim() is { Length: > 0 } errors)
                {
                    KubunoLog.WriteLine("Kubuno remote: ssh said: " + errors.Replace(System.Environment.NewLine, " | "));
                }

                RemoteHostInfoBar.ShowFailure(result, settings);
            });
        }
    }
}
