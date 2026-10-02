using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;

namespace Kubuno.Shared.UI
{
    /// <summary>
    /// The base of every modal dialog the extension shows (docs/ARCHITECTURE.md, "Themed dialogs"), so each one
    /// looks like Visual Studio's own in the dark, light, blue and high-contrast themes:
    /// <list type="bullet">
    /// <item>the window uses the themed-dialog panel colors (<see cref="ThemedDialogColors.WindowPanelBrushKey"/> /
    /// <see cref="ThemedDialogColors.WindowPanelTextBrushKey"/>) instead of WPF's white;</item>
    /// <item>standard controls get Visual Studio's themed-dialog styles implicitly (<see cref="VsResourceKeys"/>:
    /// buttons, text boxes, check boxes, radio buttons, combo boxes, list boxes, list views and their items and
    /// column headers, tree views, labels, hyperlinks), including controls generated later by data templates;</item>
    /// <item>the title bar follows the theme: Windows' immersive dark mode is switched on when the panel color is
    /// dark (DWMWA_USE_IMMERSIVE_DARK_MODE) and updated when the theme changes.</item>
    /// </list>
    /// Every color is a dynamic resource, so switching the Visual Studio theme while the dialog is open restyles
    /// it. Build content in code as usual; use <see cref="ThemedControls"/> for the few controls that need more
    /// than an implicit style (grid list views, placeholder text boxes, secondary text).
    /// Part of Kubuno.Shared (every layer references it). The two template wizard assemblies, which keep a minimal
    /// dependency closure (they are loaded by the template engine, not by the package), compile this file as source
    /// instead, with KUBUNO_SHARED_AS_SOURCE defined so the types stay internal to them.
    /// </summary>
#if KUBUNO_SHARED_AS_SOURCE
    internal
#else
    public
#endif
    class ThemedDialog : DialogWindow
    {
        private const int DwmwaUseImmersiveDarkMode = 20;

        public ThemedDialog()
        {
            ThemedDialogStyleLoader.SetUseDefaultThemedDialogStyles(this, true);
            SetResourceReference(BackgroundProperty, ThemedDialogColors.WindowPanelBrushKey);
            SetResourceReference(ForegroundProperty, ThemedDialogColors.WindowPanelTextBrushKey);
            ShowInTaskbar = false;
            HasMinimizeButton = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            ThemedControls.AddImplicitStyles(Resources);

            SourceInitialized += (_, _) => ApplyTitleBarTheme();
            VSColorTheme.ThemeChanged += OnThemeChanged;
            Closed += (_, _) => VSColorTheme.ThemeChanged -= OnThemeChanged;
        }

        private void OnThemeChanged(ThemeChangedEventArgs e) => ApplyTitleBarTheme();

        /// <summary>Dark title bar when the dialog panel is dark (Windows 10 20H1+ / Windows 11; ignored elsewhere).</summary>
        private void ApplyTitleBarTheme()
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }
            int dark = IsDark(TryFindResource(ThemedDialogColors.WindowPanelColorKey)) ? 1 : 0;
            try
            {
                DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
            }
            catch (DllNotFoundException)
            {
            }
            catch (EntryPointNotFoundException)
            {
            }
        }

        public static bool IsDark(object? color) =>
            color is Color c && (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) < 128;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }

    /// <summary>
    /// Visual Studio-themed controls for code-built dialogs and tool-window content (see <see cref="ThemedDialog"/>).
    /// </summary>
#if KUBUNO_SHARED_AS_SOURCE
    internal
#else
    public
#endif
    static class ThemedControls
    {
        /// <summary>
        /// Adds Visual Studio's themed-dialog styles as implicit (type-keyed) styles to <paramref name="resources"/>, so
        /// every control below them - including template-generated ones - is themed. Keys missing from the running
        /// Visual Studio are skipped. The styles themselves reference theme colors dynamically.
        /// </summary>
        public static void AddImplicitStyles(ResourceDictionary resources)
        {
            Add(resources, typeof(Button), VsResourceKeys.ThemedDialogButtonStyleKey);
            Add(resources, typeof(TextBox), VsResourceKeys.ThemedDialogTextBoxStyleKey);
            Add(resources, typeof(CheckBox), VsResourceKeys.ThemedDialogCheckBoxStyleKey);
            Add(resources, typeof(RadioButton), VsResourceKeys.ThemedDialogRadioButtonStyleKey);
            Add(resources, typeof(ComboBox), VsResourceKeys.ThemedDialogComboBoxStyleKey);
            Add(resources, typeof(ListBox), VsResourceKeys.ThemedDialogListBoxStyleKey);
            Add(resources, typeof(ListView), VsResourceKeys.ThemedDialogListViewStyleKey);
            Add(resources, typeof(ListViewItem), VsResourceKeys.ThemedDialogListViewItemStyleKey);
            Add(resources, typeof(GridViewColumnHeader), VsResourceKeys.ThemedDialogGridViewColumnHeaderStyleKey);
            Add(resources, typeof(TreeView), VsResourceKeys.ThemedDialogTreeViewStyleKey);
            Add(resources, typeof(TreeViewItem), VsResourceKeys.ThemedDialogTreeViewItemStyleKey);
            Add(resources, typeof(Label), VsResourceKeys.ThemedDialogLabelStyleKey);
            Add(resources, typeof(Hyperlink), VsResourceKeys.ThemedDialogHyperlinkStyleKey);
            Add(resources, typeof(System.Windows.Controls.Primitives.ToggleButton), VsResourceKeys.ThemedDialogToggleButtonStyleKey);
        }

        /// <summary>A list view with a grid view: the grid-row item style (themed selection) and themed column headers.</summary>
        public static ListView GridListView(GridView view)
        {
            var list = new ListView { View = view };
            if (Application.Current?.TryFindResource(VsResourceKeys.ThemedDialogListViewItemGridStyleKey) is Style itemStyle)
            {
                list.ItemContainerStyle = itemStyle;
            }
            if (Application.Current?.TryFindResource(VsResourceKeys.ThemedDialogGridViewColumnHeaderStyleKey) is Style headerStyle)
            {
                view.ColumnHeaderContainerStyle = headerStyle;
            }
            return list;
        }

        /// <summary>
        /// Puts <paramref name="box"/> in a container that shows <paramref name="placeholder"/> (gray) while the box is
        /// empty, like Visual Studio's search boxes; the placeholder is also the box's accessible name. Place the
        /// returned container in the layout instead of the box.
        /// </summary>
        public static Grid WithPlaceholder(TextBox box, string placeholder)
        {
            var container = new Grid();
            container.Children.Add(box);
            var hint = new TextBlock
            {
                Text = placeholder,
                IsHitTestVisible = false,
                Margin = new Thickness(5, 0, 5, 0),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            hint.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.SystemGrayTextBrushKey);
            hint.SetBinding(UIElement.VisibilityProperty, new System.Windows.Data.Binding(nameof(TextBox.Text)) { Source = box, Converter = EmptyToVisibleConverter.Instance });
            container.Children.Add(hint);
            System.Windows.Automation.AutomationProperties.SetName(box, placeholder);
            return container;
        }

        /// <summary>A secondary (gray) text line, e.g. a description or a status.</summary>
        public static TextBlock SecondaryText(string text)
        {
            var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
            block.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.SystemGrayTextBrushKey);
            return block;
        }

        /// <summary>The standard OK/Cancel-style button row: right-aligned, default and cancel buttons wired.</summary>
        public static StackPanel ButtonRow(params Button[] buttons)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            for (int i = 0; i < buttons.Length; i++)
            {
                buttons[i].MinWidth = 75;
                buttons[i].MinHeight = 23;
                buttons[i].Margin = new Thickness(i == 0 ? 0 : 7, 0, 0, 0);
                row.Children.Add(buttons[i]);
            }
            return row;
        }

        private static void Add(ResourceDictionary resources, Type type, object key)
        {
            if (resources.Contains(type))
            {
                return;
            }
            if (Application.Current?.TryFindResource(key) is Style style && (style.TargetType is null || style.TargetType.IsAssignableFrom(type)))
            {
                resources[type] = new Style(type, style);
            }
        }
    }

    /// <summary>Visible for an empty string, collapsed otherwise (placeholder text).</summary>
#if KUBUNO_SHARED_AS_SOURCE
    internal
#else
    public
#endif
    sealed class EmptyToVisibleConverter : System.Windows.Data.IValueConverter
    {
        public static EmptyToVisibleConverter Instance { get; } = new EmptyToVisibleConverter();

        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
