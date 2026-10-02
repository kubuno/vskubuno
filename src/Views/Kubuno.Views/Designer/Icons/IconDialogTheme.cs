using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;

namespace Kubuno.Desktop.Designer.Icons
{
    /// <summary>
    /// The icon picker's controls that Visual Studio has no themed-dialog style for (a tab control and its tabs, the tiles of a
    /// gallery), drawn with the theme's own colours only - dynamic resources, so a theme switch restyles them: tabs as Visual
    /// Studio's dialogs draw them (the panel's text, the selected one underlined in the accent colour), the tiles selected like
    /// a tree view's items (active and inactive selection, hover). Nothing falls back to WPF's default colours.
    /// </summary>
    internal static class IconDialogTheme
    {
        /// <summary>A tab control without WPF's chrome: the headers in a row, the content below on the dialog's panel.</summary>
        public static TabControl TabControl()
        {
            var tabs = new TabControl { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0) };
            var root = new FrameworkElementFactory(typeof(DockPanel));
            var headers = new FrameworkElementFactory(typeof(TabPanel));
            headers.SetValue(Panel.IsItemsHostProperty, true);
            headers.SetValue(DockPanel.DockProperty, Dock.Top);
            headers.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 2));
            var rule = new FrameworkElementFactory(typeof(Border));
            rule.SetValue(DockPanel.DockProperty, Dock.Top);
            rule.SetValue(FrameworkElement.HeightProperty, 1.0);
            rule.SetResourceReference(Border.BackgroundProperty, ThemedDialogColors.ListBoxBorderBrushKey);
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.ContentSourceProperty, "SelectedContent");
            root.AppendChild(headers);
            root.AppendChild(rule);
            root.AppendChild(content);
            tabs.Template = new ControlTemplate(typeof(TabControl)) { VisualTree = root };
            tabs.ItemContainerStyle = TabItemStyle();
            return tabs;
        }

        private static Style TabItemStyle()
        {
            var style = new Style(typeof(TabItem));
            style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
            var border = new FrameworkElementFactory(typeof(Border), "Chrome");
            border.SetValue(Border.BorderThicknessProperty, new Thickness(0, 0, 0, 2));
            border.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
            border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            border.SetValue(Border.PaddingProperty, new Thickness(10, 6, 10, 5));
            border.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 4, 0));
            var text = new FrameworkElementFactory(typeof(ContentPresenter), "Header");
            text.SetValue(ContentPresenter.ContentSourceProperty, "Header");
            text.SetResourceReference(TextElement.ForegroundProperty, ThemedDialogColors.WindowPanelTextBrushKey);
            border.AppendChild(text);
            var template = new ControlTemplate(typeof(TabItem)) { VisualTree = border };
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension(ThemedDialogColors.ListBoxBorderBrushKey), "Chrome"));
            var selected = new Trigger { Property = TabItem.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension(EnvironmentColors.SystemHighlightBrushKey), "Chrome"));
            selected.Setters.Add(new Setter(TextElement.FontWeightProperty, FontWeights.SemiBold, "Header"));
            var focused = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
            focused.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension(EnvironmentColors.CommandBarMouseOverBackgroundGradientBrushKey), "Chrome"));
            template.Triggers.Add(hover);
            template.Triggers.Add(selected);
            template.Triggers.Add(focused);
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            return style;
        }

        /// <summary>The tiles of a gallery: transparent at rest, the hover and the (active or inactive) selection of a tree view.</summary>
        public static Style GalleryItemStyle()
        {
            var style = new Style(typeof(ListBoxItem));
            style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.MarginProperty, new Thickness(1)));
            var border = new FrameworkElementFactory(typeof(Border), "Chrome");
            border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
            var content = new FrameworkElementFactory(typeof(ContentPresenter), "Content");
            content.SetResourceReference(TextElement.ForegroundProperty, ThemedDialogColors.ListBoxTextBrushKey);
            border.AppendChild(content);
            var template = new ControlTemplate(typeof(ListBoxItem)) { VisualTree = border };
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension(TreeViewColors.BackgroundBrushKey), "Chrome"));
            hover.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension(TreeViewColors.SelectedItemInactiveBrushKey), "Chrome"));
            var inactive = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            inactive.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension(TreeViewColors.SelectedItemInactiveBrushKey), "Chrome"));
            inactive.Setters.Add(new Setter(TextElement.ForegroundProperty, new DynamicResourceExtension(TreeViewColors.SelectedItemInactiveTextBrushKey), "Content"));
            var active = new MultiTrigger();
            active.Conditions.Add(new Condition(ListBoxItem.IsSelectedProperty, true));
            active.Conditions.Add(new Condition(Selector.IsSelectionActiveProperty, true));
            active.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension(TreeViewColors.SelectedItemActiveBrushKey), "Chrome"));
            active.Setters.Add(new Setter(TextElement.ForegroundProperty, new DynamicResourceExtension(TreeViewColors.SelectedItemActiveTextBrushKey), "Content"));
            var focused = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
            focused.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension(EnvironmentColors.SystemHighlightBrushKey), "Chrome"));
            template.Triggers.Add(hover);
            template.Triggers.Add(inactive);
            template.Triggers.Add(active);
            template.Triggers.Add(focused);
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            return style;
        }

        /// <summary>Visual Studio's own scroll bars for everything below <paramref name="root"/>.</summary>
        public static void ThemeScrollBars(DependencyObject root) => ImageThemingUtilities.SetThemeScrollBars(root, true);
    }
}
