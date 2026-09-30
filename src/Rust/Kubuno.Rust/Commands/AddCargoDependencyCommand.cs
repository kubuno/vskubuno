using System.ComponentModel.Design;
using Kubuno.Rust.CrateManager;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;

namespace Kubuno.Rust.Commands
{
    /// <summary>
    /// "Dépendance Cargo (crate)..." (the extended "Ajouter" submenu): opens the crate manager
    /// (<see cref="CrateManagerToolWindow"/>) on its Browse tab - the NuGet-like UI that searches
    /// crates.io and applies <c>cargo add</c>, like "Manage NuGet Packages..." in a .NET project.
    /// </summary>
    internal static class AddCargoDependencyCommand
    {
        public static void Initialize(AsyncPackage package, OleMenuCommandService commandService)
        {
            var id = new CommandID(PackageGuids.KubunoCommandSet, PackageIds.AddCargoDependencyCommand);
#pragma warning disable VSTHRD100, VSTHRD010 // both handlers always fire on the UI thread (OleMenuCommand invoke/query events), like GenerateRustProjectsCommand's own suppression.
            var command = new OleMenuCommand((sender, args) => Execute(), id);
            command.BeforeQueryStatus += (sender, args) => OnBeforeQueryStatus((OleMenuCommand)sender!);
#pragma warning restore VSTHRD100, VSTHRD010
            commandService.AddCommand(command);
        }

        private static void OnBeforeQueryStatus(OleMenuCommand command)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            command.Visible = command.Enabled = RsprojSelection.TryGetCurrent() is not null;
        }

        private static void Execute()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var context = RsprojSelection.TryGetCurrent();
            if (context is null)
            {
                return;
            }

#pragma warning disable VSSDK007 // deliberately fire-and-forget from a synchronous command handler; FileAndForget reports any fault to the "Kubuno" pane instead of dropping it silently.
            ThreadHelper.JoinableTaskFactory.RunAsync(() => CrateManagerToolWindow.ShowAsync(context, crateName: null)).FileAndForget("Kubuno/AddCargoDependency");
#pragma warning restore VSSDK007
        }
    }
}
