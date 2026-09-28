using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;

namespace Kubuno.VisualStudio.Commands
{
    /// <summary>Which Cargo.toml table <c>cargo add</c> should target.</summary>
    internal enum CargoDependencyKind
    {
        Normal,
        Dev,
        Build,
    }

    /// <summary>
    /// "Dépendance Cargo (crate)..." dialog: crate name, optional version and features, and the
    /// dependency kind - a small VS-styled (<see cref="DialogWindow"/> + <see cref="VsBrushes"/>)
    /// WPF dialog, not a browser/OS prompt, matching <see cref="ProjectReferenceDialog"/>'s own
    /// plain-code construction (see its own remark on why this project has no XAML/BAML wiring yet).
    /// </summary>
    internal sealed class CargoDependencyDialog : DialogWindow
    {
        private readonly TextBox _nameBox;
        private readonly TextBox _versionBox;
        private readonly TextBox _featuresBox;
        private readonly RadioButton _normalRadio;
        private readonly RadioButton _devRadio;
        private readonly RadioButton _buildRadio;

        public CargoDependencyDialog()
        {
            Title = "Dépendance Cargo (crate)";
            Width = 420;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            SetResourceReference(BackgroundProperty, VsBrushes.WindowKey);
            SetResourceReference(ForegroundProperty, VsBrushes.WindowTextKey);

            var grid = new Grid { Margin = new Thickness(12) };
            for (var i = 0; i < 6; i++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _nameBox = AddRow(grid, 0, "Crate :");
            _versionBox = AddRow(grid, 1, "Version (facultatif) :");
            _featuresBox = AddRow(grid, 2, "Fonctionnalités (features, séparées par des virgules) :");

            var kindLabel = Label("Type de dépendance :");
            Grid.SetRow(kindLabel, 3);
            Grid.SetColumn(kindLabel, 0);
            grid.Children.Add(kindLabel);

            var kindPanel = new StackPanel { Orientation = Orientation.Horizontal };
            _normalRadio = new RadioButton { Content = "Normale", GroupName = "kind", IsChecked = true, Margin = new Thickness(0, 0, 10, 0) };
            _devRadio = new RadioButton { Content = "Dev", GroupName = "kind", Margin = new Thickness(0, 0, 10, 0) };
            _buildRadio = new RadioButton { Content = "Build", GroupName = "kind" };
            foreach (var radio in new[] { _normalRadio, _devRadio, _buildRadio })
            {
                radio.SetResourceReference(ForegroundProperty, VsBrushes.WindowTextKey);
                kindPanel.Children.Add(radio);
            }

            Grid.SetRow(kindPanel, 3);
            Grid.SetColumn(kindPanel, 1);
            grid.Children.Add(kindPanel);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 6, 0) };
            var cancel = new Button { Content = "Annuler", IsCancel = true, MinWidth = 80 };
            ok.Click += (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(_nameBox.Text))
                {
                    _nameBox.Focus();
                    return;
                }

                DialogResult = true;
            };
            cancel.Click += (_, _) => { DialogResult = false; };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            Grid.SetRow(buttons, 4);
            Grid.SetColumn(buttons, 0);
            Grid.SetColumnSpan(buttons, 2);
            grid.Children.Add(buttons);

            Content = grid;
            _nameBox.Loaded += (_, _) => _nameBox.Focus();
        }

        public string CrateName => _nameBox.Text.Trim();

        public string? Version => string.IsNullOrWhiteSpace(_versionBox.Text) ? null : _versionBox.Text.Trim();

        public string? Features => string.IsNullOrWhiteSpace(_featuresBox.Text) ? null : _featuresBox.Text.Trim();

        public CargoDependencyKind Kind => _devRadio.IsChecked == true ? CargoDependencyKind.Dev : _buildRadio.IsChecked == true ? CargoDependencyKind.Build : CargoDependencyKind.Normal;

        private TextBox AddRow(Grid grid, int row, string label)
        {
            var textLabel = Label(label);
            Grid.SetRow(textLabel, row);
            Grid.SetColumn(textLabel, 0);
            grid.Children.Add(textLabel);

            var box = new TextBox { Margin = new Thickness(6, 4, 0, 4) };
            box.SetResourceReference(BackgroundProperty, VsBrushes.ComboBoxBackgroundKey);
            box.SetResourceReference(ForegroundProperty, VsBrushes.WindowTextKey);
            box.SetResourceReference(BorderBrushProperty, VsBrushes.ComboBoxBorderKey);
            Grid.SetRow(box, row);
            Grid.SetColumn(box, 1);
            grid.Children.Add(box);
            return box;
        }

        private TextBlock Label(string text)
        {
            var block = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 0, 4), TextWrapping = TextWrapping.Wrap };
            block.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.WindowTextKey);
            return block;
        }
    }
}
