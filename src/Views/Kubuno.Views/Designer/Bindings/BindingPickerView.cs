using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;

namespace Kubuno.Views.Designer.Bindings
{
    /// <summary>
    /// The binding picker (docs/DESIGNER.md, "Data bindings"): the drop-down of a bindable property's value cell in the
    /// Properties window - and of a member's row in the collection editor. On top, the property's binding problems and its
    /// literal values (a boolean's, an enumeration's); then a searchable tree of what it can be bound to
    /// (<see cref="BindingPickerModel"/>: the row of the template, the data context, the data sources and their members, the
    /// resources), fitting members first, the others greyed; at the bottom « Avancé… » (the « Liaison de données » dialog),
    /// « Modifier la valeur… » (the property's own editor) and « Supprimer la liaison ». Themed like a Visual Studio dialog.
    /// </summary>
    internal sealed class BindingPickerView : UserControl
    {
        private readonly BindingSourceSchema _schema;
        private readonly BindingShape? _want;
        private readonly string? _current;
        private readonly TreeView _tree;
        private readonly TextBox _search;
        private readonly bool _embedded;

        /// <param name="literals">The property's own values (true/false, an enumeration's variants), offered above the tree; may be empty.</param>
        /// <param name="issues">The binding problems of the property, shown on top.</param>
        /// <param name="canEditValue">Whether the property has its own editor (« Modifier la valeur… »).</param>
        /// <param name="embedded">Part of the « Liaison de données » dialog: no links, no resources (a pick fills the dialog).</param>
        public BindingPickerView(BindingSourceSchema schema, BindingShape? want, string? current, IReadOnlyList<string> literals, IEnumerable<BindingIssue> issues, bool canEditValue, bool embedded = false)
        {
            _schema = schema;
            _embedded = embedded;
            _want = want;
            _current = current;
            SetResourceReference(BackgroundProperty, ThemedDialogColors.WindowPanelBrushKey);
            SetResourceReference(ForegroundProperty, ThemedDialogColors.WindowPanelTextBrushKey);
            Focusable = false;

            var root = new DockPanel { Margin = new Thickness(6), LastChildFill = true };

            // Problems.
            foreach (var issue in issues)
            {
                var text = Text(BindingStrings.WithIssue(issue.Message), wrap: true);
                text.Margin = new Thickness(0, 0, 0, 6);
                text.SetResourceReference(TextBlock.ForegroundProperty, issue.Severity == "error" ? EnvironmentColors.SystemHighlightBrushKey : ThemedDialogColors.WindowPanelTextBrushKey);
                text.FontWeight = FontWeights.SemiBold;
                DockPanel.SetDock(text, Dock.Top);
                root.Children.Add(text);
            }

            // Literal values.
            if (literals.Count > 0)
            {
                var title = Text(BindingStrings.Values);
                title.FontWeight = FontWeights.SemiBold;
                DockPanel.SetDock(title, Dock.Top);
                root.Children.Add(title);
                var values = new WrapPanel { Margin = new Thickness(0, 2, 0, 6) };
                foreach (var v in literals)
                {
                    var button = new Button { Content = v, Margin = new Thickness(0, 0, 4, 4), Padding = new Thickness(8, 1, 8, 1), MinWidth = 0, Tag = v };
                    button.SetResourceReference(StyleProperty, VsResourceKeys.ThemedDialogButtonStyleKey);
                    button.Click += (_, _) => LiteralPicked?.Invoke(v);
                    values.Children.Add(button);
                }

                DockPanel.SetDock(values, Dock.Top);
                root.Children.Add(values);
            }

            // Search.
            _search = new TextBox { Margin = new Thickness(0, 0, 0, 4) };
            _search.SetResourceReference(StyleProperty, VsResourceKeys.ThemedDialogTextBoxStyleKey);
            System.Windows.Automation.AutomationProperties.SetName(_search, BindingStrings.Search);
            _search.TextChanged += (_, _) => Fill();
            _search.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Down && _tree is { Items.Count: > 0 } tree && tree.ItemContainerGenerator.ContainerFromIndex(0) is TreeViewItem first)
                {
                    first.Focus();
                    e.Handled = true;
                }
            };
            DockPanel.SetDock(_search, Dock.Top);
            root.Children.Add(_search);

            // Links (not when the picker is part of the « Liaison de données » dialog).
            var links = new WrapPanel { Margin = new Thickness(0, 6, 0, 0), Visibility = embedded ? Visibility.Collapsed : Visibility.Visible };
            links.Children.Add(Link(BindingStrings.Advanced, () => AdvancedRequested?.Invoke()));
            if (canEditValue)
            {
                links.Children.Add(Link(BindingStrings.EditValue, () => EditValueRequested?.Invoke()));
            }

            if (BindingMarkup.IsMarkupExtension(current))
            {
                links.Children.Add(Link(BindingStrings.RemoveBinding, () => RemoveRequested?.Invoke()));
            }

            DockPanel.SetDock(links, Dock.Bottom);
            root.Children.Add(links);

            // The tree.
            _tree = new TreeView { MinHeight = 120 };
            _tree.SetResourceReference(StyleProperty, VsResourceKeys.ThemedDialogTreeViewStyleKey);
            System.Windows.Automation.AutomationProperties.SetName(_tree, BindingStrings.Source);
            _tree.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter && _tree.SelectedItem is TreeViewItem { Tag: BindingPickerNode node })
                {
                    Pick(node);
                    e.Handled = true;
                }
            };
            root.Children.Add(_tree);
            Content = root;
            Fill();
            Loaded += (_, _) => _search.Focus();
            PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    Cancelled?.Invoke();
                    e.Handled = true;
                }
            };
        }

        /// <summary>A member was picked: the new attribute value (the binding pointed at it, its options kept).</summary>
        public event Action<string>? Picked;

        /// <summary>A member was picked (raised before <see cref="Picked"/>).</summary>
        public event Action<BindingMember>? MemberPicked;

        /// <summary>A literal value was picked.</summary>
        public event Action<string>? LiteralPicked;

        public event Action? AdvancedRequested;

        public event Action? EditValueRequested;

        public event Action? RemoveRequested;

        public event Action? Cancelled;

        /// <summary>The tree's groups, as shown (for UI automation and tests of the live picker).</summary>
        public IReadOnlyList<BindingPickerGroup> Groups { get; private set; } = Array.Empty<BindingPickerGroup>();

        private void Fill()
        {
            _tree.Items.Clear();
            Groups = BindingPickerModel.Build(_schema, _want, _current, _search.Text, includeResources: !_embedded);
            foreach (var group in Groups)
            {
                var header = Text(BindingStrings.Group(group.Id, group.Label));
                header.FontWeight = FontWeights.SemiBold;
                var item = new TreeViewItem { Header = header, IsExpanded = true, Focusable = true };
                item.SetResourceReference(StyleProperty, VsResourceKeys.ThemedDialogTreeViewItemStyleKey);
                foreach (var node in group.Nodes)
                {
                    item.Items.Add(NodeItem(node, expand: !string.IsNullOrWhiteSpace(_search.Text) || node.Children.Any(c => c.IsCurrent)));
                }

                _tree.Items.Add(item);
            }

            if (Groups.Count == 0)
            {
                var empty = new TreeViewItem { Header = Text(BindingStrings.NoSources, wrap: true), Focusable = false, MaxWidth = 340 };
                empty.SetResourceReference(StyleProperty, VsResourceKeys.ThemedDialogTreeViewItemStyleKey);
                _tree.Items.Add(empty);
            }
            else if (_schema.Context.Open && string.IsNullOrWhiteSpace(_search.Text))
            {
                var open = new TreeViewItem { Header = Text(BindingStrings.OpenContext, wrap: true), Focusable = false, MaxWidth = 340, Opacity = 0.7 };
                open.SetResourceReference(StyleProperty, VsResourceKeys.ThemedDialogTreeViewItemStyleKey);
                _tree.Items.Add(open);
            }
        }

        private TreeViewItem NodeItem(BindingPickerNode node, bool expand)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var name = Plain(node.Label);
            if (node.IsCurrent)
            {
                name.FontWeight = FontWeights.Bold;
            }

            row.Children.Add(name);
            var type = Plain("  " + BindingStrings.TypeOf(node.Member));
            type.Opacity = 0.65;
            row.Children.Add(type);
            var item = new TreeViewItem { Header = row, Tag = node, IsExpanded = expand };
            item.SetResourceReference(StyleProperty, VsResourceKeys.ThemedDialogTreeViewItemStyleKey);
            System.Windows.Automation.AutomationProperties.SetName(item, node.Label);
            if (!node.Compatible)
            {
                row.Opacity = 0.45;
            }

            var tip = BindingStrings.Combine(node.Member.Path, node.Member.Doc, node.Compatible ? null : BindingStrings.Incompatible(node.Member.Shape, _want));
            if (tip.Length > 0)
            {
                item.ToolTip = tip;
            }

            foreach (var child in node.Children)
            {
                item.Items.Add(NodeItem(child, expand: false));
            }

            // A leaf is picked by a click (like a Windows Forms drop-down list); a node with members by a double-click.
            item.PreviewMouseLeftButtonUp += (_, e) =>
            {
                if (e.OriginalSource is DependencyObject source && Owner(source) == item && node.Children.Count == 0)
                {
                    Pick(node);
                    e.Handled = true;
                }
            };
            item.MouseDoubleClick += (_, e) =>
            {
                if (e.OriginalSource is DependencyObject source && Owner(source) == item)
                {
                    Pick(node);
                    e.Handled = true;
                }
            };
            return item;
        }

        /// <summary>The tree item that holds <paramref name="d"/>.</summary>
        private static TreeViewItem? Owner(DependencyObject d)
        {
            while (d is not null and not TreeViewItem)
            {
                d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            }

            return d as TreeViewItem;
        }

        /// <summary>Picks <paramref name="node"/> (what UI automation and the keyboard also do).</summary>
        public void Pick(BindingPickerNode node)
        {
            MemberPicked?.Invoke(node.Member);
            Picked?.Invoke(BindingPickerModel.Apply(_current, node.Member));
        }

        /// <summary>A text that takes its colour from the tree item (its selected and greyed states included).</summary>
        private static TextBlock Plain(string text) => new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };

        private static TextBlock Text(string text, bool wrap = false)
        {
            var block = new TextBlock { Text = text, TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center };
            block.SetResourceReference(TextBlock.ForegroundProperty, ThemedDialogColors.WindowPanelTextBrushKey);
            return block;
        }

        private static TextBlock Link(string text, Action click)
        {
            var link = new Hyperlink(new Run(text));
            link.SetResourceReference(TextElement.ForegroundProperty, EnvironmentColors.ControlLinkTextBrushKey);
            link.Click += (_, _) => click();
            var block = new TextBlock(link) { Margin = new Thickness(0, 0, 12, 0) };
            return block;
        }
    }
}
