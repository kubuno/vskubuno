using System;
using System.ComponentModel.Design;
using System.Globalization;
using Kubuno.Rust.LanguageService;
using Kubuno.Core.Logging;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;

namespace Kubuno.Rust.Commands
{
    /// <summary>
    /// Tools &gt; "Kubuno: Restart rust-analyzer" ("Kubuno : Redémarrer rust-analyzer" in a French Visual
    /// Studio): restarts the Rust language server without closing the solution
    /// (<see cref="RustLanguageClient.RestartAsync"/>). Disabled until a .rs file has activated the client.
    /// </summary>
    internal static class RestartRustAnalyzerCommand
    {
        public static void Initialize(AsyncPackage package, OleMenuCommandService commandService)
        {
            var id = new CommandID(PackageGuids.KubunoCommandSet, PackageIds.RestartRustAnalyzerCommand);
#pragma warning disable VSTHRD010, VSSDK007 // OleMenuCommand invoke/query events fire on the UI thread; the restart is fire-and-forget with its own logging.
            var command = new OleMenuCommand((sender, args) => Execute(package), id);
            command.BeforeQueryStatus += (sender, args) =>
            {
                var menuCommand = (OleMenuCommand)sender!;
                menuCommand.Enabled = RustLanguageClient.Instance is not null;
                menuCommand.Text = IsFrench ? "Kubuno : Redémarrer rust-analyzer" : "Kubuno: Restart rust-analyzer";
            };
#pragma warning restore VSTHRD010, VSSDK007
            commandService.AddCommand(command);
        }

        private static bool IsFrench => string.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "fr", StringComparison.OrdinalIgnoreCase);

        private static void Execute(AsyncPackage package)
        {
            var client = RustLanguageClient.Instance;
            if (client is null)
            {
                KubunoLog.WriteLine("rust-analyzer is not running yet (open a .rs file first); nothing to restart.");
                return;
            }

            package.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    await client.RestartAsync();
                }
                catch (Exception exception)
                {
                    KubunoLog.WriteException("Restarting rust-analyzer failed", exception);
                }
            }).FileAndForget("Kubuno/RestartRustAnalyzer");
        }
    }
}
