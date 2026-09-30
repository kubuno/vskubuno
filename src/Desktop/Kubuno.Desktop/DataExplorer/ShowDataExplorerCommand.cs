using System.ComponentModel.Design;
using Kubuno.Desktop.Logic.Data;
using Microsoft.VisualStudio.Shell;

namespace Kubuno.Desktop.DataExplorer
{
    /// <summary>
    /// View &gt; "Explorateur de données" (next to Server Explorer) and View &gt; Other Windows: shows the
    /// <see cref="DataExplorerToolWindow"/>. Canonical name for DTE: <c>View.KubunoDataExplorer</c>.
    /// </summary>
    internal static class ShowDataExplorerCommand
    {
        public static void Initialize(OleMenuCommandService commandService)
        {
            var id = new CommandID(PackageGuids.KubunoCommandSet, PackageIds.ShowDataExplorerCommand);
#pragma warning disable VSTHRD010 // OleMenuCommand invoke/query events fire on the UI thread.
            var command = new OleMenuCommand((_, _) => DataUi.RunUi(async () => await DataExplorerToolWindow.ShowAsync(), "Show"), id);
            command.BeforeQueryStatus += (sender, _) => ((OleMenuCommand)sender!).Text = DataText.DataExplorer;
#pragma warning restore VSTHRD010
            commandService.AddCommand(command);
        }
    }
}
