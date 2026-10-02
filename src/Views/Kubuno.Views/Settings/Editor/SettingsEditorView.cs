using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Kubuno.Shared.UI;
using Kubuno.Views.Logic.Settings;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;

// A WPF control: every member runs on the UI thread of its dispatcher (the pane is created and used there only).
#pragma warning disable VSTHRD010

namespace Kubuno.Views.Settings.Editor
{
    /// <summary>
    /// The WPF surface of the settings editor (Windows Forms' Settings.settings designer): a toolbar (add, remove, the
    /// schema version, the app id, view code), a grid with one row per setting (Name, Type, Scope, Roaming, Value,
    /// Accepted Values, Previous Names, Description), a hint and a status line listing the problems. Built in code;
    /// only theme brushes. The file itself is <see cref="KbsettingsFile"/>; the pane keeps it in step with the text
    /// buffer.
    /// </summary>
    internal sealed class SettingsEditorView : UserControl
    {
        private readonly Action _viewCode;
        private readonly ObservableCollection<SettingRow> _rows = new ObservableCollection<SettingRow>();
        private readonly DataGrid _grid = new DataGrid();
        private readonly TextBox _version = new TextBox { Width = 48, Margin = new Thickness(4, 0, 12, 0), VerticalContentAlignment = VerticalAlignment.Center };
        private readonly TextBox _app = new TextBox { Width = 180, Margin = new Thickness(4, 0, 12, 0), VerticalContentAlignment = VerticalAlignment.Center };
        private readonly CheckBox _accountScoped = new CheckBox { Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        private readonly Button _add = new Button { Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 4, 0) };
        private readonly Button _remove = new Button { Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 12, 0) };
        private readonly Button _viewCodeButton = new Button { Padding = new Thickness(8, 2, 8, 2) };
        private readonly TextBlock _hint = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 6, 8, 4) };
        private readonly TextBlock _status = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(8, 2, 8, 4) };
        private readonly Border _banner = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(8, 4, 8, 4), Visibility = Visibility.Collapsed };
        private readonly TextBlock _bannerText = new TextBlock { TextWrapping = TextWrapping.Wrap };
        private bool _loading;

        public SettingsEditorView(Action viewCode)
        {
            _viewCode = viewCode;
            ThemedControls.AddImplicitStyles(Resources);
            this.SetResourceReference(BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
            this.SetResourceReference(ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);

            _add.Content = SettingsText.AddSetting;
            _remove.Content = SettingsText.RemoveSetting;
            _viewCodeButton.Content = SettingsText.ViewCode;
            foreach (var b in new[] { _add, _remove, _viewCodeButton })
            {
                b.SetResourceReference(StyleProperty, VsResourceKeys.ButtonStyleKey);
            }

            foreach (var t in new[] { _version, _app })
            {
                t.SetResourceReference(StyleProperty, VsResourceKeys.TextBoxStyleKey);
                t.LostKeyboardFocus += (_, _) => RaiseChanged();
                t.KeyDown += (_, e) =>
                {
                    if (e.Key == Key.Enter)
                    {
                        RaiseChanged();
                    }
                };
            }

            _version.ToolTip = SettingsText.VersionTip;
            _app.ToolTip = SettingsText.AppTip;
            _accountScoped.Content = SettingsText.AccountScoped;
            _accountScoped.ToolTip = SettingsText.AccountScopedTip;
            _accountScoped.SetResourceReference(ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            _accountScoped.Click += (_, _) => RaiseChanged();
            _add.Click += (_, _) => AddRow();
            _remove.Click += (_, _) => RemoveSelected();
            _viewCodeButton.Click += (_, _) => _viewCode();

            var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 4, 6, 4) };
            toolbar.Children.Add(_add);
            toolbar.Children.Add(_remove);
            toolbar.Children.Add(Label(SettingsText.Version));
            toolbar.Children.Add(_version);
            toolbar.Children.Add(Label(SettingsText.App));
            toolbar.Children.Add(_app);
            toolbar.Children.Add(_accountScoped);
            toolbar.Children.Add(_viewCodeButton);
            var toolbarBorder = new Border { Child = toolbar, BorderThickness = new Thickness(0, 0, 0, 1) };
            toolbarBorder.SetResourceReference(Border.BackgroundProperty, EnvironmentColors.CommandBarGradientBeginBrushKey);
            toolbarBorder.SetResourceReference(Border.BorderBrushProperty, EnvironmentColors.ToolWindowBorderBrushKey);

            _banner.Child = _bannerText;
            _banner.SetResourceReference(Border.BackgroundProperty, VsBrushes.InfoBackgroundKey);
            _banner.SetResourceReference(Border.BorderBrushProperty, EnvironmentColors.ToolWindowBorderBrushKey);
            _bannerText.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.InfoTextKey);
            _hint.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.SystemGrayTextBrushKey);
            _status.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.SystemGrayTextBrushKey);

            BuildGrid();

            var root = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(toolbarBorder, Dock.Top);
            DockPanel.SetDock(_banner, Dock.Top);
            DockPanel.SetDock(_hint, Dock.Top);
            DockPanel.SetDock(_status, Dock.Bottom);
            root.Children.Add(toolbarBorder);
            root.Children.Add(_banner);
            root.Children.Add(_hint);
            root.Children.Add(_status);
            root.Children.Add(_grid);
            Content = root;
            UpdateButtons();
        }

        /// <summary>Raised when the user changed something the file holds.</summary>
        public event EventHandler? Changed;

        /// <summary>The settings' file name, for the hint (<c>settings.kbsettings</c>).</summary>
        public string FileName { get; set; } = "settings.kbsettings";

        /// <summary>A text box of the editor (a cell, Version, App id) has the keyboard: Edit.Undo is its own.</summary>
        public bool IsEditingText => IsKeyboardFocusWithin && Keyboard.FocusedElement is TextBox;

        /// <summary>The grid's rows (tests).</summary>
        internal IReadOnlyList<SettingRow> Rows => _rows;

        /// <summary>Shows <paramref name="file"/> (read from the buffer); <paramref name="parseErrors"/> of an unreadable file show in the banner.</summary>
        public void Load(KbsettingsFile file, IReadOnlyList<string> parseErrors)
        {
            _loading = true;
            try
            {
                var selected = _grid.SelectedIndex;
                if (_grid.IsKeyboardFocusWithin)
                {
                    _grid.CommitEdit(DataGridEditingUnit.Row, true);
                }

                foreach (var r in _rows)
                {
                    r.Edited -= OnRowEdited;
                }

                _rows.Clear();
                foreach (var e in file.Entries)
                {
                    var row = SettingRow.From(e);
                    row.Edited += OnRowEdited;
                    _rows.Add(row);
                }

                _version.Text = file.Version.ToString(CultureInfo.InvariantCulture);
                _app.Text = file.App ?? string.Empty;
                _accountScoped.IsChecked = file.AccountScoped;
                var unreadable = parseErrors.Count > 0 && file.Entries.Count == 0;
                _banner.Visibility = unreadable ? Visibility.Visible : Visibility.Collapsed;
                _bannerText.Text = unreadable ? SettingsText.InvalidFile + " " + parseErrors[0] : string.Empty;
                _hint.Text = string.Format(CultureInfo.CurrentCulture, SettingsText.Hint, FileName);
                if (selected >= 0 && selected < _rows.Count)
                {
                    _grid.SelectedIndex = selected;
                }

                Validate();
            }
            finally
            {
                _loading = false;
            }
        }

        /// <summary>The file the grid describes now.</summary>
        public KbsettingsFile Current()
        {
            var file = new KbsettingsFile
            {
                Version = int.TryParse(_version.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var v) && v >= 1 ? v : 1,
                App = string.IsNullOrWhiteSpace(_app.Text) ? null : _app.Text.Trim(),
                AccountScoped = _accountScoped.IsChecked == true,
            };
            file.Entries.AddRange(_rows.Select(r => r.ToEntry()));
            return file;
        }

        /// <summary>Adds a setting with a fresh name and starts editing its name (tests call it too).</summary>
        internal void AddRow()
        {
            var n = 1;
            while (_rows.Any(r => string.Equals(r.Name, "Setting" + n, StringComparison.OrdinalIgnoreCase)))
            {
                n++;
            }

            var row = new SettingRow { Name = "Setting" + n };
            row.Edited += OnRowEdited;
            _rows.Add(row);
            _grid.SelectedItem = row;
            _grid.ScrollIntoView(row);
            if (IsLoaded)
            {
                _grid.CurrentCell = new DataGridCellInfo(row, _grid.Columns[0]);
                _grid.BeginEdit();
            }

            RaiseChanged();
        }

        internal void RemoveSelected()
        {
            var selected = _grid.SelectedItems.OfType<SettingRow>().ToList();
            if (selected.Count == 0)
            {
                return;
            }

            _grid.CommitEdit(DataGridEditingUnit.Row, true);
            foreach (var r in selected)
            {
                r.Edited -= OnRowEdited;
                _rows.Remove(r);
            }

            RaiseChanged();
        }

        private void OnRowEdited(object? sender, EventArgs e) => RaiseChanged();

        private void RaiseChanged()
        {
            if (_loading)
            {
                return;
            }

            Validate();
            UpdateButtons();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private void Validate()
        {
            var file = Current();
            var problems = file.Validate().ToList();
            for (var i = 0; i < _rows.Count; i++)
            {
                var entry = file.Entries[i];
                var mine = problems.Where(p => ReferenceEquals(p.Entry, entry)).Select(p => p.Message).ToList();
                _rows[i].Problem = mine.Count == 0 ? null : string.Join("\n", mine);
            }

            var errors = problems.Count(p => p.IsError);
            var warnings = problems.Count - errors;
            _status.Text = problems.Count == 0
                ? SettingsText.NoProblem(_rows.Count)
                : SettingsText.Problems(errors, warnings) + string.Join(" · ", problems.Take(3).Select(p => p.Message));
            UpdateButtons();
        }

        private void UpdateButtons() => _remove.IsEnabled = _grid.SelectedItems.Count > 0;

        private static TextBlock Label(string text)
        {
            var t = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
            t.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.CommandBarTextActiveBrushKey);
            return t;
        }

        private void BuildGrid()
        {
            _grid.ItemsSource = _rows;
            _grid.AutoGenerateColumns = false;
            _grid.CanUserAddRows = false;
            _grid.CanUserDeleteRows = false;
            _grid.CanUserResizeRows = false;
            _grid.CanUserReorderColumns = false;
            _grid.CanUserSortColumns = false;
            _grid.SelectionMode = DataGridSelectionMode.Extended;
            _grid.SelectionUnit = DataGridSelectionUnit.FullRow;
            _grid.HeadersVisibility = DataGridHeadersVisibility.Column;
            _grid.GridLinesVisibility = DataGridGridLinesVisibility.All;
            _grid.BorderThickness = new Thickness(0);
            _grid.SetResourceReference(Control.BackgroundProperty, TreeViewColors.BackgroundBrushKey);
            _grid.SetResourceReference(Control.ForegroundProperty, TreeViewColors.BackgroundTextBrushKey);
            _grid.SetResourceReference(DataGrid.HorizontalGridLinesBrushProperty, EnvironmentColors.GridLineBrushKey);
            _grid.SetResourceReference(DataGrid.VerticalGridLinesBrushProperty, EnvironmentColors.GridLineBrushKey);
            AutomationPropertiesHelper.SetName(_grid, SettingsText.EditorTitle);

            _grid.Columns.Add(Text(SettingsText.ColumnName, nameof(SettingRow.Name), 170));
            _grid.Columns.Add(Choice(SettingsText.ColumnType, nameof(SettingRow.Type), KbsettingsFile.Types, 100));
            _grid.Columns.Add(Choice(SettingsText.ColumnScope, nameof(SettingRow.Scope), KbsettingsFile.Scopes, 110));
            var roaming = new DataGridCheckBoxColumn { Header = SettingsText.ColumnRoaming, Binding = new Binding(nameof(SettingRow.Roaming)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 70 };
            var roamingCell = new Style(typeof(CheckBox));
            roamingCell.Setters.Add(new Setter(UIElement.IsEnabledProperty, new Binding(nameof(SettingRow.RoamingEnabled))));
            roamingCell.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center));
            roaming.ElementStyle = roamingCell;
            roaming.EditingElementStyle = roamingCell;
            _grid.Columns.Add(roaming);
            _grid.Columns.Add(Text(SettingsText.ColumnValue, nameof(SettingRow.Value), 150));
            _grid.Columns.Add(Text(SettingsText.ColumnValues, nameof(SettingRow.Values), 140));
            _grid.Columns.Add(Text(SettingsText.ColumnPreviousNames, nameof(SettingRow.PreviousNames), 120));
            _grid.Columns.Add(Text(SettingsText.ColumnDescription, nameof(SettingRow.Description), 0));

            var header = new Style(typeof(DataGridColumnHeader));
            header.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 4, 6, 4)));
            header.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 1, 1)));
            header.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Left));
            header.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension(HeaderColors.DefaultBrushKey)));
            header.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension(HeaderColors.DefaultTextBrushKey)));
            header.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension(HeaderColors.SeparatorLineBrushKey)));
            header.Setters.Add(new Setter(Control.TemplateProperty, HeaderTemplate()));
            _grid.ColumnHeaderStyle = header;

            var row = new Style(typeof(DataGridRow));
            row.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension(TreeViewColors.BackgroundBrushKey)));
            row.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension(TreeViewColors.BackgroundTextBrushKey)));
            row.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding(nameof(SettingRow.Problem))));
            var rowSelected = new MultiTrigger();
            rowSelected.Conditions.Add(new Condition(DataGridRow.IsSelectedProperty, true));
            rowSelected.Conditions.Add(new Condition(Selector.IsSelectionActiveProperty, true));
            rowSelected.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension(TreeViewColors.SelectedItemActiveBrushKey)));
            rowSelected.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension(TreeViewColors.SelectedItemActiveTextBrushKey)));
            var rowSelectedInactive = new MultiTrigger();
            rowSelectedInactive.Conditions.Add(new Condition(DataGridRow.IsSelectedProperty, true));
            rowSelectedInactive.Conditions.Add(new Condition(Selector.IsSelectionActiveProperty, false));
            rowSelectedInactive.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension(TreeViewColors.SelectedItemInactiveBrushKey)));
            rowSelectedInactive.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension(TreeViewColors.SelectedItemInactiveTextBrushKey)));
            row.Triggers.Add(rowSelectedInactive);
            row.Triggers.Add(rowSelected);
            _grid.RowStyle = row;

            var cell = new Style(typeof(DataGridCell));
            cell.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            cell.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            cell.Setters.Add(new Setter(Control.TemplateProperty, CellTemplate()));
            var cellSelected = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
            cellSelected.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            cellSelected.Setters.Add(new Setter(Control.ForegroundProperty, new Binding(nameof(Control.Foreground)) { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(DataGridRow), 1) }));
            cell.Triggers.Add(cellSelected);
            var cellEditing = new Trigger { Property = DataGridCell.IsEditingProperty, Value = true };
            cellEditing.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension(CommonControlsColors.TextBoxBackgroundFocusedBrushKey)));
            cellEditing.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension(CommonControlsColors.TextBoxTextFocusedBrushKey)));
            cell.Triggers.Add(cellEditing);
            _grid.CellStyle = cell;

            _grid.SelectionChanged += (_, _) => UpdateButtons();
            _grid.PreviewKeyDown += (_, e) =>
            {
                var editing = Keyboard.FocusedElement is TextBox or ComboBox;
                if (e.Key == Key.Delete && !editing)
                {
                    RemoveSelected();
                    e.Handled = true;
                }
                else if (e.Key == Key.Insert && !editing)
                {
                    AddRow();
                    e.Handled = true;
                }
            };
        }

        private static DataGridTextColumn Text(string header, string path, double width)
        {
            var column = new DataGridTextColumn { Header = header, Binding = new Binding(path) { UpdateSourceTrigger = UpdateSourceTrigger.LostFocus } };
            column.Width = width > 0 ? new DataGridLength(width) : new DataGridLength(1, DataGridLengthUnitType.Star);
            var edit = new Style(typeof(TextBox));
            edit.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            edit.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(2, 0, 2, 0)));
            edit.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension(CommonControlsColors.TextBoxBackgroundFocusedBrushKey)));
            edit.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension(CommonControlsColors.TextBoxTextFocusedBrushKey)));
            edit.Setters.Add(new Setter(TextBoxBase.CaretBrushProperty, new DynamicResourceExtension(CommonControlsColors.TextBoxTextFocusedBrushKey)));
            column.EditingElementStyle = edit;
            var show = new Style(typeof(TextBlock));
            show.Setters.Add(new Setter(TextBlock.PaddingProperty, new Thickness(4, 2, 4, 2)));
            show.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
            column.ElementStyle = show;
            return column;
        }

        private static DataGridComboBoxColumn Choice(string header, string path, IReadOnlyList<string> values, double width)
        {
            var column = new DataGridComboBoxColumn
            {
                Header = header,
                ItemsSource = values,
                SelectedItemBinding = new Binding(path) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
                Width = width,
            };
            var show = new Style(typeof(ComboBox));
            show.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(4, 2, 4, 2)));
            column.ElementStyle = show;
            // The editing combo box gets Visual Studio's themed style from the implicit styles of the view
            // (ThemedControls.AddImplicitStyles).
            return column;
        }

        private static ControlTemplate HeaderTemplate()
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.HorizontalAlignmentProperty, new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(content);
            var grid = new FrameworkElementFactory(typeof(Grid));
            grid.AppendChild(border);
            var grip = new FrameworkElementFactory(typeof(Thumb), "PART_RightHeaderGripper");
            grip.SetValue(FrameworkElement.WidthProperty, 6.0);
            grip.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
            grip.SetValue(FrameworkElement.CursorProperty, Cursors.SizeWE);
            var thumb = new FrameworkElementFactory(typeof(Border));
            thumb.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            grip.SetValue(Control.TemplateProperty, new ControlTemplate(typeof(Thumb)) { VisualTree = thumb });
            grid.AppendChild(grip);
            return new ControlTemplate(typeof(DataGridColumnHeader)) { VisualTree = grid };
        }

        private static ControlTemplate CellTemplate()
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            border.SetValue(UIElement.SnapsToDevicePixelsProperty, true);
            border.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
            return new ControlTemplate(typeof(DataGridCell)) { VisualTree = border };
        }

        /// <summary><c>AutomationProperties.Name</c>, so UI Automation (and screen readers) name the grid.</summary>
        private static class AutomationPropertiesHelper
        {
            public static void SetName(DependencyObject element, string name) => System.Windows.Automation.AutomationProperties.SetName(element, name);
        }
    }
}
