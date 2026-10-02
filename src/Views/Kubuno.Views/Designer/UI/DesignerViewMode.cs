namespace Kubuno.Views.Designer.UI
{
    /// <summary>
    /// The three states of the Design/XML orientation toggle (docs/DESIGNER.md §1: "modelled on the
    /// XAML designer's own split ... rather than WinForms' separate-tab model"). <see cref="Split"/> is
    /// the initial mode - both panes are text end to end (docs/DESIGNER.md §1: "a .kbview file is XML
    /// text end to end"), so hiding either by default would hide real information, not just a
    /// convenience view.
    /// </summary>
    public enum DesignerViewMode
    {
        Design,
        Xml,
        Split,
    }
}
