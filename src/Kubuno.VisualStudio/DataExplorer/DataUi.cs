using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Kubuno.VisualStudio.Core.Data;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace Kubuno.VisualStudio.DataExplorer
{
    /// <summary>Shared WPF pieces of the Data Explorer and the query window (VS-themed, colours from theme keys only).</summary>
    internal static class DataUi
    {
        /// <summary>The image of a tree node (Visual Studio's own database images).</summary>
        public static ImageMoniker MonikerOf(DataNodeKind kind, bool expanded = false) => kind switch
        {
            DataNodeKind.Connection => KnownMonikers.Database,
            DataNodeKind.Schema => KnownMonikers.Schema,
            DataNodeKind.TablesFolder or DataNodeKind.ViewsFolder or DataNodeKind.FunctionsFolder or DataNodeKind.ProceduresFolder
                or DataNodeKind.ColumnsFolder or DataNodeKind.KeysFolder or DataNodeKind.IndexesFolder => expanded ? KnownMonikers.FolderOpened : KnownMonikers.FolderClosed,
            DataNodeKind.Table => KnownMonikers.Table,
            DataNodeKind.View => KnownMonikers.View,
            DataNodeKind.Column => KnownMonikers.Column,
            DataNodeKind.PrimaryKeyColumn => KnownMonikers.KeyColumn,
            DataNodeKind.PrimaryKey => KnownMonikers.Key,
            DataNodeKind.ForeignKey => KnownMonikers.ForeignKey,
            DataNodeKind.Index => KnownMonikers.ClusteredIndex,
            DataNodeKind.Function => KnownMonikers.ScalarFunction,
            DataNodeKind.Procedure => KnownMonikers.StoredProcedure,
            DataNodeKind.Loading => KnownMonikers.Loading,
            DataNodeKind.Error => KnownMonikers.StatusError,
            _ => KnownMonikers.StatusInformation,
        };

        /// <summary>The image of a connection, by provider.</summary>
        public static ImageMoniker MonikerOf(DataProviderKind? provider) => provider switch
        {
            DataProviderKind.Sqlite => KnownMonikers.DatabaseFile,
            DataProviderKind.SqlServer => KnownMonikers.SQLDatabase,
            _ => KnownMonikers.Database,
        };

        /// <summary>Runs async UI work from an event handler; faults are reported (FileAndForget), never thrown into WPF.</summary>
        public static void RunUi(Func<Task> work, string name)
        {
#pragma warning disable VSSDK007 // fire-and-forget from WPF/command handlers; FileAndForget reports faults.
            ThreadHelper.JoinableTaskFactory.RunAsync(work).FileAndForget("Kubuno/Data/" + name);
#pragma warning restore VSSDK007
        }

        /// <summary>A header with an image and a text (tree items).</summary>
        public static StackPanel ImageText(ImageMoniker moniker, string text, out CrispImage image, out TextBlock block)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            image = new CrispImage { Moniker = moniker, Width = 16, Height = 16, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
            block = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
            panel.Children.Add(image);
            panel.Children.Add(block);
            return panel;
        }

        /// <summary>A flat, underlined "tab" (the crate manager's style): a radio button in <paramref name="group"/>.</summary>
        public static RadioButton Tab(string text, string group)
        {
            var border = new FrameworkElementFactory(typeof(Border), "border");
            border.SetValue(Border.BorderThicknessProperty, new Thickness(0, 0, 0, 2));
            border.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
            border.SetValue(Border.PaddingProperty, new Thickness(4, 2, 4, 3));
            border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            border.AppendChild(presenter);
            var template = new ControlTemplate(typeof(RadioButton)) { VisualTree = border };
            var checkedTrigger = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
            checkedTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension(EnvironmentColors.SystemHighlightBrushKey), "border"));
            checkedTrigger.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
            template.Triggers.Add(checkedTrigger);
            var tab = new RadioButton { Content = text, GroupName = group, Template = template, Margin = new Thickness(0, 0, 12, 0), Cursor = Cursors.Hand, Focusable = true };
            tab.SetResourceReference(Control.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            System.Windows.Automation.AutomationProperties.SetName(tab, text);
            return tab;
        }

        /// <summary>
        /// A read-only, VS-themed grid for one result set. Rows are the <c>string?[]</c> arrays of the result; each column
        /// binds by position (<c>[i]</c>), never by name, so names with dots, brackets or duplicates cannot break the
        /// binding. NULL shows as a grey, italic <c>NULL</c>.
        /// </summary>
        public static DataGrid ResultGrid(ResultSetInfo result)
        {
            var grid = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                CanUserReorderColumns = true,
                CanUserSortColumns = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                SelectionMode = DataGridSelectionMode.Extended,
                SelectionUnit = DataGridSelectionUnit.CellOrRowHeader,
                ClipboardCopyMode = DataGridClipboardCopyMode.IncludeHeader,
                GridLinesVisibility = DataGridGridLinesVisibility.All,
                EnableColumnVirtualization = true,
                EnableRowVirtualization = true,
                BorderThickness = new Thickness(1),
                RowHeight = double.NaN,
                MinColumnWidth = 30,
            };
            VirtualizingPanel.SetVirtualizationMode(grid, VirtualizationMode.Recycling);
            grid.SetResourceReference(Control.BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
            grid.SetResourceReference(Control.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            grid.SetResourceReference(Control.BorderBrushProperty, EnvironmentColors.ToolWindowBorderBrushKey);
            grid.SetResourceReference(DataGrid.HorizontalGridLinesBrushProperty, EnvironmentColors.ToolWindowBorderBrushKey);
            grid.SetResourceReference(DataGrid.VerticalGridLinesBrushProperty, EnvironmentColors.ToolWindowBorderBrushKey);
            grid.RowBackground = Brushes.Transparent;
            grid.AlternatingRowBackground = Brushes.Transparent;

            var header = new Style(typeof(DataGridColumnHeader));
            header.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension(HeaderColors.DefaultBrushKey)));
            header.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension(HeaderColors.DefaultTextBrushKey)));
            header.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension(HeaderColors.SeparatorLineBrushKey)));
            header.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 1, 1)));
            header.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(5, 3, 5, 3)));
            var headerHover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            headerHover.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension(HeaderColors.MouseOverBrushKey)));
            headerHover.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension(HeaderColors.MouseOverTextBrushKey)));
            header.Triggers.Add(headerHover);
            grid.ColumnHeaderStyle = header;

            var cell = new Style(typeof(DataGridCell));
            cell.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            cell.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(4, 1, 4, 1)));
            cell.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension(EnvironmentColors.ToolWindowTextBrushKey)));
            var selected = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension(TreeViewColors.SelectedItemActiveBrushKey)));
            selected.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension(TreeViewColors.SelectedItemActiveTextBrushKey)));
            cell.Triggers.Add(selected);
            grid.CellStyle = cell;

            var headers = QueryText.ColumnHeaders(result);
            for (int i = 0; i < result.Columns.Count; i++)
            {
                grid.Columns.Add(TextColumn(headers[i], i, result.Columns[i].DbType));
            }

            grid.ItemsSource = result.Rows;
            System.Windows.Automation.AutomationProperties.SetName(grid, DataText.Results);
            return grid;
        }

        private static DataGridTemplateColumn TextColumn(string header, int index, string? dbType)
        {
            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetBinding(TextBlock.TextProperty, new Binding($"[{index}]") { Mode = BindingMode.OneTime, TargetNullValue = "NULL" });
            text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            text.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            var style = new Style(typeof(TextBlock));
            var isNull = new DataTrigger { Binding = new Binding($"[{index}]") { Mode = BindingMode.OneTime }, Value = null };
            isNull.Setters.Add(new Setter(TextBlock.ForegroundProperty, new DynamicResourceExtension(EnvironmentColors.SystemGrayTextBrushKey)));
            isNull.Setters.Add(new Setter(TextBlock.FontStyleProperty, FontStyles.Italic));
            style.Triggers.Add(isNull);
            text.SetValue(FrameworkElement.StyleProperty, style);

            // The header is a TextBlock, not a string: no access-key processing of '_' and no binding path semantics.
            var headerBlock = new TextBlock { Text = header, ToolTip = string.IsNullOrEmpty(dbType) ? header : $"{header} ({dbType})" };
            return new DataGridTemplateColumn
            {
                Header = headerBlock,
                CellTemplate = new DataTemplate { VisualTree = text },
                ClipboardContentBinding = new Binding($"[{index}]") { Mode = BindingMode.OneTime, TargetNullValue = "NULL" },
                MaxWidth = 600,
            };
        }

        /// <summary>A Visual Studio message box for an error (acceptable for errors: docs/ARCHITECTURE.md "Themed dialogs").</summary>
        public static void ShowError(string message)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            VsShellUtilities.ShowMessageBox(ServiceProvider.GlobalProvider, message, DataText.DataExplorer, OLEMSGICON.OLEMSGICON_CRITICAL, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }

        /// <summary>A Visual Studio Yes/No question; true for Yes.</summary>
        public static bool Confirm(string message)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            int result = VsShellUtilities.ShowMessageBox(ServiceProvider.GlobalProvider, message, DataText.DataExplorer, OLEMSGICON.OLEMSGICON_QUERY, OLEMSGBUTTON.OLEMSGBUTTON_YESNO, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_SECOND);
            return result == 6; // IDYES
        }
    }
}
