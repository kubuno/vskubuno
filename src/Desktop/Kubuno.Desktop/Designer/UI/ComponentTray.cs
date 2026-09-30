using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;

namespace Kubuno.Desktop.Designer.UI
{
    /// <summary>One non-visual component shown in the <see cref="ComponentTray"/>.</summary>
    public sealed class ComponentTrayItem
    {
        public ComponentTrayItem(string elementId, string tag, string? name)
        {
            ElementId = elementId ?? string.Empty;
            Tag = tag ?? string.Empty;
            Name = name;
        }

        /// <summary>The element's stable id (docs/DESIGNER.md §8).</summary>
        public string ElementId { get; }

        /// <summary>The element name (<c>Timer</c>).</summary>
        public string Tag { get; }

        /// <summary>Its <c>x:Name</c>, when it has one.</summary>
        public string? Name { get; }

        /// <summary>What the tray shows: the <c>x:Name</c>, else the element name.</summary>
        public string Label => string.IsNullOrEmpty(Name) ? Tag : Name!;

        public override bool Equals(object? obj) => obj is ComponentTrayItem other && other.ElementId == ElementId && other.Tag == Tag && other.Name == Name;

        public override int GetHashCode() => (ElementId + "|" + Tag + "|" + Name).GetHashCode();
    }

    /// <summary>
    /// The component tray under the design surface (docs/EVENTS.md EVT-7b, WinForms' component tray): the view's
    /// non-visual components (a <c>&lt;Timer&gt;</c>, a project's <c>#[kubuno(extends = Component)]</c> class),
    /// which have no place on the surface. A click selects one (the Properties window then shows it, F4), a
    /// double-click creates or opens its default event's handler, Delete removes it. Hidden while the view has none.
    /// </summary>
    public sealed class ComponentTray : Border
    {
        private readonly WrapPanel _panel = new WrapPanel { Margin = new Thickness(4, 2, 4, 2) };
        private IReadOnlyList<ComponentTrayItem> _items = Array.Empty<ComponentTrayItem>();
        private HashSet<string> _selected = new HashSet<string>(StringComparer.Ordinal);

        public ComponentTray()
        {
            BorderThickness = new Thickness(0, 1, 0, 0);
            MinHeight = 34;
            Visibility = Visibility.Collapsed;
            Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 96, Content = _panel };
            SetResourceReference(BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
            SetResourceReference(BorderBrushProperty, EnvironmentColors.ToolWindowBorderBrushKey);
            Focusable = true;
            KeyDown += OnKeyDown;
        }

        /// <summary>The icon of an element (a Kubuno control icon XAML name, see <c>NativeToolboxInstaller.IconName</c>); null: none.</summary>
        public Func<string, FrameworkElement?>? IconFactory { get; set; }

        /// <summary>A component was clicked: select it.</summary>
        public event EventHandler<string>? ItemSelected;

        /// <summary>A component was double-clicked: its default event's handler.</summary>
        public event EventHandler<string>? ItemActivated;

        /// <summary>Delete was pressed on the selected component.</summary>
        public event EventHandler<string>? DeleteRequested;

        /// <summary>The components shown (tests).</summary>
        public IReadOnlyList<ComponentTrayItem> Items => _items;

        /// <summary>Shows <paramref name="items"/> (the tray hides when there is none).</summary>
        public void SetItems(IReadOnlyList<ComponentTrayItem> items)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            items ??= Array.Empty<ComponentTrayItem>();
            if (items.SequenceEqual(_items))
            {
                return;
            }

            _items = items;
            Rebuild();
        }

        /// <summary>Highlights the selected elements that are in the tray.</summary>
        public void SetSelection(IEnumerable<string> elementIds)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var selected = new HashSet<string>(elementIds ?? Array.Empty<string>(), StringComparer.Ordinal);
            if (selected.SetEquals(_selected))
            {
                return;
            }

            _selected = selected;
            Rebuild();
        }

        private void Rebuild()
        {
            _panel.Children.Clear();
            foreach (var item in _items)
            {
                _panel.Children.Add(BuildChip(item));
            }

            Visibility = _items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        private FrameworkElement BuildChip(ComponentTrayItem item)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            if (IconFactory?.Invoke(item.Tag) is { } icon)
            {
                icon.Width = 16;
                icon.Height = 16;
                icon.Margin = new Thickness(0, 0, 5, 0);
                icon.VerticalAlignment = VerticalAlignment.Center;
                content.Children.Add(icon);
            }

            var text = new TextBlock { Text = item.Label, VerticalAlignment = VerticalAlignment.Center };
            text.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            content.Children.Add(text);

            var selected = _selected.Contains(item.ElementId);
            var chip = new Border
            {
                Child = content,
                Padding = new Thickness(6, 3, 8, 3),
                Margin = new Thickness(2),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Arrow,
                ToolTip = item.Name is null ? item.Tag : $"{item.Name} ({item.Tag})",
            };
            chip.SetResourceReference(Border.BorderBrushProperty, selected ? EnvironmentColors.SystemHighlightBrushKey : EnvironmentColors.ToolWindowBackgroundBrushKey);
            chip.Background = Brushes.Transparent;
            if (selected)
            {
                chip.SetResourceReference(Border.BackgroundProperty, EnvironmentColors.CommandBarMouseOverBackgroundBeginBrushKey);
            }

            chip.MouseLeftButtonDown += (_, e) =>
            {
                Focus();
                if (e.ClickCount >= 2)
                {
                    ItemActivated?.Invoke(this, item.ElementId);
                }
                else
                {
                    ItemSelected?.Invoke(this, item.ElementId);
                }

                e.Handled = true;
            };
            return chip;
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete && _items.FirstOrDefault(i => _selected.Contains(i.ElementId)) is { } item)
            {
                DeleteRequested?.Invoke(this, item.ElementId);
                e.Handled = true;
            }
        }
    }
}
