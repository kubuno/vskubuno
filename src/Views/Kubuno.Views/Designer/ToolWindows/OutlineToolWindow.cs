using System.Runtime.InteropServices;
using Kubuno.Desktop.Designer.Outline;
using Microsoft.VisualStudio.Shell;

namespace Kubuno.Desktop.Designer.ToolWindows
{
    /// <summary>
    /// <c>View &gt; Other Windows &gt; Kubuno View Outline</c> (INTEGRATION.md §9 point 6: "the same
    /// shape §7 point 1 above already asks for the Toolbox/Properties tool windows"): a thin,
    /// package-registered <see cref="ToolWindowPane"/> around <see cref="Outline.OutlineView"/>, bound to
    /// <see cref="OutlineToolWindowHost.Current"/> - the SAME <see cref="OutlineViewModel"/> instance
    /// <see cref="DesignSurface.DesignSurfaceEditingCoordinator"/> passes to
    /// <see cref="Selection.SelectionSyncService"/>'s optional <c>outline</c> argument. Starts empty
    /// (<see cref="OutlineViewModel.Roots"/>'s own default) until a designer pane's own
    /// <c>textDocument/documentSymbol</c> round trip populates it.
    /// </summary>
    [Guid(DesignerConstants.OutlineToolWindowGuidString)]
    public sealed class OutlineToolWindow : ToolWindowPane
    {
        public OutlineToolWindow()
        {
            Caption = "Kubuno View Outline";
            Content = new OutlineView(OutlineToolWindowHost.Current);
        }
    }
}
