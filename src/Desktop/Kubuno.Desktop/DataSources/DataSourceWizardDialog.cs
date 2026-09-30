using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Kubuno.Desktop.Logic.Data;
using Kubuno.Desktop.Logic.DataSources;
using Kubuno.Desktop.DataExplorer;
using Kubuno.Core.UI;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;

namespace Kubuno.Desktop.DataSources
{
    /// <summary>
    /// "Ajouter une source de données..." / "Configure..." (docs/DATA.md §9, DATA-6; Visual Studio's Data Source Configuration Wizard):
    /// four pages - (a) the Data Explorer connection (or a new one), (b) the connection string's name in the application and the
    /// project's secret store, (c) the tables and views (schema tree with check boxes, loaded off the UI thread), (d) the source's
    /// name and what Finish writes - with Previous / Next / Finish / Cancel. The rules live in <see cref="DataSourceWizardModel"/>;
    /// Finish runs <see cref="IDataSourceWizardBackend.FinishAsync"/> and closes on success.
    /// </summary>
    internal sealed class DataSourceWizardDialog : ThemedDialog
    {
        private const double LabelWidth = 190;

        private readonly DataSourceWizardModel _model;
        private readonly IDataSourceWizardBackend _backend;
        private readonly TextBlock _stepTitle = new TextBlock { FontWeight = FontWeights.SemiBold, FontSize = 15, Margin = new Thickness(0, 0, 0, 3) };
        private readonly TextBlock _stepDescription = new TextBlock { TextWrapping = TextWrapping.Wrap };
        private readonly ContentControl _page = new ContentControl { Focusable = false };
        private readonly Button _previous = new Button();
        private readonly Button _next = new Button { IsDefault = true };
        private readonly Button _finish = new Button();
        private readonly Button _cancel = new Button { IsCancel = true };
        private readonly TextBlock _status = new TextBlock { TextWrapping = TextWrapping.Wrap, MinHeight = 18 };
        private readonly CrispImage _statusIcon = new CrispImage { Width = 16, Height = 16, Moniker = KnownMonikers.StatusError, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 1, 6, 0), VerticalAlignment = VerticalAlignment.Top };
        private readonly ProgressBar _progress = new ProgressBar { IsIndeterminate = true, Height = 4, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 0) };

        // Page (a).
        private readonly ListBox _connections = new ListBox { Height = 170 };
        private readonly TextBlock _connectionDetails = new TextBlock { TextWrapping = TextWrapping.Wrap };

        // Page (b).
        private readonly TextBox _connectionName = new TextBox();
        private readonly TextBlock _keyPreview = new TextBlock();
        private readonly RadioButton _userSecrets = new RadioButton { GroupName = "store" };
        private readonly RadioButton _credentialManager = new RadioButton { GroupName = "store" };

        // Page (c).
        private readonly TreeView _objects = new TreeView { Height = 240 };
        private readonly CheckBox _selectAll = new CheckBox();

        // Page (d).
        private readonly TextBox _sourceName = new TextBox();
        private readonly StackPanel _summary = new StackPanel();

        private CancellationTokenSource? _schemaLoad;
        private string? _loadedFor;
        private bool _busy;
        private bool _updatingChecks;

        public DataSourceWizardDialog(DataSourceWizardModel model, IDataSourceWizardBackend backend)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _model = model;
            _backend = backend;
            Title = DataSourcesText.WizardTitle;
            Width = 620;
            Height = 560;
            MinWidth = 520;
            MinHeight = 480;
            ResizeMode = ResizeMode.CanResizeWithGrip;

            var root = new DockPanel { Margin = new Thickness(14, 12, 14, 12) };

            var header = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            header.Children.Add(_stepTitle);
            _stepDescription.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.SystemGrayTextBrushKey);
            header.Children.Add(_stepDescription);
            var separator = new Border { Height = 1, Margin = new Thickness(0, 8, 0, 0) };
            separator.SetResourceReference(Border.BackgroundProperty, EnvironmentColors.ToolWindowBorderBrushKey);
            header.Children.Add(separator);
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            // Buttons and status at the bottom.
            _previous.Content = DataSourcesText.Previous;
            _next.Content = DataSourcesText.Next;
            _finish.Content = DataSourcesText.Finish;
            _cancel.Content = DataSourcesText.Cancel;
            _previous.Click += (_, _) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                GoBack();
            };
            _next.Click += (_, _) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                GoNext();
            };
            _finish.Click += (_, _) => DataUi.RunUi(FinishAsync, "DataSources/Finish");
            var bottom = new StackPanel();
            var statusRow = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            DockPanel.SetDock(_statusIcon, Dock.Left);
            statusRow.Children.Add(_statusIcon);
            statusRow.Children.Add(_status);
            System.Windows.Automation.AutomationProperties.SetLiveSetting(_status, System.Windows.Automation.AutomationLiveSetting.Polite);
            bottom.Children.Add(statusRow);
            bottom.Children.Add(_progress);
            bottom.Children.Add(ThemedControls.ButtonRow(_previous, _next, _finish, _cancel));
            DockPanel.SetDock(bottom, Dock.Bottom);
            root.Children.Add(bottom);

            root.Children.Add(_page);
            Content = root;

            BuildPages();
            Loaded += (_, _) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                ShowStep();
            };
            Closed += (_, _) => _schemaLoad?.Cancel();
        }

        /// <summary>The source written by Finish (null until then).</summary>
        public string? CreatedSource { get; private set; }

        private FrameworkElement? _connectionPage;
        private FrameworkElement? _applicationPage;
        private FrameworkElement? _objectsPage;
        private FrameworkElement? _namePage;

        private void BuildPages()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            // (a) The connection.
            var a = new DockPanel();
            var newConnection = new Button { Content = DataSourcesText.NewConnection, Padding = new Thickness(10, 1, 10, 1), MinHeight = 23, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
            newConnection.Click += (_, _) => DataUi.RunUi(NewConnectionAsync, "DataSources/NewConnection");
            var aTop = new StackPanel();
            aTop.Children.Add(new Label { Content = DataSourcesText.ConnectionLabel, Target = _connections, Padding = new Thickness(0, 0, 0, 3) });
            DockPanel.SetDock(aTop, Dock.Top);
            a.Children.Add(aTop);
            var aBottom = new StackPanel();
            aBottom.Children.Add(newConnection);
            aBottom.Children.Add(new TextBlock { Text = DataSourcesText.ConnectionDetails, Margin = new Thickness(0, 10, 0, 2), FontWeight = FontWeights.SemiBold });
            _connectionDetails.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.SystemGrayTextBrushKey);
            aBottom.Children.Add(_connectionDetails);
            DockPanel.SetDock(aBottom, Dock.Bottom);
            a.Children.Add(aBottom);
            System.Windows.Automation.AutomationProperties.SetName(_connections, DataSourcesText.ConnectionLabel.TrimEnd(':', ' ', ' '));
            System.Windows.Automation.AutomationProperties.SetAutomationId(_connections, "KubunoDataSourceConnections");
            _connections.SelectionChanged += (_, _) =>
            {
                if (_connections.SelectedItem is ListBoxItem { Tag: ExplorerConnectionInfo info })
                {
                    _model.ExplorerConnection = info.Name;
                }

                UpdateConnectionDetails();
                ClearStatus();
            };
            _connections.MouseDoubleClick += (_, _) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                GoNext();
            };
            a.Children.Add(_connections);
            _connectionPage = a;

            // (b) The application's connection string.
            var b = new StackPanel();
            var grid = FieldGrid();
            AddRow(grid, DataSourcesText.ConnectionStringName, _connectionName);
            _keyPreview.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.SystemGrayTextBrushKey);
            AddRow(grid, string.Empty, _keyPreview);
            b.Children.Add(grid);
            _connectionName.TextChanged += (_, _) =>
            {
                if (_connectionName.IsKeyboardFocusWithin)
                {
                    _model.ConnectionName = _connectionName.Text;
                }

                _keyPreview.Text = DataSourcesText.KeyPreview(_connectionName.Text.Trim());
                ClearStatus();
            };
            b.Children.Add(new TextBlock { Text = DataSourcesText.SecretStore, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 4) });
            _userSecrets.Content = DataSourcesText.StoreUserSecrets;
            _credentialManager.Content = DataSourcesText.StoreCredentialManager;
            _credentialManager.Margin = new Thickness(0, 3, 0, 0);
            _userSecrets.Checked += (_, _) => _model.Store = CredentialStoreKind.UserSecrets;
            _credentialManager.Checked += (_, _) => _model.Store = CredentialStoreKind.CredentialManager;
            b.Children.Add(_userSecrets);
            b.Children.Add(_credentialManager);
            var note = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
            var shield = new CrispImage { Moniker = KnownMonikers.StatusInformation, Width = 16, Height = 16, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Top };
            DockPanel.SetDock(shield, Dock.Left);
            note.Children.Add(shield);
            note.Children.Add(ThemedControls.SecondaryText(DataSourcesText.NeverInProject));
            b.Children.Add(note);
            _applicationPage = b;

            // (c) The objects.
            var c = new DockPanel();
            var cTop = new StackPanel();
            if (_model.ModuleSchema is { } schema)
            {
                var module = ThemedControls.SecondaryText(DataSourcesText.ModuleSchemaNote(schema));
                module.Margin = new Thickness(0, 0, 0, 6);
                cTop.Children.Add(module);
            }

            _selectAll.Content = DataSourcesText.SelectAll;
            _selectAll.Margin = new Thickness(0, 0, 0, 6);
            // Checked/Unchecked (not Click): a keyboard or UI Automation toggle raises them too.
            RoutedEventHandler selectAll = (_, _) =>
            {
                if (_updatingChecks || _selectAll.IsChecked is null)
                {
                    return;
                }

                foreach (var o in _model.Objects)
                {
                    o.Selected = _selectAll.IsChecked == true;
                }

                RefreshObjectChecks();
                ClearStatus();
            };
            _selectAll.Checked += selectAll;
            _selectAll.Unchecked += selectAll;
            cTop.Children.Add(_selectAll);
            DockPanel.SetDock(cTop, Dock.Top);
            c.Children.Add(cTop);
            System.Windows.Automation.AutomationProperties.SetName(_objects, DataSourcesText.StepTitle(DataSourceWizardStep.Objects));
            System.Windows.Automation.AutomationProperties.SetAutomationId(_objects, "KubunoDataSourceObjects");
            c.Children.Add(_objects);
            _objectsPage = c;

            // (d) The name and the summary.
            var d = new DockPanel();
            var nameGrid = FieldGrid();
            AddRow(nameGrid, DataSourcesText.DataSourceName, _sourceName);
            DockPanel.SetDock(nameGrid, Dock.Top);
            d.Children.Add(nameGrid);
            _sourceName.TextChanged += (_, _) =>
            {
                if (_sourceName.IsKeyboardFocusWithin)
                {
                    _model.SourceName = _sourceName.Text;
                }

                UpdateSummary();
                ClearStatus();
            };
            var summaryTitle = new TextBlock { Text = DataSourcesText.SummaryHeader, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) };
            DockPanel.SetDock(summaryTitle, Dock.Top);
            d.Children.Add(summaryTitle);
            d.Children.Add(new ScrollViewer { Content = _summary, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            _namePage = d;
        }

        private void ShowStep()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var step = _model.Step;
            _stepTitle.Text = DataSourcesText.StepTitle(step);
            _stepDescription.Text = DataSourcesText.StepDescription(step);
            switch (step)
            {
                case DataSourceWizardStep.Connection:
                    FillConnections();
                    _page.Content = _connectionPage;
                    _connections.Focus();
                    break;
                case DataSourceWizardStep.Application:
                    _connectionName.Text = _model.ConnectionName;
                    _keyPreview.Text = DataSourcesText.KeyPreview(_model.ConnectionName);
                    (_model.Store == CredentialStoreKind.UserSecrets ? _userSecrets : _credentialManager).IsChecked = true;
                    _page.Content = _applicationPage;
                    _connectionName.Focus();
                    _connectionName.SelectAll();
                    break;
                case DataSourceWizardStep.Objects:
                    _page.Content = _objectsPage;
                    if (!string.Equals(_loadedFor, _model.ExplorerConnection, StringComparison.Ordinal) || !_model.SchemaLoaded)
                    {
                        DataUi.RunUi(LoadObjectsAsync, "DataSources/Schema");
                    }
                    else
                    {
                        FillObjects();
                    }

                    break;
                default:
                    _sourceName.Text = _model.SourceName;
                    _sourceName.IsReadOnly = _model.IsReconfigure;
                    _page.Content = _namePage;
                    UpdateSummary();
                    _sourceName.Focus();
                    _sourceName.SelectAll();
                    break;
            }

            UpdateButtons();
        }

        private void UpdateButtons()
        {
            _previous.IsEnabled = !_busy && _model.CanGoBack;
            _next.IsEnabled = !_busy && !_model.IsLastStep;
            _finish.IsEnabled = !_busy;
            _next.IsDefault = !_model.IsLastStep;
            _finish.IsDefault = _model.IsLastStep;
            _page.IsEnabled = !_busy;
        }

        private void GoNext()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_busy)
            {
                return;
            }

            SyncFromControls();
            if (_model.Next() is { } error)
            {
                ShowError(error);
                return;
            }

            ClearStatus();
            ShowStep();
        }

        private void GoBack()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_busy)
            {
                return;
            }

            SyncFromControls();
            _model.Back();
            ClearStatus();
            ShowStep();
        }

        private void SyncFromControls()
        {
            switch (_model.Step)
            {
                case DataSourceWizardStep.Application:
                    if (!string.Equals(_connectionName.Text.Trim(), _model.ConnectionName, StringComparison.Ordinal))
                    {
                        _model.ConnectionName = _connectionName.Text;
                    }

                    break;
                case DataSourceWizardStep.Name:
                    if (!_model.IsReconfigure && !string.Equals(_sourceName.Text.Trim(), _model.SourceName, StringComparison.Ordinal))
                    {
                        _model.SourceName = _sourceName.Text;
                    }

                    break;
            }
        }

        private async Task FinishAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (_busy)
            {
                return;
            }

            SyncFromControls();
            if (_model.Step != DataSourceWizardStep.Objects || _model.SchemaLoaded)
            {
                if (_model.ValidateAll() is { } error)
                {
                    ShowStep();
                    ShowError(error);
                    return;
                }
            }
            else
            {
                ShowError(DataSourcesText.SchemaNotLoaded);
                return;
            }

            if (_model.IsReconfigure && !DataSourcesToolWindow.Confirm(DataSourcesText.ConfirmRewrite(_model.SourceName)))
            {
                return;
            }

            SetBusy(true, DataSourcesText.Working);
            string? failure;
            try
            {
                failure = await _backend.FinishAsync(_model);
            }
            catch (Exception exception)
            {
                failure = exception.Message;
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            SetBusy(false, null);
            if (failure != null)
            {
                ShowError(failure);
                return;
            }

            CreatedSource = _model.SourceName;
            DialogResult = true;
        }

        private async Task NewConnectionAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            string? created = await _backend.NewConnectionAsync(_model.Connections.Select(c => c.Name).ToList());
            if (created is null)
            {
                return;
            }

            try
            {
                var list = await _backend.ListConnectionsAsync();
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                _model.SetConnections(list, created);
            }
            catch (DataToolException exception)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                ShowError(exception.Message);
            }

            FillConnections();
        }

        private async Task LoadObjectsAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            string? connection = _model.ExplorerConnection;
            if (connection is null)
            {
                return;
            }

            _schemaLoad?.Cancel();
            var cancellation = new CancellationTokenSource();
            _schemaLoad = cancellation;
            _objects.Items.Clear();
            SetBusy(true, DataSourcesText.LoadingSchema);
            try
            {
                var schema = await _backend.LoadSchemaAsync(connection, cancellation.Token);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (cancellation.IsCancellationRequested)
                {
                    return;
                }

                _model.LoadObjects(schema);
                _loadedFor = connection;
                SetBusy(false, null);
                FillObjects();
                if (_model.SchemaError is { } error)
                {
                    ShowError(error);
                }
            }
            catch (OperationCanceledException)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                SetBusy(false, null);
            }
            catch (DataToolException exception)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                SetBusy(false, null);
                ShowError(exception.Message);
            }
        }

        private void FillConnections()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _connections.Items.Clear();
            foreach (var connection in _model.Connections)
            {
                var item = new ListBoxItem
                {
                    Content = DataUi.ImageText(DataUi.MonikerOf(connection.ProviderKind), connection.Name, out _, out _),
                    Tag = connection,
                    ToolTip = connection.Display,
                };
                System.Windows.Automation.AutomationProperties.SetName(item, connection.Name);
                _connections.Items.Add(item);
                if (string.Equals(connection.Name, _model.ExplorerConnection, StringComparison.Ordinal))
                {
                    item.IsSelected = true;
                }
            }

            UpdateConnectionDetails();
        }

        private void UpdateConnectionDetails()
        {
            var info = _model.ExplorerConnectionInfo;
            _connectionDetails.Text = info is null
                ? DataSourcesText.ChooseConnection
                : DataText.ProviderName(info.ProviderKind ?? DataProviderKind.Postgres) + " · " + info.Display + "\n" + DataText.StoreName(info.Store);
        }

        private void FillObjects()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _objects.Items.Clear();
            foreach (var group in _model.Objects.GroupBy(o => o.Schema))
            {
                var schemaItem = new TreeViewItem { Header = DataUi.ImageText(KnownMonikers.Schema, group.Key, out _, out _), IsExpanded = true, Focusable = false };
                System.Windows.Automation.AutomationProperties.SetName(schemaItem, group.Key);
                foreach (var kind in new[] { false, true })
                {
                    var objects = group.Where(o => o.IsView == kind).ToList();
                    if (objects.Count == 0)
                    {
                        continue;
                    }

                    string folderName = kind ? DataSourcesText.Views : DataSourcesText.Tables;
                    var folder = new TreeViewItem { Header = DataUi.ImageText(KnownMonikers.FolderOpened, folderName, out _, out _), IsExpanded = true, Focusable = false };
                    System.Windows.Automation.AutomationProperties.SetName(folder, folderName);
                    foreach (var o in objects)
                    {
                        var check = new CheckBox { IsChecked = o.Selected, Tag = o, VerticalAlignment = VerticalAlignment.Center };
                        var content = DataUi.ImageText(kind ? KnownMonikers.View : KnownMonikers.Table, o.Name, out _, out _);
                        check.Content = content;
                        System.Windows.Automation.AutomationProperties.SetName(check, o.Name);
                        RoutedEventHandler changed = (_, _) =>
                        {
                            if (_updatingChecks)
                            {
                                return;
                            }

                            o.Selected = check.IsChecked == true;
                            UpdateSelectAll();
                            ClearStatus();
                        };
                        check.Checked += changed;
                        check.Unchecked += changed;
                        var item = new TreeViewItem { Header = check, Focusable = false };
                        System.Windows.Automation.AutomationProperties.SetName(item, o.Name);
                        folder.Items.Add(item);
                    }

                    schemaItem.Items.Add(folder);
                }

                _objects.Items.Add(schemaItem);
            }

            UpdateSelectAll();
        }

        private void RefreshObjectChecks()
        {
            _updatingChecks = true;
            foreach (var check in AllChecks())
            {
                check.IsChecked = ((DataSourceWizardObject)check.Tag).Selected;
            }

            _updatingChecks = false;
            UpdateSelectAll();
        }

        private IEnumerable<CheckBox> AllChecks()
        {
            foreach (TreeViewItem schema in _objects.Items)
            {
                foreach (TreeViewItem folder in schema.Items)
                {
                    foreach (TreeViewItem item in folder.Items)
                    {
                        if (item.Header is CheckBox check)
                        {
                            yield return check;
                        }
                    }
                }
            }
        }

        private void UpdateSelectAll()
        {
            if (_updatingChecks)
            {
                return;
            }

            int selected = _model.Objects.Count(o => o.Selected);
            _updatingChecks = true;
            _selectAll.IsChecked = selected == 0 ? false : selected == _model.Objects.Count ? true : (bool?)null;
            _updatingChecks = false;
            _selectAll.IsEnabled = _model.Objects.Count > 0;
        }

        private void UpdateSummary()
        {
            _summary.Children.Clear();
            string name = _sourceName.Text.Trim();
            var facts = _backend.Facts(name);
            foreach (var line in _model.Summary(facts.HasDataModule, facts.ModExists, facts.NeedsFeature, facts.NeedsSecretsId))
            {
                var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
                var bullet = new TextBlock { Text = "•", Margin = new Thickness(0, 0, 6, 0) };
                DockPanel.SetDock(bullet, Dock.Left);
                row.Children.Add(bullet);
                row.Children.Add(new TextBlock { Text = line.Replace("<name>", name), TextWrapping = TextWrapping.Wrap });
                _summary.Children.Add(row);
            }
        }

        private void SetBusy(bool busy, string? message)
        {
            _busy = busy;
            _progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            Cursor = busy ? Cursors.AppStarting : null;
            if (busy && message != null)
            {
                _statusIcon.Visibility = Visibility.Collapsed;
                _status.Text = message;
                _status.SetResourceReference(TextBlock.ForegroundProperty, ThemedDialogColors.WindowPanelTextBrushKey);
            }
            else if (!busy)
            {
                _status.Text = string.Empty;
            }

            UpdateButtons();
        }

        private void ShowError(string message)
        {
            _statusIcon.Visibility = Visibility.Visible;
            _status.Text = message;
            _status.SetResourceReference(TextBlock.ForegroundProperty, ThemedDialogColors.WindowPanelTextBrushKey);
        }

        private void ClearStatus()
        {
            if (_busy)
            {
                return;
            }

            _statusIcon.Visibility = Visibility.Collapsed;
            _status.Text = string.Empty;
        }

        private static Grid FieldGrid()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LabelWidth) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            return grid;
        }

        private static void AddRow(Grid grid, string label, FrameworkElement field)
        {
            int row = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            if (label.Length > 0)
            {
                var labelControl = new Label { Content = label, Target = field, Padding = new Thickness(0, 3, 6, 3), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetRow(labelControl, row);
                grid.Children.Add(labelControl);
                System.Windows.Automation.AutomationProperties.SetName(field, label.TrimEnd(':', ' ', ' '));
            }

            field.Margin = new Thickness(0, 2, 0, 2);
            Grid.SetRow(field, row);
            Grid.SetColumn(field, 1);
            grid.Children.Add(field);
        }
    }

    /// <summary>The dialog gallery's backend: sample connections and schema, Finish only validates.</summary>
    internal sealed class SampleDataSourceWizardBackend : IDataSourceWizardBackend
    {
        public static IReadOnlyList<ExplorerConnectionInfo> Connections { get; } = new[]
        {
            new ExplorerConnectionInfo { Name = "Shop", Provider = "sqlite", Store = "usersecrets", Display = @"C:\data\shop.db" },
            new ExplorerConnectionInfo { Name = "Kubuno dev", Provider = "postgres", Store = "credman", Display = "localhost:5432/kubuno (user kubuno)" },
        };

        public Task<string?> NewConnectionAsync(IReadOnlyCollection<string> existing) => Task.FromResult<string?>(null);

        public Task<IReadOnlyList<ExplorerConnectionInfo>> ListConnectionsAsync() => Task.FromResult(Connections);

        public Task<DatabaseSchemaInfo> LoadSchemaAsync(string explorerConnection, CancellationToken cancellationToken) => Task.FromResult(new DatabaseSchemaInfo
        {
            Provider = "sqlite",
            Schemas = new List<SchemaInfo>
            {
                new SchemaInfo
                {
                    Name = "main",
                    Tables = new List<TableInfo>
                    {
                        new TableInfo { Name = "customers", Kind = "table", Columns = new List<ColumnInfo> { new ColumnInfo { Name = "id" }, new ColumnInfo { Name = "name" } } },
                        new TableInfo { Name = "orders", Kind = "table", Columns = new List<ColumnInfo> { new ColumnInfo { Name = "id" } } },
                        new TableInfo { Name = "v_orders", Kind = "view", Columns = new List<ColumnInfo> { new ColumnInfo { Name = "id" } } },
                    },
                },
            },
        });

        public DataSourceSummaryFacts Facts(string sourceName) => new DataSourceSummaryFacts(false, false, true, true);

        public Task<string?> FinishAsync(DataSourceWizardModel model) => Task.FromResult<string?>(null);
    }
}
