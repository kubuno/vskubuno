using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Kubuno.Desktop.Logic.Data;
using Kubuno.Core.UI;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;

namespace Kubuno.Desktop.DataExplorer
{
    /// <summary>
    /// "Ajouter une connexion" (Data Explorer, docs/DATA.md §9): name, provider and its fields, SSL/TLS, an "Advanced"
    /// part with the generated connection string (password masked) or a hand-written one, where the credentials are
    /// stored (Windows Credential Manager or user secrets - never the project), "Test Connection" (cancellable) and OK,
    /// which validates and stores the connection through <c>explorer.add</c>. The password is read from the
    /// <see cref="PasswordBox"/> only at the instant a request is built.
    /// </summary>
    internal sealed class AddConnectionDialog : ThemedDialog
    {
        private static readonly DataProviderKind[] Providers = { DataProviderKind.Postgres, DataProviderKind.Sqlite, DataProviderKind.MySql, DataProviderKind.SqlServer };

        private readonly ISet<string> _existingNames;
        private readonly Func<DataConnectionTarget, CancellationToken, Task<ConnectionTestResult>>? _test;
        private readonly Func<string, DataProviderKind, string, CredentialStoreKind, CancellationToken, Task<ExplorerConnectionInfo>>? _save;

        private readonly TextBox _name = new TextBox();
        private readonly ComboBox _provider = new ComboBox();
        private readonly StackPanel _sqlitePanel = new StackPanel();
        private readonly Grid _serverPanel;
        private readonly TextBox _file = new TextBox();
        private readonly CheckBox _createIfMissing = new CheckBox();
        private readonly TextBox _server = new TextBox();
        private readonly TextBox _port = new TextBox { Width = 70, HorizontalAlignment = HorizontalAlignment.Left };
        private readonly TextBox _database = new TextBox();
        private readonly ComboBox _authentication = new ComboBox();
        private readonly TextBox _user = new TextBox();
        private readonly PasswordBox _password = new PasswordBox();
        private readonly ComboBox _ssl = new ComboBox();
        private readonly CheckBox _trustCertificate = new CheckBox();
        private readonly ToggleButton _advancedToggle = new ToggleButton();
        private readonly StackPanel _advancedPanel = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 6, 0, 0) };
        private readonly TextBox _generated = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap };
        private readonly CheckBox _useRaw = new CheckBox();
        private readonly TextBox _raw = new TextBox { TextWrapping = TextWrapping.Wrap, AcceptsReturn = false, IsEnabled = false, MinHeight = 44 };
        private readonly RadioButton _credentialManager = new RadioButton { GroupName = "store" };
        private readonly RadioButton _userSecrets = new RadioButton { GroupName = "store" };
        private readonly Button _testButton = new Button();
        private readonly Button _ok = new Button { IsDefault = true };
        private readonly Button _cancel = new Button { IsCancel = true };
        private readonly CrispImage _statusIcon = new CrispImage { Width = 16, Height = 16, Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 6, 0) };
        private readonly TextBlock _status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        private readonly ProgressBar _progress = new ProgressBar { IsIndeterminate = true, Height = 4, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 0) };

        private DataProviderKind _currentProvider = DataProviderKind.Postgres;
        private CancellationTokenSource? _operation;
        private bool _nameEdited;
        private bool _settingName;

        /// <param name="existingNames">The names already used (OK refuses them, case-insensitively).</param>
        /// <param name="defaultStore">The credential store selected initially (Tools &gt; Options &gt; Kubuno &gt; Data).</param>
        /// <param name="test">Runs <c>connection.test</c>; <see langword="null"/> in the dialog gallery (the button then only validates).</param>
        /// <param name="save">Runs <c>explorer.add</c>; <see langword="null"/> in the dialog gallery (OK then only validates).</param>
        public AddConnectionDialog(
            IEnumerable<string> existingNames,
            CredentialStoreKind defaultStore,
            Func<DataConnectionTarget, CancellationToken, Task<ConnectionTestResult>>? test,
            Func<string, DataProviderKind, string, CredentialStoreKind, CancellationToken, Task<ExplorerConnectionInfo>>? save)
        {
            _existingNames = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
            _test = test;
            _save = save;

            Title = DataText.AddConnectionTitle;
            Width = 520;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;

            var root = new StackPanel { Margin = new Thickness(12) };

            // Name and provider.
            var top = FieldGrid();
            AddRow(top, DataText.ConnectionName, _name);
            foreach (var provider in Providers)
            {
                _provider.Items.Add(new ComboBoxItem { Content = DataText.ProviderName(provider), Tag = provider });
            }

            AddRow(top, DataText.Provider, _provider);
            root.Children.Add(top);

            // SQLite.
            var fileRow = new DockPanel();
            var browse = new Button { Content = DataText.Browse, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 1, 8, 1), MinWidth = 0 };
            browse.Click += (_, _) => BrowseForFile();
            DockPanel.SetDock(browse, Dock.Right);
            fileRow.Children.Add(browse);
            fileRow.Children.Add(_file);
            var sqliteGrid = FieldGrid();
            AddRow(sqliteGrid, DataText.DatabaseFile, fileRow, _file);
            _sqlitePanel.Children.Add(sqliteGrid);
            _createIfMissing.Content = DataText.CreateIfMissing;
            _createIfMissing.Margin = new Thickness(LabelWidth, 2, 0, 4);
            _sqlitePanel.Children.Add(_createIfMissing);
            root.Children.Add(_sqlitePanel);

            // Servers.
            _serverPanel = FieldGrid();
            AddRow(_serverPanel, DataText.Server, _server);
            AddRow(_serverPanel, DataText.Port, _port);
            AddRow(_serverPanel, DataText.Database, _database);
            _authentication.Items.Add(new ComboBoxItem { Content = DataText.SqlServerAuthentication, Tag = false });
            _authentication.Items.Add(new ComboBoxItem { Content = DataText.WindowsAuthentication, Tag = true });
            _authentication.SelectedIndex = 0;
            AddRow(_serverPanel, DataText.Authentication, _authentication);
            AddRow(_serverPanel, DataText.User, _user);
            StylePasswordBox(_password);
            AddRow(_serverPanel, DataText.Password, _password);
            AddRow(_serverPanel, DataText.Encryption, _ssl);
            _trustCertificate.Content = DataText.TrustServerCertificate;
            AddRow(_serverPanel, string.Empty, _trustCertificate);
            root.Children.Add(_serverPanel);

            // Advanced.
            _advancedToggle.Content = DataText.Advanced + " ▸";
            _advancedToggle.HorizontalAlignment = HorizontalAlignment.Left;
            _advancedToggle.Padding = new Thickness(8, 1, 8, 1);
            _advancedToggle.Margin = new Thickness(0, 8, 0, 0);
            _advancedToggle.Checked += (_, _) => { _advancedPanel.Visibility = Visibility.Visible; _advancedToggle.Content = DataText.Advanced + " ▾"; };
            _advancedToggle.Unchecked += (_, _) => { _advancedPanel.Visibility = Visibility.Collapsed; _advancedToggle.Content = DataText.Advanced + " ▸"; };
            root.Children.Add(_advancedToggle);
            _advancedPanel.Children.Add(new TextBlock { Text = DataText.GeneratedConnectionString, Margin = new Thickness(0, 0, 0, 3) });
            System.Windows.Automation.AutomationProperties.SetName(_generated, DataText.GeneratedConnectionString.TrimEnd(':', ' '));
            _advancedPanel.Children.Add(_generated);
            _useRaw.Content = DataText.UseRawConnectionString;
            _useRaw.Margin = new Thickness(0, 8, 0, 3);
            _advancedPanel.Children.Add(_useRaw);
            System.Windows.Automation.AutomationProperties.SetName(_raw, DataText.UseRawConnectionString.Replace("_", string.Empty));
            _advancedPanel.Children.Add(_raw);
            var rawHint = ThemedControls.SecondaryText(DataText.RawConnectionStringHint);
            rawHint.Margin = new Thickness(0, 3, 0, 0);
            _advancedPanel.Children.Add(rawHint);
            root.Children.Add(_advancedPanel);

            // Credential storage.
            var storeTitle = new TextBlock { Text = DataText.CredentialStorage, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) };
            root.Children.Add(storeTitle);
            _credentialManager.Content = DataText.CredentialManagerOption;
            _userSecrets.Content = DataText.UserSecretsOption;
            _userSecrets.Margin = new Thickness(0, 3, 0, 0);
            root.Children.Add(_credentialManager);
            root.Children.Add(_userSecrets);
            var explanation = ThemedControls.SecondaryText(DataText.CredentialExplanation);
            explanation.Margin = new Thickness(0, 4, 0, 0);
            root.Children.Add(explanation);
            (defaultStore == CredentialStoreKind.UserSecrets ? _userSecrets : _credentialManager).IsChecked = true;

            // Status.
            var statusRow = new DockPanel { Margin = new Thickness(0, 12, 0, 0), MinHeight = 20 };
            DockPanel.SetDock(_statusIcon, Dock.Left);
            statusRow.Children.Add(_statusIcon);
            statusRow.Children.Add(_status);
            System.Windows.Automation.AutomationProperties.SetAutomationId(_status, "KubunoDataStatus");
            System.Windows.Automation.AutomationProperties.SetLiveSetting(_status, System.Windows.Automation.AutomationLiveSetting.Polite);
            root.Children.Add(statusRow);
            root.Children.Add(_progress);

            // Buttons: Test Connection on the left, OK / Cancel on the right.
            _testButton.Content = DataText.TestConnection;
            _testButton.Padding = new Thickness(10, 1, 10, 1);
            _testButton.MinHeight = 23;
            _testButton.Click += (_, _) => OnTestClicked();
            _ok.Content = DataText.Ok;
            _ok.Click += (_, _) => OnOkClicked();
            _cancel.Content = DataText.Cancel;
            var buttons = new DockPanel { Margin = new Thickness(0, 12, 0, 0), LastChildFill = false };
            _testButton.HorizontalAlignment = HorizontalAlignment.Left;
            DockPanel.SetDock(_testButton, Dock.Left);
            buttons.Children.Add(_testButton);
            StackPanel okCancel = ThemedControls.ButtonRow(_ok, _cancel);
            okCancel.Margin = new Thickness(0);
            DockPanel.SetDock(okCancel, Dock.Right);
            buttons.Children.Add(okCancel);
            root.Children.Add(buttons);

            Content = root;

            // Live updates of the masked string.
            foreach (var box in new[] { _file, _server, _port, _database, _user })
            {
                box.TextChanged += (_, _) => OnFieldChanged();
            }

            _password.PasswordChanged += (_, _) => UpdateGenerated();
            _createIfMissing.Checked += (_, _) => UpdateGenerated();
            _createIfMissing.Unchecked += (_, _) => UpdateGenerated();
            _trustCertificate.Checked += (_, _) => UpdateGenerated();
            _trustCertificate.Unchecked += (_, _) => UpdateGenerated();
            _ssl.SelectionChanged += (_, _) => UpdateGenerated();
            _authentication.SelectionChanged += (_, _) => { UpdateAuthentication(); UpdateGenerated(); };
            _useRaw.Checked += (_, _) => UpdateRawMode();
            _useRaw.Unchecked += (_, _) => UpdateRawMode();
            _name.TextChanged += (_, _) =>
            {
                if (!_settingName)
                {
                    _nameEdited = _name.Text.Length > 0;
                }
            };
            _provider.SelectionChanged += (_, _) => OnProviderChanged();
            _provider.SelectedIndex = 0;
            OnProviderChanged();
            Closed += (_, _) => _operation?.Cancel();
            Loaded += (_, _) => _name.Focus();
        }

        /// <summary>The connection stored by OK (null in the dialog gallery or when cancelled).</summary>
        public ExplorerConnectionInfo? SavedConnection { get; private set; }

        private const double LabelWidth = 170;

        private DataProviderKind SelectedProvider => (_provider.SelectedItem as ComboBoxItem)?.Tag is DataProviderKind p ? p : DataProviderKind.Postgres;

        private bool IntegratedSecurity => SelectedProvider == DataProviderKind.SqlServer && (_authentication.SelectedItem as ComboBoxItem)?.Tag is true;

        private CredentialStoreKind SelectedStore => _userSecrets.IsChecked == true ? CredentialStoreKind.UserSecrets : CredentialStoreKind.CredentialManager;

        private static Grid FieldGrid()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LabelWidth) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            return grid;
        }

        /// <summary>A label (with its access key, targeting <paramref name="target"/>) and a field on a new row.</summary>
        private static void AddRow(Grid grid, string label, FrameworkElement field, Control? target = null)
        {
            int row = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var focusTarget = target ?? field as Control;
            if (label.Length > 0)
            {
                var labelControl = new Label { Content = label, Target = focusTarget, Padding = new Thickness(0, 3, 6, 3), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetRow(labelControl, row);
                grid.Children.Add(labelControl);
                if (focusTarget != null)
                {
                    System.Windows.Automation.AutomationProperties.SetName(focusTarget, label.Replace("_", string.Empty).TrimEnd(':', ' ', ' '));
                }
            }

            field.Margin = new Thickness(0, 2, 0, 2);
            Grid.SetRow(field, row);
            Grid.SetColumn(field, 1);
            grid.Children.Add(field);
        }

        /// <summary>A <see cref="PasswordBox"/> has no themed-dialog style: give it the themed text box colours.</summary>
        private static void StylePasswordBox(PasswordBox box)
        {
            box.SetResourceReference(Control.BackgroundProperty, CommonControlsColors.TextBoxBackgroundBrushKey);
            box.SetResourceReference(Control.ForegroundProperty, CommonControlsColors.TextBoxTextBrushKey);
            box.SetResourceReference(Control.BorderBrushProperty, CommonControlsColors.TextBoxBorderBrushKey);
            box.SetResourceReference(PasswordBox.CaretBrushProperty, CommonControlsColors.TextBoxTextBrushKey);
            box.SetResourceReference(PasswordBox.SelectionBrushProperty, CommonControlsColors.FocusVisualBrushKey);
            box.Padding = new Thickness(2, 2, 2, 2);
        }

        private void OnProviderChanged()
        {
            var previous = _currentProvider;
            var provider = SelectedProvider;
            _currentProvider = provider;
            bool sqlite = provider == DataProviderKind.Sqlite;
            _sqlitePanel.Visibility = sqlite ? Visibility.Visible : Visibility.Collapsed;
            _serverPanel.Visibility = sqlite ? Visibility.Collapsed : Visibility.Visible;

            // The port follows the provider unless the user typed another one.
            int oldDefault = DataConnectionStringBuilder.DefaultPort(previous);
            if (!sqlite && (_port.Text.Length == 0 || _port.Text == oldDefault.ToString(CultureInfo.InvariantCulture)))
            {
                _port.Text = DataConnectionStringBuilder.DefaultPort(provider).ToString(CultureInfo.InvariantCulture);
            }

            _ssl.Items.Clear();
            foreach (var (value, english, french) in DataConnectionStringBuilder.SslModes(provider))
            {
                _ssl.Items.Add(new ComboBoxItem { Content = DataText.IsFrench ? french : english, Tag = value });
            }

            _ssl.SelectedIndex = _ssl.Items.Count > 0 ? 0 : -1;
            SetRowVisibility(_authentication, provider == DataProviderKind.SqlServer);
            SetRowVisibility(_trustCertificate, provider == DataProviderKind.SqlServer);
            UpdateAuthentication();
            UpdateGenerated();
        }

        private void UpdateAuthentication()
        {
            bool login = !IntegratedSecurity;
            SetRowVisibility(_user, login);
            SetRowVisibility(_password, login);
        }

        /// <summary>Shows or hides a field and its label (same grid row).</summary>
        private void SetRowVisibility(FrameworkElement field, bool visible)
        {
            int row = Grid.GetRow(field);
            foreach (UIElement child in _serverPanel.Children)
            {
                if (Grid.GetRow(child) == row)
                {
                    child.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }

        private void OnFieldChanged()
        {
            if (!_nameEdited)
            {
                string suggestion = SelectedProvider == DataProviderKind.Sqlite ? SafeFileName(_file.Text) : _database.Text.Trim();
                suggestion = new string(suggestion.Where(c => char.IsLetterOrDigit(c) && c < 128 || c == ' ' || c == '_' || c == '.' || c == '-').ToArray()).Trim();
                _settingName = true;
                _name.Text = suggestion.Length > 64 ? suggestion.Substring(0, 64) : suggestion;
                _settingName = false;
            }

            UpdateGenerated();
        }

        private static string SafeFileName(string path)
        {
            try
            {
                return Path.GetFileNameWithoutExtension(path.Trim()) ?? string.Empty;
            }
            catch (ArgumentException)
            {
                return string.Empty;
            }
        }

        private DataConnectionSettings Settings()
        {
            var settings = new DataConnectionSettings
            {
                Provider = SelectedProvider,
                Server = _server.Text,
                Database = _database.Text,
                FilePath = _file.Text,
                CreateIfMissing = _createIfMissing.IsChecked == true,
                IntegratedSecurity = IntegratedSecurity,
                User = _user.Text,
                SslMode = SelectedProvider == DataProviderKind.SqlServer ? string.Empty : (_ssl.SelectedItem as ComboBoxItem)?.Tag as string ?? string.Empty,
                Encrypt = SelectedProvider == DataProviderKind.SqlServer ? (_ssl.SelectedItem as ComboBoxItem)?.Tag as string ?? string.Empty : string.Empty,
                TrustServerCertificate = SelectedProvider == DataProviderKind.SqlServer && _trustCertificate.IsChecked == true,
            };
            string port = _port.Text.Trim();
            if (port.Length > 0)
            {
                settings.Port = int.TryParse(port, NumberStyles.None, CultureInfo.InvariantCulture, out int p) ? p : -1;
            }

            return settings;
        }

        private bool HasPassword => !IntegratedSecurity && _password.SecurePassword.Length > 0;

        private void UpdateGenerated()
        {
            _generated.Text = DataConnectionStringBuilder.Display(Settings(), HasPassword);
        }

        private void UpdateRawMode()
        {
            bool raw = _useRaw.IsChecked == true;
            _raw.IsEnabled = raw;
            if (raw && _raw.Text.Length == 0)
            {
                // Start from the generated string, without the password (never copied into plain text).
                _raw.Text = DataConnectionStringBuilder.Display(Settings(), hasPassword: false);
            }

            foreach (var field in new UIElement[] { _file, _createIfMissing, _server, _port, _database, _authentication, _user, _password, _ssl, _trustCertificate })
            {
                field.IsEnabled = !raw;
            }
        }

        private void BrowseForFile()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = DataText.DatabaseFile.Replace("_", string.Empty).TrimEnd(':', ' ', ' '),
                Filter = DataText.SqliteFilter,
                OverwritePrompt = false,
                CheckFileExists = false,
                AddExtension = true,
                DefaultExt = ".db",
            };
            try
            {
                string current = _file.Text.Trim();
                if (current.Length > 0)
                {
                    dialog.InitialDirectory = Path.GetDirectoryName(current);
                    dialog.FileName = Path.GetFileName(current);
                }
            }
            catch (ArgumentException)
            {
                // Not a path: start from the default folder.
            }

            if (dialog.ShowDialog(this) == true)
            {
                _file.Text = dialog.FileName;
                if (!File.Exists(dialog.FileName))
                {
                    _createIfMissing.IsChecked = true;
                    UpdateGenerated();
                }
            }
        }

        /// <summary>The field errors (empty = the connection can be tested or saved).</summary>
        private List<string> FieldErrors()
        {
            var errors = new List<string>();
            if (_useRaw.IsChecked == true)
            {
                if (string.IsNullOrWhiteSpace(_raw.Text))
                {
                    errors.Add(DataText.ConnectionStringRequired);
                }

                return errors;
            }

            var settings = Settings();
            errors.AddRange(DataConnectionStringBuilder.Validate(settings, HasPassword));
            if (settings.Provider == DataProviderKind.Sqlite && errors.Count == 0 && !settings.CreateIfMissing && !SafeExists(settings.FilePath))
            {
                errors.Add(DataText.FileMissing);
            }

            return errors;
        }

        private static bool SafeExists(string path)
        {
            try
            {
                return File.Exists(path.Trim());
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>The connection string with its password: built right before a request, never kept.</summary>
        private string BuildConnectionString()
        {
            if (_useRaw.IsChecked == true)
            {
                return _raw.Text.Trim();
            }

            var settings = Settings();
            DataConnectionStringBuilder.EnsureSqliteFile(settings);
            return DataConnectionStringBuilder.Build(settings, HasPassword ? _password.Password : null);
        }

        private void OnTestClicked()
        {
            if (_operation != null)
            {
                _operation.Cancel();
                return;
            }

            var errors = FieldErrors();
            if (errors.Count > 0)
            {
                ShowStatus(string.Join(" ", errors), KnownMonikers.StatusError);
                return;
            }

            if (_test is null)
            {
                ShowStatus(DataText.TestSucceeded("(gallery)", 0), KnownMonikers.StatusOK);
                return;
            }

            _ = RunTestAsync();
        }

        private async Task RunTestAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            using var cts = new CancellationTokenSource();
            _operation = cts;
            SetBusy(true, DataText.Testing, test: true);
            try
            {
                var target = DataConnectionTarget.Inline(SelectedProvider, BuildConnectionString());
                var result = await _test!(target, cts.Token);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                ShowStatus(DataText.TestSucceeded(result.ServerVersion, result.ElapsedMs), KnownMonikers.StatusOK);
            }
            catch (OperationCanceledException)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                ShowStatus(DataText.TestCancelled, KnownMonikers.StatusInformation);
            }
            catch (Exception exception)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                ShowStatus(DataText.TestFailed(exception.Message), KnownMonikers.StatusError);
            }
            finally
            {
                _operation = null;
                SetBusy(false, null, test: true);
            }
        }

        private void OnOkClicked()
        {
            if (_operation != null)
            {
                return;
            }

            string name = _name.Text;
            if (string.IsNullOrWhiteSpace(name))
            {
                ShowStatus(DataText.NameRequired, KnownMonikers.StatusError);
                _name.Focus();
                return;
            }

            if (!DataConnectionStringBuilder.IsValidConnectionName(name))
            {
                ShowStatus(DataText.NameInvalid, KnownMonikers.StatusError);
                _name.Focus();
                return;
            }

            if (_existingNames.Contains(name))
            {
                ShowStatus(DataText.NameTaken(name), KnownMonikers.StatusError);
                _name.Focus();
                _name.SelectAll();
                return;
            }

            var errors = FieldErrors();
            if (errors.Count > 0)
            {
                ShowStatus(string.Join(" ", errors), KnownMonikers.StatusError);
                return;
            }

            if (_save is null)
            {
                DialogResult = true;
                return;
            }

            _ = RunSaveAsync(name);
        }

        private async Task RunSaveAsync(string name)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            using var cts = new CancellationTokenSource();
            _operation = cts;
            SetBusy(true, DataText.Saving, test: false);
            bool saved = false;
            try
            {
                var provider = SelectedProvider;
                var store = SelectedStore;
                SavedConnection = await _save!(name, provider, BuildConnectionString(), store, cts.Token);
                saved = true;
            }
            catch (OperationCanceledException)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                ShowStatus(DataText.TestCancelled, KnownMonikers.StatusInformation);
            }
            catch (Exception exception)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                ShowStatus(exception.Message, KnownMonikers.StatusError);
            }
            finally
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                _operation = null;
                SetBusy(false, null, test: false);
            }

            if (saved)
            {
                _password.Clear();
                DialogResult = true;
            }
        }

        private void SetBusy(bool busy, string? message, bool test)
        {
            _progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            _ok.IsEnabled = !busy;
            _testButton.IsEnabled = !busy || test;
            _testButton.Content = busy && test ? DataText.CancelTest : DataText.TestConnection;
            if (busy && message != null)
            {
                ShowStatus(message, null);
            }
        }

        private void ShowStatus(string text, Microsoft.VisualStudio.Imaging.Interop.ImageMoniker? icon)
        {
            _status.Text = text;
            if (icon is { } moniker)
            {
                _statusIcon.Moniker = moniker;
                _statusIcon.Visibility = Visibility.Visible;
            }
            else
            {
                _statusIcon.Visibility = Visibility.Collapsed;
            }
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            // Escape while a test runs cancels the test, not the dialog.
            if (e.Key == Key.Escape && _operation != null)
            {
                _operation.Cancel();
                e.Handled = true;
                return;
            }

            base.OnPreviewKeyDown(e);
        }
    }
}
