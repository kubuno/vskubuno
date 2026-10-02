using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Kubuno.Desktop.Logic.Data;
using Kubuno.Shared.Logging;
using Kubuno.Shared.UI;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;

namespace Kubuno.Desktop.DataExplorer
{
    /// <summary>
    /// The Data Explorer tree: connections (from <c>explorer.list</c>) → schemas → Tables / Vues / Fonctions → tables and
    /// views → Colonnes / Clés / Index. A connection loads its schema (<c>schema.load</c>) the first time it is expanded,
    /// off the UI thread, with a "Chargement..." node meanwhile; the nodes below it are built from that snapshot
    /// (<see cref="DataExplorerTreeBuilder"/>) and materialized level by level as they are expanded. Errors become an
    /// error node, never an exception.
    /// </summary>
    internal sealed class DataExplorerControl : UserControl
    {
        private readonly TreeView _tree;
        private readonly Dictionary<string, ExplorerConnectionInfo> _connections = new Dictionary<string, ExplorerConnectionInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DatabaseSchemaInfo> _schemas = new Dictionary<string, DatabaseSchemaInfo>(StringComparer.OrdinalIgnoreCase);
        private int _listVersion;

        public DataExplorerControl()
        {
            ThemedDialogStyleLoader.SetUseDefaultThemedDialogStyles(this, true);
            ThemedControls.AddImplicitStyles(Resources);
            SetResourceReference(BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
            SetResourceReference(ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);

            _tree = new TreeView { BorderThickness = new Thickness(0), Padding = new Thickness(2, 4, 2, 4) };
            _tree.SetResourceReference(Control.BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
            System.Windows.Automation.AutomationProperties.SetName(_tree, DataText.DataExplorer);
            System.Windows.Automation.AutomationProperties.SetAutomationId(_tree, "KubunoDataExplorerTree");
            VirtualizingPanel.SetIsVirtualizing(_tree, true);
            _tree.SelectedItemChanged += (_, _) => SelectionChanged?.Invoke(this, EventArgs.Empty);
            _tree.PreviewMouseRightButtonDown += OnPreviewRightButtonDown;
            _tree.MouseRightButtonUp += (_, e) =>
            {
                if (_tree.SelectedItem is TreeViewItem item && item.IsMouseOver)
                {
                    e.Handled = true;
                    ContextMenuRequested?.Invoke(this, new DataContextMenuEventArgs(SelectedNode, PointToScreen(e.GetPosition(this))));
                }
            };
            _tree.PreviewKeyDown += OnTreeKeyDown;
            _tree.MouseDoubleClick += (_, e) =>
            {
                if (SelectedNode is { IsTableOrView: true } node && (e.OriginalSource as DependencyObject) is { } source && ItemFrom(source) is { IsSelected: true })
                {
                    e.Handled = true;
                    ActivateRequested?.Invoke(this, node);
                }
            };
            Content = _tree;
        }

        /// <summary>The tree selection changed (the toolbar re-queries its commands).</summary>
        public event EventHandler? SelectionChanged;

        /// <summary>Right-click or the context menu key on a node.</summary>
        public event EventHandler<DataContextMenuEventArgs>? ContextMenuRequested;

        /// <summary>Double-click or Enter on a table or view ("Show Table Data").</summary>
        public event EventHandler<DataExplorerNode>? ActivateRequested;

        /// <summary>Delete on a connection.</summary>
        public event EventHandler<DataExplorerNode>? DeleteRequested;

        /// <summary>The selected node (a connection is a <see cref="DataNodeKind.Connection"/> node), or null.</summary>
        public DataExplorerNode? SelectedNode => (_tree.SelectedItem as TreeViewItem)?.Tag as DataExplorerNode;

        /// <summary>The connections currently listed.</summary>
        public IReadOnlyCollection<string> ConnectionNames => _connections.Keys.ToList();

        public ExplorerConnectionInfo? ConnectionInfo(string name) => _connections.TryGetValue(name, out var info) ? info : null;

        /// <summary>The last schema snapshot of a connection (null until it was expanded).</summary>
        public DatabaseSchemaInfo? SchemaOf(string connection) => _schemas.TryGetValue(connection, out var schema) ? schema : null;

        /// <summary>Reloads the connection list; <paramref name="select"/> selects (and expands) that connection afterwards.</summary>
        public async Task RefreshAllAsync(string? select = null)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            int version = ++_listVersion;
            _tree.Items.Clear();
            _tree.Items.Add(MessageItem(DataNodeKind.Loading, DataText.Loading));
            IReadOnlyList<ExplorerConnectionInfo> list;
            try
            {
                list = await DataToolHost.Service.ListConnectionsAsync(CancellationToken.None);
            }
            catch (Exception exception)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (version == _listVersion)
                {
                    _tree.Items.Clear();
                    _tree.Items.Add(MessageItem(DataNodeKind.Error, exception is DataToolException { Kind: DataToolErrorKinds.Unavailable } ? DataText.ToolUnavailable(exception.Message) : DataText.ErrorNode(exception.Message)));
                }

                return;
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (version != _listVersion)
            {
                return;
            }

            _tree.Items.Clear();
            _connections.Clear();
            _schemas.Clear();
            foreach (var connection in list.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
            {
                _connections[connection.Name] = connection;
                _tree.Items.Add(ConnectionItem(connection));
            }

            if (list.Count == 0)
            {
                _tree.Items.Add(MessageItem(DataNodeKind.Message, DataText.NoConnections));
            }

            SelectionChanged?.Invoke(this, EventArgs.Empty);
            if (select != null && FindConnectionItem(select) is { } item)
            {
                item.IsSelected = true;
                item.IsExpanded = true;
                item.Focus();
            }
        }

        /// <summary>Reloads the schema of <paramref name="connection"/> (collapsing its subtree).</summary>
        public async Task RefreshConnectionAsync(string connection)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (FindConnectionItem(connection) is not { } item)
            {
                return;
            }

            _schemas.Remove(connection);
            item.Items.Clear();
            item.Items.Add(MessageItem(DataNodeKind.Loading, DataText.Loading));
            if (item.IsExpanded)
            {
                await LoadConnectionAsync(item);
            }
            else
            {
                item.IsExpanded = true;
            }
        }

        /// <summary>Expands the path of names below a connection (automation, tests): e.g. "Shop", "Tables", "customers", "Colonnes".</summary>
        public TreeViewItem? Expand(params string[] path)
        {
            ItemsControl parent = _tree;
            TreeViewItem? current = null;
            foreach (var name in path)
            {
                current = parent.Items.OfType<TreeViewItem>().FirstOrDefault(i => (i.Tag as DataExplorerNode)?.ObjectName == name || (i.Tag as DataExplorerNode)?.Text == name);
                if (current is null)
                {
                    return null;
                }

                current.IsExpanded = true;
                parent = current;
            }

            return current;
        }

        private TreeViewItem? FindConnectionItem(string name) =>
            _tree.Items.OfType<TreeViewItem>().FirstOrDefault(i => i.Tag is DataExplorerNode { Kind: DataNodeKind.Connection } n && string.Equals(n.Connection, name, StringComparison.OrdinalIgnoreCase));

        private TreeViewItem ConnectionItem(ExplorerConnectionInfo connection)
        {
            var node = new DataExplorerNode(DataNodeKind.Connection, connection.Name, connection.Name) { ToolTip = DataText.ConnectionToolTip(connection.Provider, connection.Display, connection.Store) };
            var item = new TreeViewItem
            {
                Header = DataUi.ImageText(DataUi.MonikerOf(connection.ProviderKind), connection.Name, out _, out _),
                Tag = node,
                ToolTip = node.ToolTip,
            };
            System.Windows.Automation.AutomationProperties.SetName(item, connection.Name);
            item.Items.Add(MessageItem(DataNodeKind.Loading, DataText.Loading));
            item.Expanded += (_, e) =>
            {
                if (ReferenceEquals(e.OriginalSource, item) && !_schemas.ContainsKey(connection.Name))
                {
                    DataUi.RunUi(() => LoadConnectionAsync(item), "LoadSchema");
                }
            };
            return item;
        }

        private async Task LoadConnectionAsync(TreeViewItem item)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (item.Tag is not DataExplorerNode { Kind: DataNodeKind.Connection } node)
            {
                return;
            }

            string name = node.Connection;
            DatabaseSchemaInfo schema;
            try
            {
                schema = await DataToolHost.Service.LoadSchemaAsync(DataConnectionTarget.Explorer(name), includeSystem: false, CancellationToken.None);
            }
            catch (Exception exception)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (!(exception is DataToolException))
                {
                    KubunoLog.WriteException($"Kubuno: loading the schema of '{name}' failed", exception);
                }

                item.Items.Clear();
                item.Items.Add(MessageItem(DataNodeKind.Error, DataText.ErrorNode(exception.Message)));
                return;
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            _schemas[name] = schema;
            item.Items.Clear();
            foreach (var child in DataExplorerTreeBuilder.BuildConnectionChildren(name, schema))
            {
                item.Items.Add(NodeItem(child));
            }

            if (schema.ServerVersion is { Length: > 0 } version)
            {
                item.ToolTip = node.ToolTip + "\n" + DataText.T("Server version: ", "Version du serveur : ") + version;
            }

            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>A tree item for a snapshot node; its children are materialized on first expansion.</summary>
        private static TreeViewItem NodeItem(DataExplorerNode node)
        {
            var item = new TreeViewItem { Tag = node, ToolTip = node.ToolTip };
            item.Header = DataUi.ImageText(DataUi.MonikerOf(node.Kind), node.Text, out var image, out var text);
            System.Windows.Automation.AutomationProperties.SetName(item, node.Text);
            if (node.Kind == DataNodeKind.Message)
            {
                text.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.SystemGrayTextBrushKey);
                image.Visibility = Visibility.Collapsed;
            }

            if (node.Children.Count > 0)
            {
                item.Items.Add(new TreeViewItem { Header = DataText.Loading });
                bool materialized = false;
                item.Expanded += (_, e) =>
                {
                    if (!ReferenceEquals(e.OriginalSource, item))
                    {
                        return;
                    }

                    image.Moniker = DataUi.MonikerOf(node.Kind, expanded: true);
                    if (!materialized)
                    {
                        materialized = true;
                        item.Items.Clear();
                        foreach (var child in node.Children)
                        {
                            item.Items.Add(NodeItem(child));
                        }
                    }
                };
                item.Collapsed += (_, e) =>
                {
                    if (ReferenceEquals(e.OriginalSource, item))
                    {
                        image.Moniker = DataUi.MonikerOf(node.Kind, expanded: false);
                    }
                };
            }

            return item;
        }

        private static TreeViewItem MessageItem(DataNodeKind kind, string text)
        {
            var item = new TreeViewItem { Header = DataUi.ImageText(DataUi.MonikerOf(kind), text, out _, out var block), Tag = null, Focusable = kind == DataNodeKind.Error };
            if (kind == DataNodeKind.Loading || kind == DataNodeKind.Message)
            {
                block.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.SystemGrayTextBrushKey);
            }

            block.TextWrapping = kind == DataNodeKind.Loading ? TextWrapping.NoWrap : TextWrapping.Wrap;
            block.MaxWidth = 600;
            System.Windows.Automation.AutomationProperties.SetName(item, text);
            return item;
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
            // Right-click selects the node under the mouse first, like Solution Explorer.
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
            if (menuKey && _tree.SelectedItem is TreeViewItem selected)
            {
                e.Handled = true;
                ContextMenuRequested?.Invoke(this, new DataContextMenuEventArgs(node, selected.PointToScreen(new Point(16, selected.ActualHeight > 0 ? Math.Min(selected.ActualHeight, 20) : 16))));
            }
            else if (e.Key == Key.Enter && node.IsTableOrView)
            {
                e.Handled = true;
                ActivateRequested?.Invoke(this, node);
            }
            else if (e.Key == Key.Delete && node.Kind == DataNodeKind.Connection)
            {
                e.Handled = true;
                DeleteRequested?.Invoke(this, node);
            }
        }
    }

    /// <summary>Where and on what a context menu was requested.</summary>
    internal sealed class DataContextMenuEventArgs : EventArgs
    {
        public DataContextMenuEventArgs(DataExplorerNode? node, Point screenPoint)
        {
            Node = node;
            ScreenPoint = screenPoint;
        }

        public DataExplorerNode? Node { get; }

        public Point ScreenPoint { get; }
    }
}
