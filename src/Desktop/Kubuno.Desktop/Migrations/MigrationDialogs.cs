using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using Kubuno.VisualStudio.Core.Data;
using Kubuno.VisualStudio.Core.Migrations;
using Kubuno.VisualStudio.UI;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.Shell;

namespace Kubuno.VisualStudio.Migrations
{
    /// <summary>
    /// "Add Migration" (docs/DATA.md DATA-7): a description (validated with the helper's own rules,
    /// <see cref="MigrationDescription"/>), "Reversible (up/down)" checked by default, a preview of the files it creates
    /// and, for a module's first migration, the <c>CREATE SCHEMA</c> it starts with.
    /// </summary>
    internal sealed class AddMigrationDialog : ThemedDialog
    {
        private readonly TextBox _description = new TextBox();
        private readonly CheckBox _reversible = new CheckBox { IsChecked = true };
        private readonly TextBlock _preview;
        private readonly CrispImage _statusIcon = new CrispImage { Width = 16, Height = 16, Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 6, 0) };
        private readonly TextBlock _status = new TextBlock { TextWrapping = TextWrapping.Wrap };

        /// <param name="crateName">Shown in the title.</param>
        /// <param name="schema">The schema the first migration creates, or null (shown as a note).</param>
        public AddMigrationDialog(string crateName, string? schema)
        {
            Title = MigrationText.AddMigrationTitle + " - " + crateName;
            Width = 480;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;

            var root = new StackPanel { Margin = new Thickness(12) };
            var label = new Label { Content = MigrationText.DescriptionLabel, Target = _description, Padding = new Thickness(0, 0, 0, 4) };
            root.Children.Add(label);
            System.Windows.Automation.AutomationProperties.SetName(_description, MigrationText.DescriptionLabel.TrimEnd(' ', ':'));
            root.Children.Add(ThemedControls.WithPlaceholder(_description, "create customers"));

            _reversible.Content = MigrationText.ReversibleLabel;
            _reversible.Margin = new Thickness(0, 8, 0, 0);
            root.Children.Add(_reversible);

            _preview = ThemedControls.SecondaryText(string.Empty);
            _preview.Margin = new Thickness(0, 8, 0, 0);
            _preview.TextWrapping = TextWrapping.Wrap;
            root.Children.Add(_preview);

            if (!string.IsNullOrEmpty(schema))
            {
                var note = ThemedControls.SecondaryText(MigrationText.SchemaNote(schema!));
                note.TextWrapping = TextWrapping.Wrap;
                note.Margin = new Thickness(0, 4, 0, 0);
                root.Children.Add(note);
            }

            var statusRow = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            DockPanel.SetDock(_statusIcon, Dock.Left);
            statusRow.Children.Add(_statusIcon);
            statusRow.Children.Add(_status);
            System.Windows.Automation.AutomationProperties.SetName(_status, "Status");
            System.Windows.Automation.AutomationProperties.SetLiveSetting(_status, System.Windows.Automation.AutomationLiveSetting.Polite);
            root.Children.Add(statusRow);

            var ok = new Button { Content = MigrationText.AddButton, IsDefault = true };
            var cancel = new Button { Content = MigrationText.CancelButton, IsCancel = true };
            ok.Click += (_, _) => Accept();
            cancel.Click += (_, _) => DialogResult = false;
            root.Children.Add(ThemedControls.ButtonRow(ok, cancel));
            Content = root;

            _description.TextChanged += (_, _) => UpdatePreview();
            _reversible.Checked += (_, _) => UpdatePreview();
            _reversible.Unchecked += (_, _) => UpdatePreview();
            _description.Loaded += (_, _) => _description.Focus();
            UpdatePreview();
        }

        /// <summary>The description, trimmed (valid once the dialog returned true).</summary>
        public string Description => _description.Text.Trim();

        public bool Reversible => _reversible.IsChecked == true;

        /// <summary>For tests and the dialog gallery: pre-fills the description.</summary>
        public string DescriptionText
        {
            get => _description.Text;
            set => _description.Text = value;
        }

        private void UpdatePreview()
        {
            var files = MigrationDescription.FilesPreview(_description.Text, Reversible);
            _preview.Text = files.Length == 0 ? string.Empty : MigrationText.FilesPreview(files);
            ShowStatus(null);
        }

        private void Accept()
        {
            var error = MigrationDescription.Validate(_description.Text);
            if (error != null)
            {
                ShowStatus(error);
                _description.Focus();
                return;
            }

            DialogResult = true;
        }

        private void ShowStatus(string? error)
        {
            _status.Text = error ?? string.Empty;
            _statusIcon.Moniker = KnownMonikers.StatusError;
            _statusIcon.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    /// <summary>
    /// Asks which connection a crate's migrations use (docs/DATA.md DATA-7): an editable list of the names its data sources
    /// and user secrets know, and "Set the connection string...", which copies a Data Explorer connection's string into the
    /// crate's user secrets as <c>ConnectionStrings:&lt;name&gt;</c> (<c>secrets.copyToProject</c>) - the string itself never
    /// passes through this dialog.
    /// </summary>
    internal sealed class ConnectionNameDialog : ThemedDialog
    {
        private readonly ComboBox _name = new ComboBox { IsEditable = true, IsTextSearchEnabled = true };
        private readonly StackPanel _secretPanel = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 6, 0, 0) };
        private readonly ComboBox _explorer = new ComboBox();
        private readonly Button _copy = new Button();
        private readonly TextBlock _secretIntro;
        private readonly CrispImage _statusIcon = new CrispImage { Width = 16, Height = 16, Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 6, 0) };
        private readonly TextBlock _status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        private readonly string? _userSecretsId;
        private readonly Func<CancellationToken, Task<IReadOnlyList<ExplorerConnectionInfo>>>? _listExplorer;
        private readonly Func<string, string, CancellationToken, Task>? _copyToSecrets;
        private bool _explorerLoaded;

        /// <param name="intro">The explanation at the top.</param>
        /// <param name="candidates">The names offered (data sources first, then user secrets).</param>
        /// <param name="userSecretsId">The crate's user secrets id (null: the copy link explains how to add one).</param>
        /// <param name="listExplorer"><c>explorer.list</c>; null in the dialog gallery.</param>
        /// <param name="copyToSecrets">(explorer connection, connection name) → <c>secrets.copyToProject</c>; null in the gallery.</param>
        public ConnectionNameDialog(
            string intro,
            IReadOnlyList<string> candidates,
            string? suggested,
            string? userSecretsId,
            Func<CancellationToken, Task<IReadOnlyList<ExplorerConnectionInfo>>>? listExplorer,
            Func<string, string, CancellationToken, Task>? copyToSecrets)
        {
            _userSecretsId = userSecretsId;
            _listExplorer = listExplorer;
            _copyToSecrets = copyToSecrets;

            Title = MigrationText.ConnectionDialogTitle;
            Width = 500;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;

            var root = new StackPanel { Margin = new Thickness(12) };
            root.Children.Add(new TextBlock { Text = intro, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });

            root.Children.Add(new Label { Content = MigrationText.ConnectionNameLabel, Target = _name, Padding = new Thickness(0, 0, 0, 4) });
            foreach (var candidate in candidates)
            {
                _name.Items.Add(candidate);
            }

            _name.Text = suggested ?? candidates.FirstOrDefault() ?? string.Empty;
            System.Windows.Automation.AutomationProperties.SetName(_name, MigrationText.ConnectionNameLabel.TrimEnd(' ', ':'));
            root.Children.Add(_name);

            var link = new Hyperlink(new Run(MigrationText.SetConnectionStringLink));
            link.Click += (_, _) => ToggleSecretPanel();
            System.Windows.Automation.AutomationProperties.SetName(link, MigrationText.SetConnectionStringLink);
            root.Children.Add(new TextBlock(link) { Margin = new Thickness(0, 8, 0, 0) });

            _secretIntro = ThemedControls.SecondaryText(string.Empty);
            _secretIntro.TextWrapping = TextWrapping.Wrap;
            _secretPanel.Children.Add(_secretIntro);
            _secretPanel.Children.Add(new Label { Content = MigrationText.ExplorerConnectionLabel, Target = _explorer, Padding = new Thickness(0, 6, 0, 4) });
            System.Windows.Automation.AutomationProperties.SetName(_explorer, MigrationText.ExplorerConnectionLabel.TrimEnd(' ', ':'));
            _secretPanel.Children.Add(_explorer);
            _copy.Content = MigrationText.CopyToSecrets;
            _copy.HorizontalAlignment = HorizontalAlignment.Left;
            _copy.Margin = new Thickness(0, 6, 0, 0);
            _copy.Click += (_, _) => Kubuno.VisualStudio.DataExplorer.DataUi.RunUi(CopyAsync, "Migrations/CopySecret");
            _secretPanel.Children.Add(_copy);
            root.Children.Add(_secretPanel);

            var statusRow = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            DockPanel.SetDock(_statusIcon, Dock.Left);
            statusRow.Children.Add(_statusIcon);
            statusRow.Children.Add(_status);
            System.Windows.Automation.AutomationProperties.SetName(_status, "Status");
            System.Windows.Automation.AutomationProperties.SetLiveSetting(_status, System.Windows.Automation.AutomationLiveSetting.Polite);
            root.Children.Add(statusRow);

            var ok = new Button { Content = MigrationText.OkButton, IsDefault = true };
            var cancel = new Button { Content = MigrationText.CancelButton, IsCancel = true };
            ok.Click += (_, _) => Accept();
            cancel.Click += (_, _) => DialogResult = false;
            root.Children.Add(ThemedControls.ButtonRow(ok, cancel));
            Content = root;
            _name.Loaded += (_, _) => _name.Focus();
            _name.KeyUp += (_, e) =>
            {
                if (e.Key != Key.Enter)
                {
                    ShowStatus(null, null);
                }
            };
        }

        /// <summary>The chosen name, trimmed.</summary>
        public string ConnectionName => (_name.Text ?? string.Empty).Trim();

        private void Accept()
        {
            if (!CrateDataInfo.IsValidConnectionName(ConnectionName))
            {
                ShowStatus(MigrationText.ConnectionNameInvalid, KnownMonikers.StatusError);
                _name.Focus();
                return;
            }

            DialogResult = true;
        }

        private void ToggleSecretPanel()
        {
            _secretPanel.Visibility = _secretPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            if (_secretPanel.Visibility != Visibility.Visible)
            {
                return;
            }

            var key = CrateDataInfo.SecretKey(CrateDataInfo.IsValidConnectionName(ConnectionName) ? ConnectionName : "<name>");
            _secretIntro.Text = MigrationText.SetConnectionStringIntro(key);
            if (_userSecretsId is null)
            {
                _explorer.IsEnabled = false;
                _copy.IsEnabled = false;
                ShowStatus(MigrationText.NoUserSecretsId, KnownMonikers.StatusWarning);
                return;
            }

            if (!_explorerLoaded)
            {
                _explorerLoaded = true;
                Kubuno.VisualStudio.DataExplorer.DataUi.RunUi(LoadExplorerAsync, "Migrations/ExplorerList");
            }
        }

        private async Task LoadExplorerAsync()
        {
            if (_listExplorer is null)
            {
                return;
            }

            IReadOnlyList<ExplorerConnectionInfo> connections;
            try
            {
                connections = await _listExplorer(CancellationToken.None);
            }
            catch (Exception exception) when (exception is DataToolException or OperationCanceledException)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                ShowStatus(exception.Message, KnownMonikers.StatusError);
                return;
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            _explorer.Items.Clear();
            foreach (var connection in connections)
            {
                // The display text is redacted by the helper (never a password).
                _explorer.Items.Add(new ComboBoxItem { Content = $"{connection.Name} ({connection.Display})", Tag = connection.Name });
            }

            if (connections.Count == 0)
            {
                _copy.IsEnabled = false;
                ShowStatus(MigrationText.NoExplorerConnections, KnownMonikers.StatusInformation);
            }
            else
            {
                var match = _explorer.Items.OfType<ComboBoxItem>().FirstOrDefault(i => string.Equals((string)i.Tag, ConnectionName, StringComparison.OrdinalIgnoreCase));
                _explorer.SelectedItem = match ?? _explorer.Items[0];
            }
        }

        private async Task CopyAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (!CrateDataInfo.IsValidConnectionName(ConnectionName))
            {
                ShowStatus(MigrationText.ConnectionNameInvalid, KnownMonikers.StatusError);
                return;
            }

            if ((_explorer.SelectedItem as ComboBoxItem)?.Tag is not string explorer || _copyToSecrets is null)
            {
                return;
            }

            var name = ConnectionName;
            _copy.IsEnabled = false;
            try
            {
                await _copyToSecrets(explorer, name, CancellationToken.None);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                ShowStatus(MigrationText.SecretCopied(CrateDataInfo.SecretKey(name)), KnownMonikers.StatusOK);
            }
            catch (Exception exception) when (exception is DataToolException or OperationCanceledException)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                ShowStatus(exception.Message, KnownMonikers.StatusError);
            }
            finally
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                _copy.IsEnabled = true;
            }
        }

        private void ShowStatus(string? text, ImageMoniker? icon)
        {
            _status.Text = text ?? string.Empty;
            if (icon is { } moniker && text != null)
            {
                _statusIcon.Moniker = moniker;
                _statusIcon.Visibility = Visibility.Visible;
            }
            else
            {
                _statusIcon.Visibility = Visibility.Collapsed;
            }
        }
    }
}
