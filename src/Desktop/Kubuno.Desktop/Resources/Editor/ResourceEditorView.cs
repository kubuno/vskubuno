using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Kubuno.Core.UI;
using Kubuno.Desktop.Logic.Resources;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;

// A WPF control: every member runs on the UI thread of its dispatcher (the pane is created and used there only).
#pragma warning disable VSTHRD010

namespace Kubuno.Desktop.Resources.Editor
{
    /// <summary>
    /// The WPF surface of the resource editor (like Visual Studio's .resx editor): a toolbar, a grid for strings and
    /// other text resources (Name, Value, Comment and one column per culture) or a thumbnail view for images, icons,
    /// audio and files, a status line and a banner for an invalid file. Built in code; only theme brushes. All
    /// logic is in <see cref="ResourceSetModel"/> and <see cref="ResourceGridModel"/>.
    /// </summary>
    internal sealed class ResourceEditorView : UserControl
    {
        private const double TileWidth = 112;

        private readonly IResourceEditorHost _host;
        private readonly Dictionary<string, ImageSource?> _thumbnails = new Dictionary<string, ImageSource?>();

        private readonly ComboBox _categoryBox = new ComboBox { MinWidth = 110, Margin = new Thickness(0, 0, 8, 0) };
        private readonly Button _addButton = new Button { Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 4, 0) };
        private readonly Button _removeButton = new Button { Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 8, 0) };
        private readonly TextBlock _persistenceLabel = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
        private readonly ComboBox _persistenceBox = new ComboBox { MinWidth = 150, Margin = new Thickness(0, 0, 8, 0) };
        private readonly Button _cultureButton = new Button { Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 8, 0) };
        private readonly ToggleButton _showCultures = new ToggleButton { Padding = new Thickness(8, 2, 8, 2), IsChecked = true };
        private readonly Border _banner = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(8, 4, 8, 4), Visibility = Visibility.Collapsed };
        private readonly TextBlock _bannerText = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        private readonly DataGrid _grid = new DataGrid();
        private readonly ListView _tiles = new ListView();
        private readonly TextBlock _emptyHint = new TextBlock { TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(24), Visibility = Visibility.Collapsed };
        private readonly TextBlock _details = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(8, 4, 8, 2) };
        private readonly TextBlock _status = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(8, 2, 8, 4) };

        private ResourceSetModel? _model;
        private ResourceGridModel? _gridModel;
        private ResourceCategory _category = ResourceCategory.Strings;
        private List<string> _builtCultures = new List<string>();
        private bool _refreshQueued;
        private bool _updatingUi;
        private string? _editNameAfterRefresh;
        private bool _readOnly;

        public ResourceEditorView(IResourceEditorHost host)
        {
            _host = host;
            ThemedControls.AddImplicitStyles(Resources);
            AllowDrop = true;
            Focusable = false;
            UseLayoutRounding = true;
            this.SetResourceReference(BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
            this.SetResourceReference(ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);

            BuildToolbarContent();
            BuildGrid();
            BuildTiles();

            _details.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.SystemGrayTextBrushKey);
            _status.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.SystemGrayTextBrushKey);
            _emptyHint.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.SystemGrayTextBrushKey);

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var toolbar = new Border { Padding = new Thickness(6), BorderThickness = new Thickness(0, 0, 0, 1) };
            toolbar.SetResourceReference(Border.BackgroundProperty, EnvironmentColors.CommandBarGradientBeginBrushKey);
            toolbar.SetResourceReference(Border.BorderBrushProperty, EnvironmentColors.ToolWindowBorderBrushKey);
            var bar = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var element in new UIElement[] { _categoryBox, _addButton, _removeButton, _persistenceLabel, _persistenceBox, _cultureButton, _showCultures })
            {
                if (element is FrameworkElement fe && fe.VerticalAlignment == VerticalAlignment.Stretch)
                {
                    fe.VerticalAlignment = VerticalAlignment.Center;
                }

                bar.Children.Add(element);
            }

            toolbar.Child = bar;
            Grid.SetRow(toolbar, 0);
            root.Children.Add(toolbar);

            _banner.SetResourceReference(Border.BackgroundProperty, VsBrushes.InfoBackgroundKey);
            _banner.SetResourceReference(Border.BorderBrushProperty, EnvironmentColors.ToolWindowBorderBrushKey);
            _bannerText.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.InfoTextKey);
            var viewCode = new Button { Content = ResourceText.ViewCode, Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            viewCode.Click += (_, _) => _host.ViewCode();
            var bannerPanel = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(viewCode, Dock.Right);
            bannerPanel.Children.Add(viewCode);
            bannerPanel.Children.Add(_bannerText);
            _banner.Child = bannerPanel;
            Grid.SetRow(_banner, 1);
            root.Children.Add(_banner);

            var content = new Grid();
            content.Children.Add(_grid);
            content.Children.Add(_tiles);
            content.Children.Add(_emptyHint);
            Grid.SetRow(content, 2);
            root.Children.Add(content);

            var footer = new StackPanel();
            footer.Children.Add(_details);
            footer.Children.Add(_status);
            Grid.SetRow(footer, 3);
            root.Children.Add(footer);
            Content = root;

            WireEvents();
            ApplyCategoryUi();
        }

        /// <summary>True while a text box (a cell being edited) has the keyboard: Undo/Redo/Delete then belong to the text.</summary>
        public bool IsEditingText => Keyboard.FocusedElement is TextBox box && IsVisualDescendant(box);

        public bool HasSelection => SelectedNames().Count > 0;

        public bool CanUndo => !_readOnly && _model?.CanUndo == true;

        public bool CanRedo => !_readOnly && _model?.CanRedo == true;

        /// <summary>Attaches the model to edit (or detaches it with null).</summary>
        public void SetModel(ResourceSetModel? model)
        {
            if (_model is not null)
            {
                _model.Changed -= OnModelChanged;
            }

            _model = model;
            _gridModel = null;
            _builtCultures = new List<string>();
            if (model is not null)
            {
                model.Changed += OnModelChanged;
                var first = model.Entries.Count > 0 ? ResourceSetModel.CategoryOf(model.Entries[0].Kind) : ResourceCategory.Strings;
                _category = model.InCategory(ResourceCategory.Strings).Any() || model.Entries.Count == 0 ? ResourceCategory.Strings : first;
            }

            ApplyCategoryUi();
            RefreshNow();
        }

        public void Undo()
        {
            if (CanUndo)
            {
                _grid.CancelEdit();
                _model!.Undo();
            }
        }

        public void Redo()
        {
            if (CanRedo)
            {
                _grid.CancelEdit();
                _model!.Redo();
            }
        }

        public void RemoveSelected()
        {
            if (_model is null || _readOnly)
            {
                return;
            }

            var names = SelectedNames();
            if (names.Count == 0 || !_host.Confirm(ResourceText.ConfirmRemove(names.Count, names[0])))
            {
                return;
            }

            _grid.CancelEdit();
            foreach (var name in names)
            {
                _model.Remove(name);
            }
        }

        // ---- Construction ----

        private void BuildToolbarContent()
        {
            foreach (var (label, category) in new[]
            {
                (ResourceText.Strings, ResourceCategory.Strings),
                (ResourceText.Images, ResourceCategory.Images),
                (ResourceText.Icons, ResourceCategory.Icons),
                (ResourceText.Audio, ResourceCategory.Audio),
                (ResourceText.Files, ResourceCategory.Files),
                (ResourceText.Other, ResourceCategory.Other),
            })
            {
                _categoryBox.Items.Add(new ComboBoxItem { Content = label, Tag = category });
            }

            _categoryBox.SelectedIndex = 0;
            System.Windows.Automation.AutomationProperties.SetName(_categoryBox, ResourceText.EditorTitle);

            _addButton.Content = ResourceText.AddResource + " ▾";
            _removeButton.Content = ResourceText.RemoveResource;
            _persistenceLabel.Text = ResourceText.Persistence;
            _persistenceBox.Items.Add(new ComboBoxItem { Content = ResourceText.Linked, Tag = ResourcePersistence.Linked });
            _persistenceBox.Items.Add(new ComboBoxItem { Content = ResourceText.Embedded, Tag = ResourcePersistence.Embedded });
            System.Windows.Automation.AutomationProperties.SetName(_persistenceBox, ResourceText.Persistence);
            _cultureButton.Content = ResourceText.AddCulture;
            _showCultures.Content = ResourceText.ShowCultures;
        }

        private void BuildGrid()
        {
            _grid.AutoGenerateColumns = false;
            _grid.CanUserAddRows = false;
            _grid.CanUserDeleteRows = false;
            _grid.CanUserResizeRows = false;
            _grid.CanUserReorderColumns = false;
            _grid.SelectionMode = DataGridSelectionMode.Extended;
            _grid.SelectionUnit = DataGridSelectionUnit.FullRow;
            _grid.HeadersVisibility = DataGridHeadersVisibility.Column;
            _grid.GridLinesVisibility = DataGridGridLinesVisibility.All;
            _grid.BorderThickness = new Thickness(0);
            _grid.ClipboardCopyMode = DataGridClipboardCopyMode.None;
            _grid.SetResourceReference(Control.BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
            _grid.SetResourceReference(Control.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            _grid.SetResourceReference(DataGrid.HorizontalGridLinesBrushProperty, EnvironmentColors.ToolWindowBorderBrushKey);
            _grid.SetResourceReference(DataGrid.VerticalGridLinesBrushProperty, EnvironmentColors.ToolWindowBorderBrushKey);

            var header = new Style(typeof(DataGridColumnHeader));
            header.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 3, 6, 3)));
            header.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 1, 1)));
            header.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Left));
            header.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension(EnvironmentColors.GridHeadingBackgroundBrushKey)));
            header.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension(EnvironmentColors.GridHeadingTextBrushKey)));
            header.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension(EnvironmentColors.ToolWindowBorderBrushKey)));
            _grid.ColumnHeaderStyle = header;

            var row = new Style(typeof(DataGridRow));
            row.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            var rowSelected = new Trigger { Property = DataGridRow.IsSelectedProperty, Value = true };
            rowSelected.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension(EnvironmentColors.SystemHighlightBrushKey)));
            rowSelected.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension(EnvironmentColors.SystemHighlightTextBrushKey)));
            row.Triggers.Add(rowSelected);
            _grid.RowStyle = row;

            var cell = new Style(typeof(DataGridCell));
            cell.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            cell.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            cell.Setters.Add(new Setter(UIElement.FocusableProperty, true));
            _grid.CellStyle = cell;

            _grid.PreviewKeyDown += OnGridPreviewKeyDown;
            _grid.SelectionChanged += (_, _) => UpdateToolbarState();
            _grid.ContextMenuOpening += OnGridContextMenuOpening;
        }

        private void BuildTiles()
        {
            _tiles.SelectionMode = SelectionMode.Extended;
            _tiles.BorderThickness = new Thickness(0);
            _tiles.SetResourceReference(Control.BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
            _tiles.SetResourceReference(Control.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            ScrollViewer.SetHorizontalScrollBarVisibility(_tiles, ScrollBarVisibility.Disabled);
            _tiles.ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(WrapPanel)));
            _tiles.SelectionChanged += (_, _) =>
            {
                UpdateToolbarState();
                UpdateDetails();
            };
            _tiles.PreviewKeyDown += OnTilesPreviewKeyDown;
        }

        private void WireEvents()
        {
            _categoryBox.SelectionChanged += (_, _) =>
            {
                if (_updatingUi || _categoryBox.SelectedItem is not ComboBoxItem { Tag: ResourceCategory category })
                {
                    return;
                }

                SetCategory(category);
            };
            _addButton.Click += (_, _) => ShowAddMenu();
            _removeButton.Click += (_, _) => RemoveSelected();
            _cultureButton.Click += (_, _) => AddCulture();
            _showCultures.Click += (_, _) => ApplyCultureVisibility();
            _persistenceBox.SelectionChanged += (_, _) =>
            {
                if (!_updatingUi && _persistenceBox.SelectedItem is ComboBoxItem { Tag: ResourcePersistence persistence })
                {
                    ApplyPersistence(persistence);
                }
            };

            DragOver += (_, e) =>
            {
                e.Effects = !_readOnly && _model is not null && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
                e.Handled = true;
            };
            Drop += OnDrop;
        }

        // ---- Model changes ----

        private void OnModelChanged(object? sender, EventArgs e)
        {
            if (_refreshQueued)
            {
                return;
            }

            // Deferred: an edit committed from a grid cell raises this while the grid is still committing.
            _refreshQueued = true;
#pragma warning disable VSTHRD001, VSTHRD110 // a plain dispatcher hop on the UI thread, as in the designer.
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                _refreshQueued = false;
                RefreshNow();
            }));
#pragma warning restore VSTHRD001, VSTHRD110
        }

        private void Post(Action action)
        {
#pragma warning disable VSTHRD001, VSTHRD110
            Dispatcher.BeginInvoke(DispatcherPriority.Background, action);
#pragma warning restore VSTHRD001, VSTHRD110
        }

        private void RefreshNow()
        {
            var model = _model;
            _readOnly = model is null || model.Diagnostics.Any(d => d.IsError);
            _grid.IsReadOnly = _readOnly;
            if (model is null)
            {
                _grid.ItemsSource = null;
                _tiles.Items.Clear();
                UpdateBanner();
                UpdateToolbarState();
                _status.Text = string.Empty;
                return;
            }

            if (IsGridCategory(_category))
            {
                if (_gridModel is null || _gridModel.Category != _category)
                {
                    _gridModel = new ResourceGridModel(model, _category);
                    _gridModel.Error += (_, message) => Post(() => _host.ShowError(message));
                    _grid.ItemsSource = _gridModel.Rows;
                }
                else
                {
                    _gridModel.Refresh();
                }

                if (!_builtCultures.SequenceEqual(model.Cultures) || _grid.Columns.Count == 0)
                {
                    BuildColumns();
                }

                if (_editNameAfterRefresh is { } name)
                {
                    _editNameAfterRefresh = null;
                    BeginEditName(name);
                }
            }
            else
            {
                RebuildTiles();
            }

            UpdateBanner();
            UpdateStatus();
            UpdateToolbarState();
            UpdateDetails();
        }

        private void UpdateBanner()
        {
            var error = _model?.Diagnostics.FirstOrDefault(d => d.IsError);
            _banner.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
            if (error is not null)
            {
                _bannerText.Text = ResourceText.InvalidFile(error.Message, error.Line);
            }
        }

        private void UpdateStatus()
        {
            if (_model is null)
            {
                return;
            }

            var parts = new List<string> { ResourceText.Entries(_model.Entries.Count) };
            foreach (var culture in _model.Cultures)
            {
                parts.Add(ResourceText.CultureStatus(culture, _model.MissingTranslations(culture).Count));
            }

            _status.Text = string.Join("  |  ", parts);
        }

        private void UpdateToolbarState()
        {
            var editable = _model is not null && !_readOnly;
            var binary = !IsGridCategory(_category);
            var selected = SelectedNames();
            _addButton.IsEnabled = editable;
            _removeButton.IsEnabled = editable && selected.Count > 0;
            _cultureButton.IsEnabled = editable;
            _showCultures.IsEnabled = _model is not null && IsGridCategory(_category);
            _persistenceBox.IsEnabled = editable && binary && selected.Count > 0;
            _persistenceLabel.Opacity = _persistenceBox.IsEnabled ? 1 : 0.5;

            _updatingUi = true;
            try
            {
                var first = binary && selected.Count > 0 ? _model?.Get(selected[0]) : null;
                _persistenceBox.SelectedIndex = first is null ? -1 : first.Persistence == ResourcePersistence.Embedded ? 1 : 0;
            }
            finally
            {
                _updatingUi = false;
            }
        }

        private void UpdateDetails()
        {
            var selected = SelectedNames();
            if (_model is null || IsGridCategory(_category) || selected.Count == 0 || _model.Get(selected[0]) is not { } entry)
            {
                _details.Text = string.Empty;
                return;
            }

            _details.Text = ResourceDetails.Of(_model, entry).ToText(entry.Name).Replace("\n", "   |   ");
        }

        // ---- Categories ----

        private static bool IsGridCategory(ResourceCategory category) => category is ResourceCategory.Strings or ResourceCategory.Other;

        private void SetCategory(ResourceCategory category)
        {
            _grid.CancelEdit();
            _category = category;
            ApplyCategoryUi();
            RefreshNow();
        }

        private void ApplyCategoryUi()
        {
            var grid = IsGridCategory(_category);
            _grid.Visibility = grid ? Visibility.Visible : Visibility.Collapsed;
            _tiles.Visibility = grid ? Visibility.Collapsed : Visibility.Visible;
            _updatingUi = true;
            try
            {
                foreach (ComboBoxItem item in _categoryBox.Items)
                {
                    if (item.Tag is ResourceCategory c && c == _category)
                    {
                        _categoryBox.SelectedItem = item;
                    }
                }
            }
            finally
            {
                _updatingUi = false;
            }
        }

        // ---- Grid ----

        private void BuildColumns()
        {
            _grid.Columns.Clear();
            _builtCultures = _model?.Cultures.ToList() ?? new List<string>();
            _grid.Columns.Add(TextColumn(ResourceText.ColName, "Name", null, null, new DataGridLength(180)));
            _grid.Columns.Add(TextColumn(ResourceText.ColValue, "Value", null, null, new DataGridLength(1, DataGridLengthUnitType.Star, 240, 120)));
            _grid.Columns.Add(TextColumn(ResourceText.ColComment, "Comment", null, null, new DataGridLength(180)));
            foreach (var culture in _builtCultures)
            {
                _grid.Columns.Add(TextColumn(culture, "[" + culture + "]", "Value", culture, new DataGridLength(200)));
            }

            ApplyCultureVisibility();
        }

        private void ApplyCultureVisibility()
        {
            var show = _showCultures.IsChecked == true;
            for (var i = 3; i < _grid.Columns.Count; i++)
            {
                _grid.Columns[i].Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        /// <summary>
        /// A text column: the cell shows the value (or, for a culture column without a translation, the neutral value
        /// greyed out with a marker bar) and edits it in a text box.
        /// </summary>
        private static DataGridTemplateColumn TextColumn(string header, string path, string? placeholderPath, string? culture, DataGridLength width)
        {
            var show = new FrameworkElementFactory(typeof(Grid));
            if (culture is not null)
            {
                var marker = new FrameworkElementFactory(typeof(Border));
                marker.SetValue(FrameworkElement.WidthProperty, 3.0);
                marker.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
                marker.SetResourceReference(Border.BackgroundProperty, EnvironmentColors.ControlLinkTextBrushKey);
                marker.SetBinding(UIElement.VisibilityProperty, new Binding(path) { Converter = EmptyToVisibleConverter.Instance });
                show.AppendChild(marker);
            }

            if (placeholderPath is not null)
            {
                var placeholder = new FrameworkElementFactory(typeof(TextBlock));
                placeholder.SetBinding(TextBlock.TextProperty, new Binding(placeholderPath) { Mode = BindingMode.OneWay });
                placeholder.SetBinding(UIElement.VisibilityProperty, new Binding(path) { Converter = EmptyToVisibleConverter.Instance });
                placeholder.SetValue(TextBlock.FontStyleProperty, FontStyles.Italic);
                placeholder.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
                placeholder.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 2, 4, 2));
                placeholder.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.SystemGrayTextBrushKey);
                show.AppendChild(placeholder);
            }

            var value = new FrameworkElementFactory(typeof(TextBlock));
            value.SetBinding(TextBlock.TextProperty, new Binding(path) { Mode = BindingMode.OneWay });
            value.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            value.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 2, 4, 2));
            show.AppendChild(value);

            var edit = new FrameworkElementFactory(typeof(TextBox));
            edit.SetBinding(TextBox.TextProperty, new Binding(path) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus });
            edit.SetValue(TextBox.AcceptsReturnProperty, true);
            edit.SetValue(Control.BorderThicknessProperty, new Thickness(0));
            edit.SetValue(FrameworkElement.MarginProperty, new Thickness(0));
            edit.AddHandler(FrameworkElement.LoadedEvent, new RoutedEventHandler((s, _) =>
            {
                if (s is TextBox box)
                {
                    box.Focus();
                    box.SelectAll();
                }
            }));

            return new DataGridTemplateColumn
            {
                Header = header,
                Width = width,
                CellTemplate = new DataTemplate { VisualTree = show },
                CellEditingTemplate = new DataTemplate { VisualTree = edit },
                SortMemberPath = path,
            };
        }

        private void OnGridPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.OriginalSource is TextBox && e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            {
                // Enter commits the cell; Shift+Enter inserts a line break in the value.
                e.Handled = true;
                _grid.CommitEdit(DataGridEditingUnit.Cell, true);
                _grid.CommitEdit(DataGridEditingUnit.Row, true);
                return;
            }

            if (e.OriginalSource is TextBox)
            {
                return;
            }

            if (e.Key == Key.Delete && !_readOnly)
            {
                e.Handled = true;
                RemoveSelected();
            }
            else if (e.Key == Key.F2 && _grid.CurrentCell.Item is not null)
            {
                e.Handled = true;
                _grid.BeginEdit();
            }
        }

        private void BeginEditName(string name)
        {
            var row = _gridModel?.Rows.FirstOrDefault(r => r.Name == name);
            if (row is null || _grid.Columns.Count == 0)
            {
                return;
            }

            _grid.UnselectAll();
            _grid.SelectedItem = row;
            _grid.ScrollIntoView(row);
            _grid.Focus();
            _grid.CurrentCell = new DataGridCellInfo(row, _grid.Columns[0]);
            _grid.BeginEdit();
        }

        private void OnGridContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            e.Handled = true;
            if (_model is null || _readOnly)
            {
                return;
            }

            var menu = NewMenu();
            menu.Items.Add(NewItem(ResourceText.AddNewString, AddNewString));
            menu.Items.Add(NewItem(ResourceText.Remove, RemoveSelected, HasSelection));
            var names = SelectedNames();
            if (names.Count == 1)
            {
                menu.Items.Add(new Separator());
                menu.Items.Add(NewItem(ResourceText.CopyReference, () => CopyReference(names[0])));
            }

            menu.PlacementTarget = _grid;
            menu.IsOpen = true;
        }

        // ---- Tiles ----

        private void RebuildTiles()
        {
            if (_model is null)
            {
                return;
            }

            var keep = new HashSet<string>(SelectedNames());
            _tiles.Items.Clear();
            var entries = _model.InCategory(_category).ToList();
            foreach (var entry in entries)
            {
                var item = MakeTile(entry);
                _tiles.Items.Add(item);
                if (keep.Contains(entry.Name))
                {
                    item.IsSelected = true;
                }
            }

            _emptyHint.Text = ResourceText.NoResources;
            _emptyHint.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (_thumbnails.Count > 300)
            {
                _thumbnails.Clear();
            }
        }

        private ListViewItem MakeTile(ResourceEntry entry)
        {
            var model = _model!;
            FrameworkElement picture;
            var source = Thumbnail(model, entry);
            if (source is not null)
            {
                picture = new Image { Source = source, Width = 64, Height = 64, Stretch = Stretch.Uniform };
                RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
            }
            else
            {
                picture = new CrispImage { Moniker = MonikerFor(entry.Kind), Width = 48, Height = 48, Margin = new Thickness(8) };
            }

            picture.HorizontalAlignment = HorizontalAlignment.Center;
            var label = new TextBlock
            {
                Text = entry.Name,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 36,
                Margin = new Thickness(0, 4, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            var panel = new StackPanel { Width = TileWidth - 20 };
            panel.Children.Add(picture);
            panel.Children.Add(label);

            var item = new ListViewItem
            {
                Content = panel,
                Tag = entry.Name,
                Width = TileWidth,
                Padding = new Thickness(4),
                ToolTip = ResourceDetails.Of(model, entry).ToText(entry.Name),
            };
            System.Windows.Automation.AutomationProperties.SetName(item, entry.Name);
            item.MouseDoubleClick += (_, _) => OpenEntry(entry.Name);
            item.PreviewMouseRightButtonDown += (_, _) =>
            {
                if (!item.IsSelected)
                {
                    _tiles.SelectedItems.Clear();
                    item.IsSelected = true;
                }
            };
            item.ContextMenuOpening += (_, e) =>
            {
                e.Handled = true;
                ShowTileMenu(item);
            };
            return item;
        }

        private static ImageMoniker MonikerFor(ResourceKind kind) => kind switch
        {
            ResourceKind.Image => KnownMonikers.Image,
            ResourceKind.Icon => KnownMonikers.Image,
            ResourceKind.Audio => KnownMonikers.Sound,
            _ => KnownMonikers.Document,
        };

        private ImageSource? Thumbnail(ResourceSetModel model, ResourceEntry entry)
        {
            if (entry.Kind is not (ResourceKind.Image or ResourceKind.Icon))
            {
                return null;
            }

            var key = entry.Name + "|" + entry.Persistence + "|" + entry.Path + "|" + entry.Bytes.Length;
            if (entry.Persistence == ResourcePersistence.Linked)
            {
                try
                {
                    key += "|" + File.GetLastWriteTimeUtc(model.FullPath(entry.Path)).Ticks.ToString(CultureInfo.InvariantCulture);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                {
                    // An unreadable path simply gets no cached thumbnail.
                }
            }

            if (_thumbnails.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var decoded = Decode(model.BytesOf(entry.Name));
            _thumbnails[key] = decoded;
            return decoded;
        }

        /// <summary>Decodes an image (an icon's largest frame); null for SVG and anything WPF cannot decode.</summary>
        internal static ImageSource? Decode(byte[]? bytes)
        {
            if (bytes is null || bytes.Length == 0)
            {
                return null;
            }

            try
            {
                using var stream = new MemoryStream(bytes);
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                var frame = decoder.Frames.OrderByDescending(f => f.PixelWidth * f.PixelHeight).FirstOrDefault();
                frame?.Freeze();
                return frame;
            }
            catch (Exception ex) when (ex is NotSupportedException or FileFormatException or InvalidOperationException or IOException or ArgumentException)
            {
                return null;
            }
        }

        private void OnTilesPreviewKeyDown(object sender, KeyEventArgs e)
        {
            var names = SelectedNames();
            if (e.Key == Key.Delete && !_readOnly)
            {
                e.Handled = true;
                RemoveSelected();
            }
            else if (e.Key == Key.F2 && names.Count == 1 && !_readOnly)
            {
                e.Handled = true;
                RenameEntry(names[0]);
            }
            else if (e.Key == Key.Enter && names.Count == 1)
            {
                e.Handled = true;
                OpenEntry(names[0]);
            }
        }

        private void ShowTileMenu(ListViewItem anchor)
        {
            if (_model is null)
            {
                return;
            }

            var names = SelectedNames();
            var single = names.Count == 1;
            var editable = !_readOnly;
            var menu = NewMenu();
            menu.Items.Add(NewItem(ResourceText.Open, () => OpenEntry(names[0]), single));
            menu.Items.Add(NewItem(ResourceText.Rename + "\tF2", () => RenameEntry(names[0]), single && editable));
            menu.Items.Add(NewItem(ResourceText.EditComment, () => EditComment(names[0]), single && editable));
            menu.Items.Add(NewItem(ResourceText.CopyReference, () => CopyReference(names[0]), single));
            menu.Items.Add(new Separator());
            var persistence = NewItem(ResourceText.PersistenceMenu, null, editable && names.Count > 0);
            persistence.Items.Add(NewItem(ResourceText.Linked, () => ApplyPersistence(ResourcePersistence.Linked)));
            persistence.Items.Add(NewItem(ResourceText.Embedded, () => ApplyPersistence(ResourcePersistence.Embedded)));
            menu.Items.Add(persistence);
            menu.Items.Add(NewItem(ResourceText.Remove + "\tDel", RemoveSelected, editable && names.Count > 0));
            menu.PlacementTarget = anchor;
            menu.IsOpen = true;
        }

        private void OpenEntry(string name)
        {
            if (_model?.Get(name) is { Persistence: ResourcePersistence.Linked } entry)
            {
                _host.OpenFile(_model.FullPath(entry.Path));
            }
        }

        private void RenameEntry(string name)
        {
            if (_model is null)
            {
                return;
            }

            if (TextPromptDialog.TryAsk(ResourceText.RenameTitle, ResourceText.RenamePrompt, name, value => KbresFile.IsValidName(value) ? null : value + " ?", out var newName) && newName != name)
            {
                try
                {
                    _model.Rename(name, newName);
                }
                catch (ArgumentException ex)
                {
                    _host.ShowError(ex.Message);
                }
            }
        }

        private void EditComment(string name)
        {
            if (_model?.Get(name) is { } entry &&
                TextPromptDialog.TryAsk(ResourceText.CommentTitle, ResourceText.CommentPrompt, entry.Comment ?? string.Empty, null, out var comment))
            {
                _model.SetComment(name, comment.Length == 0 ? null : comment);
            }
        }

        private void CopyReference(string name)
        {
            try
            {
                Clipboard.SetText(ResourceNames.Reference(name));
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                // The clipboard is held by another process; the user can try again.
            }
        }

        // ---- Commands ----

        private List<string> SelectedNames()
        {
            if (IsGridCategory(_category))
            {
                return _grid.SelectedItems.OfType<ResourceRow>().Select(r => r.Name).ToList();
            }

            return _tiles.SelectedItems.OfType<ListViewItem>().Select(i => (string)i.Tag).ToList();
        }

        private void ApplyPersistence(ResourcePersistence persistence)
        {
            if (_model is null || _readOnly)
            {
                return;
            }

            foreach (var name in SelectedNames())
            {
                if (_model.Get(name) is { Persistence: not ResourcePersistence.Text } entry && entry.Persistence != persistence)
                {
                    try
                    {
                        _model.SetPersistence(name, persistence);
                    }
                    catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
                    {
                        _host.ShowError(ex.Message);
                        break;
                    }
                }
            }
        }

        private void AddNewString()
        {
            if (_model is null || _readOnly)
            {
                return;
            }

            if (_category != ResourceCategory.Strings)
            {
                SetCategory(ResourceCategory.Strings);
            }

            try
            {
                _editNameAfterRefresh = _model.AddString().Name;
            }
            catch (ArgumentException ex)
            {
                _host.ShowError(ex.Message);
            }
        }

        private void AddCulture()
        {
            if (_model is null || _readOnly)
            {
                return;
            }

            if (TextPromptDialog.TryAsk(
                ResourceText.CultureTitle,
                ResourceText.CulturePrompt,
                string.Empty,
                value => ResourceNames.IsCulture(value.Trim()) ? null : ResourceText.CultureInvalid,
                out var culture))
            {
                try
                {
                    _model.AddCulture(culture.Trim());
                }
                catch (ArgumentException ex)
                {
                    _host.ShowError(ex.Message);
                }
            }
        }

        private void AddExistingFiles()
        {
            if (_model is null)
            {
                return;
            }

            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Multiselect = true,
                Filter = ResourceText.AddFilesFilter,
                InitialDirectory = Directory.Exists(_model.Directory) ? _model.Directory : string.Empty,
                Title = ResourceText.AddExistingFile.TrimEnd('.'),
            };
            if (dialog.ShowDialog() == true)
            {
                AddFiles(dialog.FileNames);
            }
        }

        private void AddFiles(IEnumerable<string> paths)
        {
            if (_model is null || _readOnly)
            {
                return;
            }

            ResourceCategory? last = null;
            foreach (var path in paths)
            {
                if (Directory.Exists(path))
                {
                    continue;
                }

                try
                {
                    var entry = _model.AddFile(path, ResourcePersistence.Linked);
                    last = ResourceSetModel.CategoryOf(entry.Kind);
                }
                catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
                {
                    _host.ShowError(ex.Message);
                }
            }

            if (last is { } category && category != _category)
            {
                SetCategory(category);
            }
        }

        private void CreateFile(string baseName, string extension, byte[] content)
        {
            if (_model is null || _readOnly)
            {
                return;
            }

            var folder = Path.Combine(_model.Directory, "Resources");
            var path = BlankResources.FreeFile(folder, baseName, extension);
            try
            {
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(path, content);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                _host.ShowError(ResourceText.CannotCreateFile(path, ex.Message));
                return;
            }

            try
            {
                var entry = _model.AddFile(path, ResourcePersistence.Linked);
                SetCategory(ResourceSetModel.CategoryOf(entry.Kind));
            }
            catch (Exception ex) when (ex is ArgumentException or IOException)
            {
                _host.ShowError(ex.Message);
                return;
            }

            _host.OpenFile(path);
        }

        private void ShowAddMenu()
        {
            var menu = NewMenu();
            menu.Items.Add(NewItem(ResourceText.AddExistingFile, AddExistingFiles));
            menu.Items.Add(NewItem(ResourceText.AddNewString, AddNewString));
            menu.Items.Add(new Separator());
            var image = NewItem(ResourceText.NewImage, null);
            foreach (var format in BlankResources.ImageFormats)
            {
                var f = format;
                image.Items.Add(NewItem(f.ToUpperInvariant(), () => CreateFile("image", f, BlankResources.Image(f))));
            }

            menu.Items.Add(image);
            menu.Items.Add(NewItem(ResourceText.NewIcon, () => CreateFile("icon", "ico", BlankResources.Icon())));
            menu.Items.Add(NewItem(ResourceText.NewTextFile, () => CreateFile("text", "txt", Array.Empty<byte>())));
            menu.PlacementTarget = _addButton;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            {
                e.Handled = true;
                AddFiles(files);
            }
        }

        // ---- Menus ----

        private static ContextMenu NewMenu()
        {
            var menu = new ContextMenu { StaysOpen = false };
            if (Application.Current?.TryFindResource(VsResourceKeys.ContextMenuStyleKey) is Style style)
            {
                menu.Style = style;
            }
            else
            {
                menu.SetResourceReference(Control.BackgroundProperty, EnvironmentColors.CommandBarMenuBackgroundGradientBeginBrushKey);
                menu.SetResourceReference(Control.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            }

            return menu;
        }

        private static MenuItem NewItem(string header, Action? click, bool enabled = true)
        {
            var item = new MenuItem { Header = header, IsEnabled = enabled };
            if (click is not null)
            {
                item.Click += (_, _) => click();
            }

            return item;
        }

        private bool IsVisualDescendant(DependencyObject element)
        {
            for (DependencyObject? current = element; current is not null; current = current is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
            {
                if (ReferenceEquals(current, this))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
