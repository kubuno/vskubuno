using System.Runtime.InteropServices;
using Kubuno.VisualStudio.Designer.Properties;
using Microsoft.VisualStudio.Shell;

namespace Kubuno.VisualStudio.Designer.ToolWindows
{
    /// <summary>
    /// <c>View &gt; Kubuno Properties</c> (INTEGRATION.md §7): a thin, package-registered
    /// <see cref="ToolWindowPane"/> around <see cref="Properties.PropertiesPanelView"/>, bound to
    /// <see cref="PropertiesToolWindowHost.Current"/> - the SAME <see cref="PropertiesPanelViewModel"/>
    /// instance <see cref="DesignSurface.DesignSurfaceEditingCoordinator"/> drives via
    /// <see cref="Selection.SelectionSyncService"/> once DSG-8's selection sync is wired for an open
    /// <c>.kbview</c> pane (INTEGRATION.md §9 point 5) - see that gateway's own doc for why a lazy
    /// singleton, not a constructor argument, is what makes the sharing work regardless of which of the
    /// two is created first.
    /// </summary>
    [Guid(DesignerConstants.PropertiesToolWindowGuidString)]
    public sealed class PropertiesToolWindow : ToolWindowPane
    {
        public PropertiesToolWindow()
        {
            Caption = "Kubuno Properties";
            Content = new PropertiesPanelView(PropertiesToolWindowHost.Current);
        }
    }
}
