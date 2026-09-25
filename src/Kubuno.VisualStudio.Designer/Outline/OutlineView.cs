using System;
using System.ComponentModel;
using System.Windows.Controls;
using Microsoft.VisualStudio.PlatformUI;

namespace Kubuno.VisualStudio.Designer.Outline
{
    /// <summary>
    /// The Document Outline tool window's content: a plain <see cref="TreeView"/> over
    /// <see cref="OutlineViewModel.Roots"/> (docs/DESIGNER.md §1). No .xaml file, matching the rest of
    /// this repo (see the csproj's own top comment) - built entirely in code, themed via
    /// <see cref="EnvironmentColors"/> the same way <see cref="Toolbox.ToolboxView"/> already is.
    ///
    /// Not unit-tested (a live WPF visual tree, same reasoning <see cref="Toolbox.ToolboxView"/>/
    /// <see cref="Properties.PropertiesPanelView"/> already give for staying out of
    /// tests/Kubuno.VisualStudio.Designer.Tests - only <see cref="OutlineViewModel"/>/
    /// <see cref="DocumentSymbolTreeBuilder"/> are).
    /// </summary>
    public sealed class OutlineView : UserControl
    {
        private readonly OutlineViewModel _viewModel;
        private readonly TreeView _tree;
        private bool _updatingSelectionFromViewModel;

        public OutlineView(OutlineViewModel viewModel)
        {
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

            _tree = new TreeView();
            _tree.SetResourceReference(BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
            _tree.SetResourceReference(ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            _tree.SelectedItemChanged += OnSelectedItemChanged;
            Content = _tree;

            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            Rebuild();
        }

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(OutlineViewModel.Roots))
            {
                Rebuild();
            }
        }

        private void Rebuild()
        {
            // Guards against TreeViewItem.IsSelected=true (set below, from OutlineViewModel's own
            // highlight state) bubbling up as a spurious SelectedItemChanged while the tree is being torn
            // down and rebuilt - that must never look like a fresh user click.
            _updatingSelectionFromViewModel = true;
            try
            {
                _tree.Items.Clear();
                foreach (var root in _viewModel.Roots)
                {
                    _tree.Items.Add(BuildItem(root));
                }
            }
            finally
            {
                _updatingSelectionFromViewModel = false;
            }
        }

        private TreeViewItem BuildItem(OutlineNodeViewModel node)
        {
            var header = string.IsNullOrEmpty(node.Detail) ? node.DisplayName : $"{node.DisplayName} ({node.Detail})";
            var item = new TreeViewItem
            {
                Header = header,
                Tag = node,
                IsExpanded = true,
                IsSelected = node.IsSelected,
            };
            item.SetResourceReference(ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);

            foreach (var child in node.Children)
            {
                item.Items.Add(BuildItem(child));
            }

            return item;
        }

        private void OnSelectedItemChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<object> e)
        {
            if (_updatingSelectionFromViewModel)
            {
                return;
            }

            if (e.NewValue is TreeViewItem { Tag: OutlineNodeViewModel node })
            {
                _updatingSelectionFromViewModel = true;
                try
                {
                    _viewModel.ActivateNode(node.ElementId);
                }
                finally
                {
                    _updatingSelectionFromViewModel = false;
                }
            }
        }
    }
}
