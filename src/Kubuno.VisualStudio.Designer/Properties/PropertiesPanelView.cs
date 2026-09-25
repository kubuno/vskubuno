using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Kubuno.VisualStudio.Designer.Registry;
using Microsoft.VisualStudio.PlatformUI;

namespace Kubuno.VisualStudio.Designer.Properties
{
    /// <summary>
    /// The Properties/Events tool window's content: a Properties tab (one row per
    /// <see cref="PropertyRowViewModel"/>, an editor picked by <see cref="PropKindTag"/>) and an Events
    /// tab (one row per <see cref="EventRowViewModel"/>) - docs/DESIGNER.md §1's "Properties window with
    /// Properties and Events tabs". No .xaml, themed via <see cref="EnvironmentColors"/>, matching
    /// <see cref="Toolbox.ToolboxView"/> (see that class's own doc comment for why).
    /// </summary>
    public sealed class PropertiesPanelView : UserControl
    {
        private readonly PropertiesPanelViewModel _viewModel;
        private readonly StackPanel _propertiesPanel;
        private readonly StackPanel _eventsPanel;

        public PropertiesPanelView(PropertiesPanelViewModel viewModel)
        {
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

            var tabs = new TabControl();
            tabs.SetResourceReference(BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);

            _propertiesPanel = new StackPanel();
            var propertiesTab = new TabItem
            {
                Header = "Properties",
                Content = new ScrollViewer { Content = _propertiesPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            };

            _eventsPanel = new StackPanel();
            var eventsTab = new TabItem
            {
                Header = "Events",
                Content = new ScrollViewer { Content = _eventsPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            };

            tabs.Items.Add(propertiesTab);
            tabs.Items.Add(eventsTab);

            Content = tabs;

            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            Rebuild();
        }

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PropertiesPanelViewModel.Properties) || e.PropertyName == nameof(PropertiesPanelViewModel.Events))
            {
                Rebuild();
            }
        }

        private void Rebuild()
        {
            _propertiesPanel.Children.Clear();
            foreach (var row in _viewModel.Properties)
            {
                _propertiesPanel.Children.Add(BuildPropertyRow(row));
            }

            _eventsPanel.Children.Clear();
            foreach (var row in _viewModel.Events)
            {
                _eventsPanel.Children.Add(BuildEventRow(row));
            }
        }

        private UIElement BuildPropertyRow(PropertyRowViewModel row)
        {
            var grid = new Grid { Margin = new Thickness(4, 2, 4, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var opacity = row.IsDefault ? 0.65 : 1.0;

            var label = new TextBlock
            {
                Text = row.Name,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = row.Property.Doc,
                Opacity = opacity,
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            Grid.SetColumn(label, 0);
            grid.Children.Add(label);

            var editor = BuildEditor(row);
            editor.Opacity = opacity;
            Grid.SetColumn(editor, 1);
            grid.Children.Add(editor);

            var reset = new Button
            {
                Content = "↺",
                ToolTip = "Reset to default",
                Width = 20,
                IsEnabled = !row.IsDefault,
                Margin = new Thickness(2, 0, 0, 0),
            };
            reset.Click += (_, _) => row.ResetToDefault();
            Grid.SetColumn(reset, 2);
            grid.Children.Add(reset);

            return grid;
        }

        private FrameworkElement BuildEditor(PropertyRowViewModel row)
        {
            if (row.IsBinding)
            {
                return BuildBindingEditor(row);
            }

            return row.Kind.Tag switch
            {
                PropKindTag.Bool => BuildBoolEditor(row),
                PropKindTag.Enum => BuildEnumEditor(row),
                PropKindTag.F32 => BuildNumericEditor(row),
                _ => BuildStringEditor(row),
            };
        }

        private static FrameworkElement BuildBindingEditor(PropertyRowViewModel row)
        {
            var panel = new DockPanel();

            // A small binding glyph, mirroring how XAML's own property grid marks a bound value
            // (docs/DESIGNER.md §1).
            var glyph = new TextBlock
            {
                Text = "\U0001F517",
                Margin = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "This value is a binding expression.",
            };
            glyph.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            DockPanel.SetDock(glyph, Dock.Left);
            panel.Children.Add(glyph);

            var box = new TextBox { Text = row.EffectiveValue, ToolTip = "{Binding Path[, Mode=TwoWay]}" };
            ApplyEditorTheme(box);
            box.LostFocus += (_, _) => row.RawValue = box.Text;
            panel.Children.Add(box);

            return panel;
        }

        private static CheckBox BuildBoolEditor(PropertyRowViewModel row)
        {
            var check = new CheckBox
            {
                IsChecked = bool.TryParse(row.EffectiveValue, out var value) ? value : (bool?)null,
                VerticalAlignment = VerticalAlignment.Center,
            };
            check.Checked += (_, _) => row.RawValue = "true";
            check.Unchecked += (_, _) => row.RawValue = "false";
            return check;
        }

        private static ComboBox BuildEnumEditor(PropertyRowViewModel row)
        {
            var combo = new ComboBox { ItemsSource = row.Kind.EnumVariants, SelectedItem = row.EffectiveValue };
            ApplyEditorTheme(combo);
            combo.SelectionChanged += (_, _) => row.RawValue = combo.SelectedItem as string;
            return combo;
        }

        private static TextBox BuildNumericEditor(PropertyRowViewModel row)
        {
            var box = new TextBox { Text = row.EffectiveValue };
            ApplyEditorTheme(box);
            box.PreviewTextInput += (_, e) => e.Handled = !IsValidNumericFragment(box, e.Text);
            DataObject.AddPastingHandler(box, (_, e) =>
            {
                var pasted = e.DataObject.GetData(DataFormats.Text) as string;
                if (pasted is null || !IsValidNumericFragment(box, pasted))
                {
                    e.CancelCommand();
                }
            });
            box.LostFocus += (_, _) => row.RawValue = box.Text;
            return box;
        }

        private static TextBox BuildStringEditor(PropertyRowViewModel row)
        {
            var box = new TextBox { Text = row.EffectiveValue };
            ApplyEditorTheme(box);
            box.LostFocus += (_, _) => row.RawValue = box.Text;
            return box;
        }

        private static void ApplyEditorTheme(Control control)
        {
            control.SetResourceReference(BackgroundProperty, EnvironmentColors.ComboBoxBackgroundBrushKey);
            control.SetResourceReference(ForegroundProperty, EnvironmentColors.ComboBoxTextBrushKey);
            control.SetResourceReference(Control.BorderBrushProperty, EnvironmentColors.ComboBoxBorderBrushKey);
        }

        private static bool IsValidNumericFragment(TextBox box, string input)
        {
            var start = box.SelectionStart;
            var length = box.SelectionLength;
            var proposed = box.Text.Remove(start, length).Insert(start, input);
            if (proposed.Length == 0 || proposed == "-" || proposed == "." || proposed == "-.")
            {
                return true;
            }

            return float.TryParse(proposed, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
        }

        private UIElement BuildEventRow(EventRowViewModel row)
        {
            var grid = new Grid { Margin = new Thickness(4, 2, 4, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var label = new TextBlock
            {
                Text = row.Name,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = row.Event.Doc,
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            Grid.SetColumn(label, 0);
            grid.Children.Add(label);

            var combo = new ComboBox
            {
                ItemsSource = row.AvailableHandlerNames,
                SelectedItem = row.HandlerName,
                IsEditable = true,
                Text = row.HandlerName ?? string.Empty,
            };
            ApplyEditorTheme(combo);
            combo.LostFocus += (_, _) => row.HandlerName = string.IsNullOrEmpty(combo.Text) ? null : combo.Text;
            Grid.SetColumn(combo, 1);
            grid.Children.Add(combo);

            var createHandler = new Button
            {
                Content = "Create handler",
                Margin = new Thickness(2, 0, 0, 0),
                IsEnabled = !row.HasHandler,
            };
            createHandler.Click += (_, _) => row.RequestCreateHandler();
            Grid.SetColumn(createHandler, 2);
            grid.Children.Add(createHandler);

            // docs/DESIGNER.md §1: "double-click on an empty row to generate a new handler".
            grid.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount == 2 && !row.HasHandler)
                {
                    row.RequestCreateHandler();
                }
            };

            return grid;
        }
    }
}
