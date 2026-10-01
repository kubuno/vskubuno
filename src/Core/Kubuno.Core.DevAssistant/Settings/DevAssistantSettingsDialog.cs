using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Kubuno.Core.DevAssistant.Logic.Credentials;
using Kubuno.Core.DevAssistant.Logic.Protocol;
using Kubuno.Core.DevAssistant.UI;
using Kubuno.Core.UI;
using Microsoft.VisualStudio.PlatformUI;

namespace Kubuno.Core.DevAssistant.Settings
{
    /// <summary>
    /// « Paramètres de l'assistant » (docs/AI-ASSISTANT.md section 8.1): provider, default model, effort, cost cap, and the
    /// API key. « Enregistrer la clé » writes the key to the Windows Credential Manager
    /// (<c>Kubuno:DevAssistant:Provider:anthropic</c>, CRED_PERSIST_LOCAL_MACHINE) and clears the box; the dialog only ever
    /// shows whether a key is stored, never the key itself.
    /// </summary>
    public sealed class DevAssistantSettingsDialog : ThemedDialog
    {
        private readonly DevAssistantSettings _settings;
        private readonly Func<string, bool> _hasKey;
        private readonly Action<string, string> _saveKey;
        private readonly Func<string, bool> _deleteKey;
        private readonly ComboBox _provider = new ComboBox { MinWidth = 320 };
        private readonly TextBlock _keyStatus = new TextBlock { Margin = new Thickness(0, 0, 0, 6) };
        private readonly PasswordBox _key = new PasswordBox { MinWidth = 320, Padding = new Thickness(2) };
        private readonly ComboBox _model = new ComboBox { IsEditable = true, MinWidth = 320 };
        private readonly ComboBox _effort = new ComboBox { MinWidth = 160 };
        private readonly TextBox _cap = new TextBox { Width = 80, HorizontalAlignment = HorizontalAlignment.Left };
        private readonly CheckBox _enterSends = new CheckBox();

        /// <summary>The dialog over the real Credential Manager.</summary>
        public DevAssistantSettingsDialog(DevAssistantSettings settings)
            : this(
                settings,
                provider => WindowsCredentialStore.Exists(ProviderIds.CredentialTarget(provider)),
                (provider, key) => WindowsCredentialStore.Write(ProviderIds.CredentialTarget(provider), "Kubuno Dev Assistant", key),
                provider => WindowsCredentialStore.Delete(ProviderIds.CredentialTarget(provider)))
        {
        }

        /// <summary>The dialog over a fake key store (Dialog Gallery).</summary>
        public DevAssistantSettingsDialog(DevAssistantSettings settings, Func<string, bool> hasKey, Action<string, string> saveKey, Func<string, bool> deleteKey)
        {
            _settings = settings;
            _hasKey = hasKey;
            _saveKey = saveKey;
            _deleteKey = deleteKey;
            Title = AssistantText.SettingsTitle;
            Width = 560;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            AutomationProperties.SetAutomationId(this, "KubunoDevAssistantSettings");

            _provider.Items.Add(new ComboBoxItem { Content = AssistantText.S("Anthropic Claude (cloud : api.anthropic.com)", "Anthropic Claude (cloud: api.anthropic.com)"), Tag = ProviderIds.Anthropic });
            _provider.Items.Add(new ComboBoxItem { Content = AssistantText.S("Test hors ligne (réponses enregistrées, rien ne quitte la machine)", "Offline test (recorded responses, nothing leaves the machine)"), Tag = ProviderIds.Fake });
            _provider.SelectedIndex = settings.Provider == ProviderIds.Fake ? 1 : 0;
            AutomationProperties.SetAutomationId(_provider, "provider");

            foreach (var model in new[] { "claude-opus-5-5", "claude-sonnet-5-5", "claude-haiku-4-5" })
            {
                _model.Items.Add(model);
            }

            _model.Text = settings.Model;
            AutomationProperties.SetAutomationId(_model, "model");
            foreach (var effort in new[] { "low", "medium", "high" })
            {
                _effort.Items.Add(new ComboBoxItem { Content = AssistantText.Effort(effort), Tag = effort });
            }

            _effort.SelectedIndex = settings.Effort == "low" ? 0 : settings.Effort == "high" ? 2 : 1;
            _cap.Text = settings.CostCapUsd.ToString("0.##", CultureInfo.InvariantCulture);
            AutomationProperties.SetAutomationId(_cap, "costCap");
            _enterSends.Content = AssistantText.S("Entrée envoie le message (Maj+Entrée : nouvelle ligne)", "Enter sends the message (Shift+Enter: new line)");
            _enterSends.IsChecked = settings.SendWithEnter;

            _key.SetResourceReference(Control.BackgroundProperty, CommonControlsColors.TextBoxBackgroundBrushKey);
            _key.SetResourceReference(Control.ForegroundProperty, CommonControlsColors.TextBoxTextBrushKey);
            _key.SetResourceReference(Control.BorderBrushProperty, CommonControlsColors.TextBoxBorderBrushKey);
            _key.SetResourceReference(PasswordBox.CaretBrushProperty, CommonControlsColors.TextBoxTextBrushKey);
            AutomationProperties.SetAutomationId(_key, "apiKey");
            AutomationProperties.SetName(_key, AssistantText.S("Clé API Anthropic", "Anthropic API key"));

            var saveKeyButton = new Button { Content = AssistantText.S("Enregistrer la clé", "Save the key"), Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 6, 7, 0) };
            AutomationProperties.SetAutomationId(saveKeyButton, "saveKey");
            saveKeyButton.Click += (_, _) => SaveKey();
            var deleteKeyButton = new Button { Content = AssistantText.S("Supprimer la clé", "Delete the key"), Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 6, 0, 0) };
            AutomationProperties.SetAutomationId(deleteKeyButton, "deleteKey");
            deleteKeyButton.Click += (_, _) => DeleteKey();

            var panel = new StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(Label(AssistantText.S("Fournisseur", "Provider")));
            panel.Children.Add(_provider);

            panel.Children.Add(Label(AssistantText.S("Clé API Anthropic", "Anthropic API key")));
            panel.Children.Add(_keyStatus);
            panel.Children.Add(_key);
            var keyButtons = new StackPanel { Orientation = Orientation.Horizontal };
            keyButtons.Children.Add(saveKeyButton);
            keyButtons.Children.Add(deleteKeyButton);
            panel.Children.Add(keyButtons);
            var keyNote = ThemedControls.SecondaryText(AssistantText.S(
                "La clé est stockée dans le Gestionnaire d'identification Windows (" + ProviderIds.CredentialTarget(ProviderIds.Anthropic) + ", non itinérante) et n'est jamais réaffichée. Seul le processus kubuno-dev-assistant.exe la lit ; elle ne passe ni par la ligne de commande, ni par les journaux, ni par une variable d'environnement.",
                "The key is stored in the Windows Credential Manager (" + ProviderIds.CredentialTarget(ProviderIds.Anthropic) + ", not roaming) and is never shown again. Only kubuno-dev-assistant.exe reads it; it never goes through a command line, a log or an environment variable."));
            keyNote.Margin = new Thickness(0, 6, 0, 0);
            panel.Children.Add(keyNote);

            panel.Children.Add(Label(AssistantText.S("Modèle par défaut", "Default model")));
            panel.Children.Add(_model);
            panel.Children.Add(Label(AssistantText.S("Effort par défaut", "Default effort")));
            panel.Children.Add(_effort);
            panel.Children.Add(Label(AssistantText.S("Plafond de coût par session (USD, estimation)", "Cost cap per session (USD, estimate)")));
            panel.Children.Add(_cap);
            _enterSends.Margin = new Thickness(0, 12, 0, 0);
            panel.Children.Add(_enterSends);
            panel.Children.Add(ThemedControls.SecondaryText(AssistantText.S(
                "Aucune télémétrie : l'extension n'envoie rien ailleurs qu'au fournisseur choisi.",
                "No telemetry: the extension sends nothing anywhere but to the chosen provider.")));

            var ok = new Button { Content = "OK", IsDefault = true };
            AutomationProperties.SetAutomationId(ok, "ok");
            ok.Click += (_, _) =>
            {
                if (Apply())
                {
                    DialogResult = true;
                }
            };
            var cancel = new Button { Content = AssistantText.S("Annuler", "Cancel"), IsCancel = true };
            AutomationProperties.SetAutomationId(cancel, "cancel");
            panel.Children.Add(ThemedControls.ButtonRow(ok, cancel));
            Content = panel;
            RefreshKeyStatus();
        }

        private string SelectedProvider => (_provider.SelectedItem as ComboBoxItem)?.Tag as string ?? ProviderIds.Anthropic;

        private static TextBlock Label(string text) => new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) };

        private void RefreshKeyStatus()
        {
            bool stored;
            try
            {
                stored = _hasKey(ProviderIds.Anthropic);
            }
            catch (Win32Exception)
            {
                stored = false;
            }

            _keyStatus.Text = stored ? AssistantText.S("● Clé enregistrée", "● Key stored") : AssistantText.S("○ Aucune clé enregistrée", "○ No key stored");
            AutomationProperties.SetName(_keyStatus, _keyStatus.Text);
            AutomationProperties.SetAutomationId(_keyStatus, "keyStatus");
        }

        private void SaveKey()
        {
            var key = _key.Password.Trim();
            if (key.Length == 0)
            {
                return;
            }

            try
            {
                _saveKey(ProviderIds.Anthropic, key);
            }
            catch (Win32Exception exception)
            {
                _keyStatus.Text = AssistantText.S("Échec de l'enregistrement : ", "Could not save: ") + exception.Message;
                return;
            }
            finally
            {
                _key.Clear();
            }

            RefreshKeyStatus();
        }

        private void DeleteKey()
        {
            try
            {
                _deleteKey(ProviderIds.Anthropic);
            }
            catch (Win32Exception exception)
            {
                _keyStatus.Text = exception.Message;
                return;
            }

            RefreshKeyStatus();
        }

        private bool Apply()
        {
            if (!decimal.TryParse(_cap.Text.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var cap) || cap <= 0 || cap > 1000)
            {
                _cap.Focus();
                return false;
            }

            if (_key.Password.Trim().Length > 0)
            {
                SaveKey();
            }

            _settings.Provider = SelectedProvider;
            _settings.Model = string.IsNullOrWhiteSpace(_model.Text) ? "claude-opus-5-5" : _model.Text.Trim();
            _settings.Effort = (_effort.SelectedItem as ComboBoxItem)?.Tag as string ?? "medium";
            _settings.CostCapUsd = cap;
            _settings.SendWithEnter = _enterSends.IsChecked == true;
            return true;
        }
    }
}
