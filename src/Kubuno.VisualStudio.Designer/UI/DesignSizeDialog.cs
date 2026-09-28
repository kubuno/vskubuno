using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;

namespace Kubuno.VisualStudio.Designer.UI
{
    /// <summary>
    /// "Design Size..." on the design canvas' context menu (docs/DESIGNER.md §12): the view's width and height
    /// on the canvas, as whole pixels. A small VS-styled (<see cref="DialogWindow"/> + <see cref="VsBrushes"/>)
    /// dialog built in code, like the other dialogs of this extension.
    /// </summary>
    internal sealed class DesignSizeDialog : DialogWindow
    {
        private const double MinDesignWidth = 120;
        private const double MinDesignHeight = 80;

        private readonly TextBox _widthBox;
        private readonly TextBox _heightBox;
        private readonly TextBlock _error;

        private DesignSizeDialog(double width, double height)
        {
            Title = DesignerText.DesignSizeTitle;
            Width = 360;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            SetResourceReference(BackgroundProperty, VsBrushes.WindowKey);
            SetResourceReference(ForegroundProperty, VsBrushes.WindowTextKey);

            var panel = new StackPanel { Margin = new Thickness(12) };
            var hint = new TextBlock { Text = DesignerText.DesignSizeHint, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
            hint.SetResourceReference(ForegroundProperty, VsBrushes.WindowTextKey);
            panel.Children.Add(hint);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _widthBox = AddRow(grid, 0, DesignerText.DesignSizeWidth, width);
            _heightBox = AddRow(grid, 1, DesignerText.DesignSizeHeight, height);
            panel.Children.Add(grid);

            _error = new TextBlock { Text = DesignerText.InvalidDesignSize, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
            _error.SetResourceReference(ForegroundProperty, VsBrushes.ControlLinkTextKey);
            panel.Children.Add(_error);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var ok = new Button { Content = DesignerText.Ok, IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 6, 0) };
            var cancel = new Button { Content = DesignerText.Cancel, IsCancel = true, MinWidth = 80 };
            ok.Click += (_, _) =>
            {
                if (TryRead(out _, out _))
                {
                    DialogResult = true;
                }
                else
                {
                    _error.Visibility = Visibility.Visible;
                }
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);
            Content = panel;
            Loaded += (_, _) =>
            {
                _widthBox.Focus();
                _widthBox.SelectAll();
            };
        }

        /// <summary>Asks for a new design size; false when cancelled.</summary>
        public static bool TryAsk(double width, double height, out double newWidth, out double newHeight)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var dialog = new DesignSizeDialog(width, height);
            newWidth = width;
            newHeight = height;
            return dialog.ShowModal() == true && dialog.TryRead(out newWidth, out newHeight);
        }

        private bool TryRead(out double width, out double height)
        {
            height = 0;
            return TryParse(_widthBox.Text, MinDesignWidth, out width) & TryParse(_heightBox.Text, MinDesignHeight, out height);
        }

        private static bool TryParse(string text, double minimum, out double value) =>
            double.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= minimum && value <= 100000;

        private static TextBox AddRow(Grid grid, int row, string label, double value)
        {
            var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) };
            text.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.WindowTextKey);
            Grid.SetRow(text, row);
            grid.Children.Add(text);
            var box = new TextBox { Text = DesignSizeFormat(value), Margin = new Thickness(0, 4, 0, 4), MinWidth = 120 };
            Grid.SetRow(box, row);
            Grid.SetColumn(box, 1);
            grid.Children.Add(box);
            return box;
        }

        private static string DesignSizeFormat(double value) => Editing.DesignSizeInfo.Format(value);
    }
}
