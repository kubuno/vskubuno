using System;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.PlatformUI;

namespace Kubuno.VisualStudio.Designer.Toolbox
{
    /// <summary>
    /// The toolbox tool window's content: a search box over a scrollable, family-grouped list of
    /// components (docs/DESIGNER.md §1). No .xaml file, matching the rest of this repo (see the csproj's
    /// own top comment) - built entirely in code, themed via <see cref="EnvironmentColors"/> dynamic
    /// resource keys so it tracks VS's light/dark/high-contrast theme automatically (the same
    /// <c>SetResourceReference</c> idiom every VS tool window uses; there is no prior example of it in
    /// this library, since nothing before DSG-4 needed VS-theme-aware chrome). Drag-and-drop onto the
    /// design surface (docs/DESIGNER.md §1/§9) is out of this package's scope - <see cref="ItemActivated"/>
    /// is the seam a later package hooks a drag source (or a plain click-to-insert fallback) onto.
    /// </summary>
    public sealed class ToolboxView : UserControl
    {
        private readonly ToolboxViewModel _viewModel;
        private readonly TextBox _searchBox;
        private readonly StackPanel _listPanel;
        private bool _updatingSearchBoxFromViewModel;

        public ToolboxView(ToolboxViewModel viewModel)
        {
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

            var root = new DockPanel();
            root.SetResourceReference(BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);

            _searchBox = new TextBox
            {
                Margin = new Thickness(4),
                Padding = new Thickness(2),
            };
            _searchBox.SetResourceReference(BackgroundProperty, EnvironmentColors.ComboBoxBackgroundBrushKey);
            _searchBox.SetResourceReference(ForegroundProperty, EnvironmentColors.ComboBoxTextBrushKey);
            _searchBox.SetResourceReference(Control.BorderBrushProperty, EnvironmentColors.ComboBoxBorderBrushKey);
            _searchBox.TextChanged += OnSearchBoxTextChanged;
            DockPanel.SetDock(_searchBox, Dock.Top);
            root.Children.Add(_searchBox);

            _listPanel = new StackPanel();
            var scrollViewer = new ScrollViewer
            {
                Content = _listPanel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            };
            root.Children.Add(scrollViewer);

            Content = root;

            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            Rebuild();
        }

        /// <summary>Raised when a toolbox entry is clicked (a stand-in for a drag gesture until a later package wires real drag-and-drop, so the toolbox is still usable via click alone).</summary>
        public event EventHandler<ToolboxItemViewModel>? ItemActivated;

        private void OnSearchBoxTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_updatingSearchBoxFromViewModel)
            {
                return;
            }

            _viewModel.SearchText = _searchBox.Text;
        }

        private void OnViewModelPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ToolboxViewModel.Families))
            {
                Rebuild();
            }
        }

        private void Rebuild()
        {
            _listPanel.Children.Clear();

            if (_viewModel.Families.Count == 0)
            {
                _listPanel.Children.Add(BuildEmptyPlaceholder());
                return;
            }

            foreach (var family in _viewModel.Families)
            {
                var header = new TextBlock
                {
                    Text = family.Family,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(4, 8, 4, 2),
                };
                header.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
                _listPanel.Children.Add(header);

                foreach (var item in family.Items)
                {
                    _listPanel.Children.Add(BuildItemRow(item));
                }
            }
        }

        /// <summary>
        /// Shown instead of an empty list when <see cref="ToolboxViewModel.Families"/> has nothing to
        /// offer - either no <c>.kbview</c> file is active yet (the language server has no document to
        /// query <c>kubuno/registry</c> against) or the search text matched nothing. A blank scroll area
        /// looked broken/crashed rather than merely "nothing to show yet" (see this tool window's own
        /// task history) - this tells the developer what to do next instead.
        /// </summary>
        private TextBlock BuildEmptyPlaceholder()
        {
            var text = new TextBlock
            {
                Text = "Open a .kbview file to see its components here.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(8, 16, 8, 8),
                Opacity = 0.75,
            };
            text.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            return text;
        }

        private Button BuildItemRow(ToolboxItemViewModel item)
        {
            var button = new Button
            {
                Content = item.DisplayName,
                ToolTip = item.Doc,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(8, 1, 4, 1),
                Padding = new Thickness(4, 2, 4, 2),
                Tag = item,
            };
            button.SetResourceReference(BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
            button.SetResourceReference(ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            button.SetResourceReference(Control.BorderBrushProperty, EnvironmentColors.ToolWindowBorderBrushKey);
            button.Click += (_, _) => ItemActivated?.Invoke(this, item);
            return button;
        }

        /// <summary>Updates the search box's text without re-triggering <see cref="ToolboxViewModel.SearchText"/> - used only if a caller sets <see cref="ToolboxViewModel.SearchText"/> directly (e.g. from a test or a "clear search" command) and wants the box to reflect it.</summary>
        public void SyncSearchBoxFromViewModel()
        {
            _updatingSearchBoxFromViewModel = true;
            try
            {
                _searchBox.Text = _viewModel.SearchText;
            }
            finally
            {
                _updatingSearchBoxFromViewModel = false;
            }
        }
    }
}
