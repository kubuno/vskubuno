using System.Collections.Generic;
using System.ComponentModel;

namespace Kubuno.VisualStudio.Designer.Outline
{
    /// <summary>One row of the Document Outline tree view - a thin WPF-binding wrapper around <see cref="OutlineNode"/>, adding only the mutable <see cref="IsSelected"/> flag <see cref="OutlineViewModel"/> drives.</summary>
    public sealed class OutlineNodeViewModel : INotifyPropertyChanged
    {
        private bool _isSelected;

        public OutlineNodeViewModel(OutlineNode node, IReadOnlyList<OutlineNodeViewModel> children)
        {
            Node = node;
            Children = children;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public OutlineNode Node { get; }

        public string ElementId => Node.ElementId;

        public string DisplayName => Node.Name;

        public string? Detail => Node.Detail;

        public IReadOnlyList<OutlineNodeViewModel> Children { get; }

        /// <summary>Driven by <see cref="OutlineViewModel.Select"/> (docs/DESIGNER.md §8's third sync participant) - never set directly from a click; <see cref="OutlineView"/>'s own <c>TreeView</c> selection is what a click drives instead.</summary>
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                {
                    return;
                }

                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
    }
}
