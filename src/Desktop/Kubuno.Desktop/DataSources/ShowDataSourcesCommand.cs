using System.ComponentModel.Design;
using Kubuno.VisualStudio.Core.DataSources;
using Kubuno.VisualStudio.DataExplorer;
using Microsoft.VisualStudio.Shell;

namespace Kubuno.VisualStudio.DataSources
{
    /// <summary>
    /// View &gt; Other Windows &gt; "Sources de données" (Shift+Alt+D, like Visual Studio's own Data Sources window): shows the
    /// <see cref="DataSourcesToolWindow"/>. Canonical name for DTE: <c>View.KubunoDataSources</c>.
    /// </summary>
    internal static class ShowDataSourcesCommand
    {
        public static void Initialize(OleMenuCommandService commandService)
        {
            var id = new CommandID(PackageGuids.KubunoCommandSet, PackageIds.ShowDataSourcesCommand);
#pragma warning disable VSTHRD010 // OleMenuCommand invoke/query events fire on the UI thread.
            var command = new OleMenuCommand((_, _) => DataUi.RunUi(async () => await DataSourcesToolWindow.ShowAsync(), "DataSources/Show"), id);
            command.BeforeQueryStatus += (sender, _) => ((OleMenuCommand)sender!).Text = DataSourcesText.DataSources;
#pragma warning restore VSTHRD010
            commandService.AddCommand(command);
        }
    }
}
