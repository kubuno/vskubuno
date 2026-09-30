using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EnvDTE;
using Kubuno.VisualStudio.Core.Migrations;
using Kubuno.VisualStudio.Logging;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace Kubuno.VisualStudio.Migrations
{
    /// <summary>
    /// After a build, "The SQLx cache of &lt;crate&gt; is stale. [Update]" (docs/DATA.md DATA-7) for each crate whose
    /// <c>Migrations</c> node Solution Explorer created and whose cache <c>sqlx.status</c> finds stale. The build itself
    /// already reports it (the <c>data_source!</c> macro's warning in the Error List); this offers the fix. One info bar
    /// per crate at a time; nothing is read for crates whose node was never shown.
    /// </summary>
    internal static class SqlxStaleInfoBar
    {
        private static readonly HashSet<string> Showing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static BuildEvents? _buildEvents;

        public static void Initialize()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_buildEvents != null || ServiceProvider.GlobalProvider.GetService(typeof(DTE)) is not DTE dte)
            {
                return;
            }

            // Kept in a field: DTE event objects stop raising events once collected.
            _buildEvents = dte.Events.BuildEvents;
            _buildEvents.OnBuildDone += (_, _) => MigrationCommands.Start(CheckAsync, "StaleInfoBar");
        }

        private static async Task CheckAsync()
        {
            foreach (var source in MigrationsSource.All())
            {
                if (source.Model is null || source.CrateDirectory is not { } crate)
                {
                    continue;
                }

                await source.ReloadAndWaitAsync();
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (source.Model?.SqlxStale == true)
                {
                    Show(crate);
                }
            }
        }

        private static void Show(string crateDirectory)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (Showing.Contains(crateDirectory)
                || ServiceProvider.GlobalProvider.GetService(typeof(SVsShell)) is not IVsShell shell
                || ErrorHandler.Failed(shell.GetProperty((int)__VSSPROPID7.VSSPROPID_MainWindowInfoBarHost, out var hostObject))
                || hostObject is not IVsInfoBarHost host
                || ServiceProvider.GlobalProvider.GetService(typeof(SVsInfoBarUIFactory)) is not IVsInfoBarUIFactory factory)
            {
                return;
            }

            var crate = CrateDataInfo.Read(crateDirectory).DisplayName;
            var model = new InfoBarModel(
                new[] { new InfoBarTextSpan(MigrationText.StaleInfoBar(crate) + " ") },
                new[] { new InfoBarHyperlink(MigrationText.UpdateAction, crateDirectory) },
                KnownMonikers.StatusWarning,
                isCloseButtonVisible: true);
            var element = factory.CreateInfoBar(model);
            element.Advise(new Events(crateDirectory), out _);
            host.AddInfoBar(element);
            Showing.Add(crateDirectory);
            KubunoLog.WriteLine("Kubuno: " + MigrationText.StaleInfoBar(crate));
        }

        private sealed class Events : IVsInfoBarUIEvents
        {
            private readonly string _crateDirectory;

            public Events(string crateDirectory) => _crateDirectory = crateDirectory;

            public void OnClosed(IVsInfoBarUIElement infoBarUIElement)
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                Showing.Remove(_crateDirectory);
            }

            public void OnActionItemClicked(IVsInfoBarUIElement infoBarUIElement, IVsInfoBarActionItem actionItem)
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                infoBarUIElement.Close();
                MigrationCommands.Start(() => MigrationCommands.PrepareAsync(_crateDirectory), "StaleInfoBar/Prepare");
            }
        }
    }
}
