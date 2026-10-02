using System.ComponentModel;

namespace Kubuno.Desktop.Designer.UI
{
    /// <summary>
    /// The view-mode state and its derived pane visibility, kept free of any WPF/VS SDK type so it is
    /// plain-unit-testable (see tests/Kubuno.Desktop.Tests/Designer) without a running Visual
    /// Studio host or an STA thread. <see cref="DesignerSplitView"/> is the only consumer; it owns one
    /// instance and re-applies <see cref="IsDesignPaneVisible"/>/<see cref="IsXmlPaneVisible"/>/
    /// <see cref="IsSplitterVisible"/> to its Grid column widths whenever <see cref="PropertyChanged"/>
    /// fires.
    /// </summary>
    public sealed class DesignerSplitViewModel : INotifyPropertyChanged
    {
        private DesignerViewMode _mode = DesignerViewMode.Split;

        public event PropertyChangedEventHandler? PropertyChanged;

        public DesignerViewMode Mode
        {
            get => _mode;
            set
            {
                if (_mode == value)
                {
                    return;
                }

                _mode = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Mode)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDesignPaneVisible)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsXmlPaneVisible)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSplitterVisible)));
            }
        }

        public bool IsDesignPaneVisible => Mode is DesignerViewMode.Design or DesignerViewMode.Split;

        public bool IsXmlPaneVisible => Mode is DesignerViewMode.Xml or DesignerViewMode.Split;

        /// <summary>Only meaningful (and only shown by <see cref="DesignerSplitView"/>) when both panes are visible.</summary>
        public bool IsSplitterVisible => Mode == DesignerViewMode.Split;
    }
}
