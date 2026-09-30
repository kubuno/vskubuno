using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Kubuno.Desktop.Logic.DataSources;
using Kubuno.Desktop.DataExplorer;
using Kubuno.Core.UI;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;

namespace Kubuno.Desktop.DataSources
{
    /// <summary>What a node of the Data Sources tree is.</summary>
    internal enum DataSourceNodeKind
    {
        Source,
        Table,
        Column,
        Error,
    }

    /// <summary>A node of the Data Sources tree (its <see cref="TreeViewItem.Tag"/>).</summary>
    internal sealed class DataSourceNode
    {
        public DataSourceNode(DataSourceNodeKind kind, string kbdataPath, KbdataSourceInfo? source, KbdataTableInfo? table = null, KbdataColumnInfo? column = null)
        {
            Kind = kind;
            KbdataPath = kbdataPath;
            Source = source;
            Table = table;
            Column = column;
        }

        public DataSourceNodeKind Kind { get; }

        public string KbdataPath { get; }

        public KbdataSourceInfo? Source { get; }

        public KbdataTableInfo? Table { get; }

        public KbdataColumnInfo? Column { get; }

        /// <summary>Tables and columns can be dropped onto a view.</summary>
        public bool IsDraggable => Source != null && Table != null && (Kind == DataSourceNodeKind.Table || Kind == DataSourceNodeKind.Column);
    }

    /// <summary>
    /// The Data Sources tree (docs/DATA.md §9, DATA-6; Visual Studio's WinForms Data Sources window): the project's data sources →
    /// tables and views (icon = how the table is dropped: grid or details) → columns (icon = the control it becomes). A small arrow
    /// on the selected node opens its drop choices (the same context menu as a right-click). Tables and columns are dragged onto an
    /// open <c>.kbview</c> designer; Enter or a double-click adds them to the active one. Colours come from the VS theme only.
    /// </summary>
    internal sealed class DataSourcesControl : UserControl
    {
        private readonly TreeView _tree;
        private readonly TextBlock _header;
        private readonly TextBlock _message;
        private Point _dragStart;
        private TreeViewItem? _dragItem;

        public DataSourcesControl()
        {
            ThemedDialogStyleLoader.SetUseDefaultThemedDialogStyles(this, true);
            ThemedControls.AddImplicitStyles(Resources);
            SetResourceReference(BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
            SetResourceReference(ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);

            var root = new DockPanel();
            _header = new TextBlock { Margin = new Thickness(6, 4, 6, 2), TextTrimming = TextTrimming.CharacterEllipsis };
            _header.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.SystemGrayTextBrushKey);
            DockPanel.SetDock(_header, Dock.Top);
            root.Children.Add(_header);

            _message = new TextBlock { Margin = new Thickness(10, 8, 10, 8), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
            _message.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.SystemGrayTextBrushKey);
            DockPanel.SetDock(_message, Dock.Top);
            root.Children.Add(_message);

            _tree = new TreeView { BorderThickness = new Thickness(0), Padding = new Thickness(2, 2, 2, 4) };
            _tree.SetResourceReference(Control.BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
            System.Windows.Automation.AutomationProperties.SetName(_tree, DataSourcesText.DataSources);
            System.Windows.Automation.AutomationProperties.SetAutomationId(_tree, "KubunoDataSourcesTree");
            _tree.SelectedItemChanged += (_, _) =>
            {
                UpdateArrows();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            };
            _tree.PreviewMouseRightButtonDown += OnPreviewRightButtonDown;
            _tree.MouseRightButtonUp += (_, e) =>
            {
                if (_tree.SelectedItem is TreeViewItem item && item.IsMouseOver && SelectedNode is { } node)
                {
                    e.Handled = true;
                    ContextMenuRequested?.Invoke(this, new DataSourcesMenuEventArgs(node, PointToScreen(e.GetPosition(this))));
                }
            };
            _tree.PreviewKeyDown += OnTreeKeyDown;
            _tree.MouseDoubleClick += (_, e) =>
            {
                if (SelectedNode is { IsDraggable: true } node && (e.OriginalSource as DependencyObject) is { } source && ItemFrom(source) is { IsSelected: true } && !IsInsideButton(source))
                {
                    e.Handled = true;
                    ActivateRequested?.Invoke(this, node);
                }
            };
            _tree.PreviewMouseLeftButtonDown += (_, e) =>
            {
                _dragStart = e.GetPosition(_tree);
                _dragItem = (e.OriginalSource as DependencyObject) is { } source && !IsInsideButton(source) ? ItemFrom(source) : null;
            };
            _tree.PreviewMouseMove += OnPreviewMouseMove;
            root.Children.Add(_tree);
            Content = root;
        }

        /// <summary>The tree selection changed.</summary>
        public event EventHandler? SelectionChanged;

        /// <summary>Right-click, the context menu key or the node's arrow.</summary>
        public event EventHandler<DataSourcesMenuEventArgs>? ContextMenuRequested;

        /// <summary>Enter or double-click on a table or a column: add it to the active view.</summary>
        public event EventHandler<DataSourceNode>? ActivateRequested;

        /// <summary>A table or a column starts being dragged (the tool window runs the OLE drag).</summary>
        public event EventHandler<DataSourceNode>? DragRequested;

        public DataSourceNode? SelectedNode => (_tree.SelectedItem as TreeViewItem)?.Tag as DataSourceNode;

        /// <summary>The settings the icons are read from (set by the tool window before <see cref="Show"/>).</summary>
        public DataSourcesSettings? Settings { get; set; }

        /// <summary>Shows a message instead of the tree (no project, no source, loading).</summary>
        public void ShowMessage(string header, string message)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _header.Text = header;
            _header.Visibility = header.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            _tree.Items.Clear();
            _message.Text = message;
            _message.Visibility = Visibility.Visible;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Shows the sources of a project; <paramref name="errors"/> are files that could not be read. Keeps the expansion state of the nodes.</summary>
        public void Show(string header, IReadOnlyList<KbdataSourceInfo> sources, IReadOnlyList<KeyValuePair<string, string>> errors)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var expanded = new HashSet<string>(AllItems().Where(i => i.IsExpanded).Select(i => KeyOf((DataSourceNode)i.Tag)), StringComparer.Ordinal);
            string? selected = SelectedNode is { } s ? KeyOf(s) : null;
            bool firstShow = _tree.Items.Count == 0;

            _header.Text = header;
            _header.Visibility = Visibility.Visible;
            _tree.Items.Clear();
            foreach (var source in sources)
            {
                var sourceItem = Item(new DataSourceNode(DataSourceNodeKind.Source, source.FilePath, source), source.Name.Length > 0 ? source.Name : source.ModuleName, KnownMonikers.DataSourceView, source.FilePath);
                foreach (var table in source.Tables)
                {
                    var tableNode = new DataSourceNode(DataSourceNodeKind.Table, source.FilePath, source, table);
                    var tableItem = Item(tableNode, table.Name, TableMoniker(tableNode), TableToolTip(tableNode));
                    foreach (var column in table.Columns)
                    {
                        var columnNode = new DataSourceNode(DataSourceNodeKind.Column, source.FilePath, source, table, column);
                        tableItem.Items.Add(Item(columnNode, column.Name, ColumnMoniker(columnNode), ColumnToolTip(columnNode)));
                    }

                    sourceItem.Items.Add(tableItem);
                }

                sourceItem.IsExpanded = firstShow || expanded.Contains(KeyOf((DataSourceNode)sourceItem.Tag));
                _tree.Items.Add(sourceItem);
            }

            foreach (var error in errors)
            {
                var item = Item(new DataSourceNode(DataSourceNodeKind.Error, error.Key, null), DataSourcesText.SourceError(Path.GetFileName(error.Key), error.Value), KnownMonikers.StatusError, error.Key + "\n" + error.Value);
                _tree.Items.Add(item);
            }

            foreach (var item in AllItems())
            {
                if (expanded.Contains(KeyOf((DataSourceNode)item.Tag)))
                {
                    item.IsExpanded = true;
                }

                if (selected != null && KeyOf((DataSourceNode)item.Tag) == selected)
                {
                    item.IsSelected = true;
                }
            }

            bool empty = sources.Count == 0 && errors.Count == 0;
            _message.Text = empty ? DataSourcesText.NoDataSources : string.Empty;
            _message.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            UpdateArrows();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Re-reads the icons and tool tips (after a drop choice changed).</summary>
        public void RefreshIcons()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            foreach (var item in AllItems())
            {
                if (item.Tag is DataSourceNode { Kind: DataSourceNodeKind.Table } table && item.Header is Grid tableHeader && tableHeader.Children[0] is CrispImage tableImage)
                {
                    tableImage.Moniker = TableMoniker(table);
                    item.ToolTip = TableToolTip(table);
                }
                else if (item.Tag is DataSourceNode { Kind: DataSourceNodeKind.Column } column && item.Header is Grid columnHeader && columnHeader.Children[0] is CrispImage columnImage)
                {
                    columnImage.Moniker = ColumnMoniker(column);
                    item.ToolTip = ColumnToolTip(column);
                }
            }
        }

        /// <summary>Selects (and reveals) a node by path: source name, then table, then column (automation, tests).</summary>
        public TreeViewItem? Select(params string[] path)
        {
            ItemsControl parent = _tree;
            TreeViewItem? current = null;
            foreach (var name in path)
            {
                current = parent.Items.OfType<TreeViewItem>().FirstOrDefault(i => i.Tag is DataSourceNode n && NameOf(n) == name);
                if (current is null)
                {
                    return null;
                }

                if (parent is TreeViewItem parentItem)
                {
                    parentItem.IsExpanded = true;
                }

                parent = current;
            }

            if (current != null)
            {
                current.IsSelected = true;
                current.BringIntoView();
            }

            return current;
        }

        /// <summary>The screen point under the selected node's arrow (where its menu opens).</summary>
        public Point SelectedMenuPoint()
        {
            if (_tree.SelectedItem is TreeViewItem item && item.Header is Grid header && header.Children.Count > 2 && header.Children[2] is Button arrow && arrow.IsVisible)
            {
                return arrow.PointToScreen(new Point(0, arrow.ActualHeight));
            }

            return _tree.SelectedItem is TreeViewItem selected && selected.IsVisible ? selected.PointToScreen(new Point(16, 18)) : PointToScreen(new Point(16, 16));
        }

        private string TableToolTip(DataSourceNode node)
        {
            var mode = Settings?.ModeOf(node.KbdataPath, node.Table!.Name) ?? DataTableDropMode.Grid;
            return DataSourcesText.TableToolTip(node.Table!.Name, node.Table.IsView, node.Table.Columns.Count, mode == DataTableDropMode.Grid ? DataSourcesText.GridMode : DataSourcesText.DetailsMode);
        }

        private string ColumnToolTip(DataSourceNode node) =>
            DataSourcesText.ColumnToolTip(node.Column!.Name, node.Column.DbType, node.Column.RustType, node.Column.Nullable, DataSourcesText.ControlName(ControlOf(node)));

        internal DataControlKind ControlOf(DataSourceNode node) =>
            Settings?.ControlOf(node.KbdataPath, node.Table!.Name, node.Column!.Name) is { } chosen && DataColumnKinds.ControlsFor(node.Column.Kind).Contains(chosen)
                ? chosen
                : DataColumnKinds.DefaultControl(node.Column!);

        private ImageMoniker TableMoniker(DataSourceNode node) =>
            (Settings?.ModeOf(node.KbdataPath, node.Table!.Name) ?? DataTableDropMode.Grid) == DataTableDropMode.Details ? KnownMonikers.DetailView : KnownMonikers.DataGrid;

        private ImageMoniker ColumnMoniker(DataSourceNode node) => MonikerOf(ControlOf(node));

        internal static ImageMoniker MonikerOf(DataControlKind kind) => kind switch
        {
            DataControlKind.NumericField => KnownMonikers.Numeric,
            DataControlKind.CheckBox => KnownMonikers.CheckBoxChecked,
            DataControlKind.DatePicker => KnownMonikers.DateTimePicker,
            DataControlKind.Label => KnownMonikers.Label,
            DataControlKind.None => KnownMonikers.Cancel,
            _ => KnownMonikers.TextBox,
        };

        private static string NameOf(DataSourceNode node) => node.Kind switch
        {
            DataSourceNodeKind.Source => node.Source!.Name.Length > 0 ? node.Source.Name : node.Source.ModuleName,
            DataSourceNodeKind.Table => node.Table!.Name,
            DataSourceNodeKind.Column => node.Column!.Name,
            _ => node.KbdataPath,
        };

        private static string KeyOf(DataSourceNode node) => node.KbdataPath + "|" + node.Table?.Name + "|" + node.Column?.Name + "|" + node.Kind;

        private TreeViewItem Item(DataSourceNode node, string text, ImageMoniker moniker, string toolTip)
        {
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var image = new CrispImage { Moniker = moniker, Width = 16, Height = 16, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
            var block = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(block, 1);
            header.Children.Add(image);
            header.Children.Add(block);
            if (node.IsDraggable)
            {
                // The WinForms drop-down arrow of the selected table / column: its drop choices.
                var arrow = new Button
                {
                    Content = "▾",
                    Padding = new Thickness(3, -2, 3, -1),
                    Margin = new Thickness(6, 0, 0, 0),
                    MinWidth = 0,
                    MinHeight = 0,
                    FontSize = 10,
                    Focusable = false,
                    Visibility = Visibility.Hidden,
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = DataSourcesText.DropDownToolTip,
                    Cursor = Cursors.Arrow,
                };
                arrow.SetResourceReference(Control.BackgroundProperty, EnvironmentColors.CommandBarMouseOverBackgroundBeginBrushKey);
                arrow.SetResourceReference(Control.ForegroundProperty, EnvironmentColors.CommandBarTextActiveBrushKey);
                arrow.SetResourceReference(Control.BorderBrushProperty, EnvironmentColors.CommandBarBorderBrushKey);
                System.Windows.Automation.AutomationProperties.SetName(arrow, DataSourcesText.DropDownToolTip);
                arrow.Click += (_, e) =>
                {
                    e.Handled = true;
                    ContextMenuRequested?.Invoke(this, new DataSourcesMenuEventArgs(node, arrow.PointToScreen(new Point(0, arrow.ActualHeight))));
                };
                Grid.SetColumn(arrow, 2);
                header.Children.Add(arrow);
            }

            if (node.Kind == DataSourceNodeKind.Error)
            {
                block.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.SystemGrayTextBrushKey);
                block.TextWrapping = TextWrapping.Wrap;
                block.MaxWidth = 500;
            }

            var item = new TreeViewItem { Header = header, Tag = node, ToolTip = toolTip };
            System.Windows.Automation.AutomationProperties.SetName(item, text);
            return item;
        }

        private void UpdateArrows()
        {
            foreach (var item in AllItems())
            {
                if (item.Header is Grid header && header.Children.Count > 2 && header.Children[2] is Button arrow)
                {
                    arrow.Visibility = item.IsSelected ? Visibility.Visible : Visibility.Hidden;
                }
            }
        }

        private IEnumerable<TreeViewItem> AllItems()
        {
            var stack = new Stack<TreeViewItem>(_tree.Items.OfType<TreeViewItem>());
            while (stack.Count > 0)
            {
                var item = stack.Pop();
                yield return item;
                foreach (var child in item.Items.OfType<TreeViewItem>())
                {
                    stack.Push(child);
                }
            }
        }

        private void OnPreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _dragItem is null)
            {
                return;
            }

            var position = e.GetPosition(_tree);
            if (Math.Abs(position.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(position.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            var item = _dragItem;
            _dragItem = null;
            if (item.Tag is DataSourceNode { IsDraggable: true } node)
            {
                item.IsSelected = true;
                DragRequested?.Invoke(this, node);
            }
        }

        private static bool IsInsideButton(DependencyObject source)
        {
            for (DependencyObject? current = source; current != null; current = current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
            {
                if (current is Button)
                {
                    return true;
                }

                if (current is TreeViewItem)
                {
                    return false;
                }
            }

            return false;
        }

        private static TreeViewItem? ItemFrom(DependencyObject source)
        {
            DependencyObject? current = source;
            while (current != null && current is not TreeViewItem)
            {
                current = current is Visual || current is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
            }

            return current as TreeViewItem;
        }

        private void OnPreviewRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if ((e.OriginalSource as DependencyObject) is { } source && ItemFrom(source) is { } item)
            {
                item.IsSelected = true;
                item.Focus();
            }
        }

        private void OnTreeKeyDown(object sender, KeyEventArgs e)
        {
            var node = SelectedNode;
            if (node is null)
            {
                return;
            }

            bool menuKey = e.Key == Key.Apps || (e.Key == Key.F10 && (Keyboard.Modifiers & ModifierKeys.Shift) != 0) || (e.Key == Key.System && e.SystemKey == Key.F10 && (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
            bool arrowKey = e.Key == Key.System && e.SystemKey == Key.Down && (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
            if (menuKey || arrowKey)
            {
                e.Handled = true;
                ContextMenuRequested?.Invoke(this, new DataSourcesMenuEventArgs(node, SelectedMenuPoint()));
            }
            else if (e.Key == Key.Enter && node.IsDraggable)
            {
                e.Handled = true;
                ActivateRequested?.Invoke(this, node);
            }
        }
    }

    /// <summary>Where and on what a context menu was requested.</summary>
    internal sealed class DataSourcesMenuEventArgs : EventArgs
    {
        public DataSourcesMenuEventArgs(DataSourceNode node, Point screenPoint)
        {
            Node = node;
            ScreenPoint = screenPoint;
        }

        public DataSourceNode Node { get; }

        public Point ScreenPoint { get; }
    }
}
