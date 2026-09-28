using System;
using System.ComponentModel.Design;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Cargo.Commands;
using Kubuno.Cargo.Processes;
using Kubuno.VisualStudio.Logging;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;

namespace Kubuno.VisualStudio.Commands
{
    /// <summary>
    /// "Dépendance Cargo (crate)..." (the extended "Ajouter" submenu, and the Dependencies node's own
    /// context menu, <see cref="Kubuno.VisualStudio.SolutionExplorer.DependenciesNodeCommandTarget"/>):
    /// a small VS-styled dialog (<see cref="CargoDependencyDialog"/>) for crate name/version/features/
    /// kind, applied with <c>cargo add</c> - <c>Cargo.toml</c> is never hand-edited (docs/RSPROJ.md
    /// §1, CLAUDE.md §7's "never hand-rewrite the TOML"). cargo's own stdout/stderr goes to the
    /// "Kubuno" Output pane, matching every other Cargo-invoking command in this extension.
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
            ThreadHelper.JoinableTaskFactory.RunAsync(() => ShowDialogAndAddAsync(context)).FileAndForget("Kubuno/AddCargoDependency");
#pragma warning restore VSSDK007
        }

        /// <summary>
        /// Shared by the project-node "Ajouter &gt; Dépendance Cargo (crate)..." command above and
        /// the Dependencies node's own "Dépendance Cargo (crate)..." context-menu entry.
        /// </summary>
        internal static async Task ShowDialogAndAddAsync(RsprojProjectContext context)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var dialog = new CargoDependencyDialog();
            if (dialog.ShowModal() != true || string.IsNullOrEmpty(dialog.CrateName))
            {
                return;
            }

            var spec = string.IsNullOrEmpty(dialog.Version) ? dialog.CrateName : $"{dialog.CrateName}@{dialog.Version}";
            var command = CargoCommand.Add().WithManifestPath(context.ManifestPath).WithExtraArgs(spec);
            if (!string.IsNullOrEmpty(context.PackageName))
            {
                command = command.WithPackage(context.PackageName!);
            }

            if (!string.IsNullOrEmpty(dialog.Features))
            {
                command = command.WithFeatures(dialog.Features!.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries));
            }

            switch (dialog.Kind)
            {
                case CargoDependencyKind.Dev:
                    command = command.WithExtraArgs("--dev");
                    break;
                case CargoDependencyKind.Build:
                    command = command.WithExtraArgs("--build");
                    break;
            }

            await RunAndReportAsync(command, $"cargo add {spec}");
        }

        /// <summary>The Dependencies node's own "Supprimer" (<c>cargo remove</c>).</summary>
        internal static async Task RemoveAsync(RsprojProjectContext context, string crateName)
        {
            var command = CargoCommand.Remove().WithManifestPath(context.ManifestPath).WithExtraArgs(crateName);
            if (!string.IsNullOrEmpty(context.PackageName))
            {
                command = command.WithPackage(context.PackageName!);
            }

            await RunAndReportAsync(command, $"cargo remove {crateName}");
        }

        private static async Task RunAndReportAsync(CargoCommand command, string description)
        {
            await TaskScheduler.Default;

            KubunoLog.WriteLine($"Kubuno: {description}...");
            var line = command.ToCommandLine();
            bool succeeded;
            int exitCode = -1;
            try
            {
                var result = await new ProcessRunner().RunAsync(
                    new ProcessRunRequest(line.FileName, line.Arguments),
                    onOutput: new Progress<ProcessOutputLine>(l => KubunoLog.WriteLine($"Kubuno:   {l.Text}")),
                    CancellationToken.None);
                succeeded = result.Succeeded;
                exitCode = result.ExitCode;
            }
            catch (Exception exception)
            {
                KubunoLog.WriteException($"Kubuno: {description}", exception);
                succeeded = false;
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (!succeeded)
            {
                KubunoLog.WriteLine($"Kubuno:   {description} failed (exit code {exitCode}).");
                VsShellUtilities.ShowMessageBox(
                    ServiceProvider.GlobalProvider,
                    $"{description} a échoué - voir le panneau de sortie \"Kubuno\" pour le détail.",
                    "Kubuno",
                    OLEMSGICON.OLEMSGICON_WARNING,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            }

            // No manual refresh: CollectionSources.ProjectDependenciesSource watches this Cargo.toml
            // and reloads the Dependencies node on its own once cargo has written the change.
        }
    }
}
