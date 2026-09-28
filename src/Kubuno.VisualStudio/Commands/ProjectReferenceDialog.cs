using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;

namespace Kubuno.VisualStudio.Commands
{
    /// <summary>
    /// "Référence de projet..." dialog: a checkbox list of every other <c>.rsproj</c> in the
    /// solution, mirroring Visual Studio's own Reference Manager. Built in plain code rather than
    /// XAML/BAML (this project's classic, non-SDK <c>.csproj</c> has no <c>&lt;Page&gt;</c>/WPF
    /// build wiring yet - see the csproj comment next to <c>ProjectReferenceDialog</c>'s references
    /// if one is ever added). Themed via <see cref="VsBrushes"/> dynamic resources, which
    /// <see cref="DialogWindow"/> already makes available - "VS-styled", not a plain/browser dialog.
    /// </summary>
    internal sealed class ProjectReferenceDialog : DialogWindow
    {
        public ProjectReferenceDialog(IReadOnlyList<ReferenceCandidate> candidates)
        {
            Title = "Référence de projet";
            Width = 460;
            Height = 420;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            SetResourceReference(BackgroundProperty, VsBrushes.WindowKey);
            SetResourceReference(ForegroundProperty, VsBrushes.WindowTextKey);

            var root = new Grid { Margin = new Thickness(12) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var caption = new TextBlock
            {
                Text = "Cochez les projets .rsproj à référencer (dépendance de chemin dans Cargo.toml) :",
                Margin = new Thickness(0, 0, 0, 8),
                TextWrapping = TextWrapping.Wrap,
            };
            caption.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.WindowTextKey);
            Grid.SetRow(caption, 0);
            root.Children.Add(caption);

            var list = new ListBox
            {
                BorderThickness = new Thickness(1),
                ItemsSource = candidates,
                SelectionMode = SelectionMode.Single,
            };
            list.SetResourceReference(BackgroundProperty, VsBrushes.ComboBoxBackgroundKey);
            list.SetResourceReference(BorderBrushProperty, VsBrushes.ComboBoxBorderKey);
            var itemTemplate = new DataTemplate();
            var checkBoxFactory = new FrameworkElementFactory(typeof(CheckBox));
            checkBoxFactory.SetBinding(CheckBox.IsCheckedProperty, new System.Windows.Data.Binding(nameof(ReferenceCandidate.IsChecked)) { Mode = System.Windows.Data.BindingMode.TwoWay });
            checkBoxFactory.SetBinding(ContentControl.ContentProperty, new System.Windows.Data.Binding(nameof(ReferenceCandidate.CrateName)));
            checkBoxFactory.SetBinding(ToolTipProperty, new System.Windows.Data.Binding(nameof(ReferenceCandidate.ManifestPath)));
            itemTemplate.VisualTree = checkBoxFactory;
            list.ItemTemplate = itemTemplate;
            Grid.SetRow(list, 1);
            root.Children.Add(list);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 6, 0) };
            var cancel = new Button { Content = "Annuler", IsCancel = true, MinWidth = 80 };
            ok.Click += (_, _) => { DialogResult = true; };
            cancel.Click += (_, _) => { DialogResult = false; };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            Grid.SetRow(buttons, 2);
            root.Children.Add(buttons);

            Content = root;
        }
    }
}
