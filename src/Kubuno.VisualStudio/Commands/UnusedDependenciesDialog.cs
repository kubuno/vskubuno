using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Kubuno.VisualStudio.Core.SolutionExplorer;
using Microsoft.VisualStudio.PlatformUI;

namespace Kubuno.VisualStudio.Commands
{
    /// <summary>
    /// "Remove Unused Dependencies" - the checked list .NET's "Remove Unused References" dialog shows,
    /// filled from cargo-machete/cargo-udeps. VS-themed (<see cref="ThemedDialogStyleLoader"/>).
    /// </summary>
    internal sealed class UnusedDependenciesDialog : DialogWindow
    {
        private readonly List<CheckBox> _boxes = new List<CheckBox>();

        public UnusedDependenciesDialog(string tool, IReadOnlyList<string> names)
        {
            Title = DependenciesText.RemoveUnusedTitle;
            Width = 480;
            Height = 400;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            HasMinimizeButton = false;
            HasMaximizeButton = false;
            ThemedDialogStyleLoader.SetUseDefaultThemedDialogStyles(this, true);

            var root = new DockPanel { Margin = new Thickness(12) };

            var caption = new TextBlock { Text = DependenciesText.RemoveUnusedCaption(tool), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
            DockPanel.SetDock(caption, Dock.Top);
            root.Children.Add(caption);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var ok = new Button { Content = DependenciesText.Remove, IsDefault = true, MinWidth = 86, Margin = new Thickness(0, 0, 6, 0) };
            var cancel = new Button { Content = DependenciesText.Cancel, IsCancel = true, MinWidth = 86 };
            ok.Click += (_, _) => DialogResult = true;
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            var list = new StackPanel();
            foreach (var name in names.OrderBy(n => n, System.StringComparer.OrdinalIgnoreCase))
            {
                var box = new CheckBox { Content = name, IsChecked = true, Margin = new Thickness(4, 3, 4, 3) };
                _boxes.Add(box);
                list.Children.Add(box);
            }

            root.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, BorderThickness = new Thickness(1) });
            Content = root;
        }

        public IReadOnlyList<string> SelectedNames => _boxes.Where(b => b.IsChecked == true).Select(b => (string)b.Content).ToList();
    }
}
