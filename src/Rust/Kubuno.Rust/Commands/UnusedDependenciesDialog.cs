using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Kubuno.Rust.Logic.SolutionExplorer;
using Kubuno.Core.UI;
using Microsoft.VisualStudio.PlatformUI;

namespace Kubuno.Rust.Commands
{
    /// <summary>
    /// "Remove Unused Dependencies" - the checked list .NET's "Remove Unused References" dialog shows,
    /// filled from cargo-machete/cargo-udeps. VS-themed (<see cref="ThemedDialog"/>).
    /// </summary>
    internal sealed class UnusedDependenciesDialog : ThemedDialog
    {
        private readonly List<CheckBox> _boxes = new List<CheckBox>();

        public UnusedDependenciesDialog(string tool, IReadOnlyList<string> names)
        {
            Title = DependenciesText.RemoveUnusedTitle;
            Width = 480;
            Height = 400;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            HasMaximizeButton = false;

            var root = new DockPanel { Margin = new Thickness(12) };

            var caption = new TextBlock { Text = DependenciesText.RemoveUnusedCaption(tool), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
            DockPanel.SetDock(caption, Dock.Top);
            root.Children.Add(caption);

            var ok = new Button { Content = DependenciesText.Remove, IsDefault = true };
            var cancel = new Button { Content = DependenciesText.Cancel, IsCancel = true };
            ok.Click += (_, _) => DialogResult = true;
            StackPanel buttons = ThemedControls.ButtonRow(ok, cancel);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            var list = new StackPanel();
            foreach (var name in names.OrderBy(n => n, System.StringComparer.OrdinalIgnoreCase))
            {
                var box = new CheckBox { Content = name, IsChecked = true, Margin = new Thickness(4, 3, 4, 3) };
                _boxes.Add(box);
                list.Children.Add(box);
            }

            var frame = new Border { BorderThickness = new Thickness(1), Child = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
            frame.SetResourceReference(Border.BorderBrushProperty, ThemedDialogColors.ListBoxBorderBrushKey);
            frame.SetResourceReference(Border.BackgroundProperty, ThemedDialogColors.ListBoxBrushKey);
            root.Children.Add(frame);
            Content = root;
        }

        public IReadOnlyList<string> SelectedNames => _boxes.Where(b => b.IsChecked == true).Select(b => (string)b.Content).ToList();
    }
}
