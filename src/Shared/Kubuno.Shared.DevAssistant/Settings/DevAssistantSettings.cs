using System;
using System.Globalization;
using Kubuno.Shared.DevAssistant.Logic.Protocol;
using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Settings;

namespace Kubuno.Shared.DevAssistant.Settings
{
    /// <summary>
    /// The assistant's per-user settings (docs/AI-ASSISTANT.md sections 4 and 7.3), kept in Visual Studio's user settings
    /// store (collection <c>Kubuno\DevAssistant</c>). The API key is NOT here: it lives in the Windows Credential
    /// Manager (<see cref="ProviderIds.CredentialTarget"/>), written by <see cref="DevAssistantSettingsDialog"/>.
    /// </summary>
    public sealed class DevAssistantSettings
    {
        private const string Collection = @"Kubuno\DevAssistant";

        public string Provider { get; set; } = ProviderIds.Anthropic;

        /// <summary>Default model: Claude Opus 5.5 (Q3), Sonnet 5.5 one click away in the picker.</summary>
        public string Model { get; set; } = "claude-opus-5-5";

        public string Effort { get; set; } = "medium";

        /// <summary>Hard cost cap per session, in US dollars (Q3: 5 $).</summary>
        public decimal CostCapUsd { get; set; } = 5m;

        /// <summary>Maximum model calls in one answer.</summary>
        public int MaxRounds { get; set; } = 12;

        /// <summary>Enter sends (Shift+Enter for a new line); false: Ctrl+Enter sends.</summary>
        public bool SendWithEnter { get; set; } = true;

        /// <summary>Whether the developer accepted the data notice of the Anthropic provider (section 8.2).</summary>
        public bool AnthropicConsent { get; set; }

        public static DevAssistantSettings Load()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var settings = new DevAssistantSettings();
            var store = Store(writable: false);
            if (store is null || !store.CollectionExists(Collection))
            {
                return settings;
            }

            settings.Provider = store.GetString(Collection, nameof(Provider), settings.Provider);
            settings.Model = store.GetString(Collection, nameof(Model), settings.Model);
            settings.Effort = store.GetString(Collection, nameof(Effort), settings.Effort);
            if (decimal.TryParse(store.GetString(Collection, nameof(CostCapUsd), settings.CostCapUsd.ToString(CultureInfo.InvariantCulture)), NumberStyles.Number, CultureInfo.InvariantCulture, out var cap) && cap > 0)
            {
                settings.CostCapUsd = cap;
            }

            settings.MaxRounds = Math.Max(1, store.GetInt32(Collection, nameof(MaxRounds), settings.MaxRounds));
            settings.SendWithEnter = store.GetBoolean(Collection, nameof(SendWithEnter), settings.SendWithEnter);
            settings.AnthropicConsent = store.GetBoolean(Collection, nameof(AnthropicConsent), settings.AnthropicConsent);
            return settings;
        }

        public void Save()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (Store(writable: true) is not WritableSettingsStore store)
            {
                return;
            }

            if (!store.CollectionExists(Collection))
            {
                store.CreateCollection(Collection);
            }

            store.SetString(Collection, nameof(Provider), Provider);
            store.SetString(Collection, nameof(Model), Model);
            store.SetString(Collection, nameof(Effort), Effort);
            store.SetString(Collection, nameof(CostCapUsd), CostCapUsd.ToString(CultureInfo.InvariantCulture));
            store.SetInt32(Collection, nameof(MaxRounds), MaxRounds);
            store.SetBoolean(Collection, nameof(SendWithEnter), SendWithEnter);
            store.SetBoolean(Collection, nameof(AnthropicConsent), AnthropicConsent);
        }

        private static SettingsStore? Store(bool writable)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var manager = new ShellSettingsManager(ServiceProvider.GlobalProvider);
            return writable ? manager.GetWritableSettingsStore(SettingsScope.UserSettings) : manager.GetReadOnlySettingsStore(SettingsScope.UserSettings);
        }
    }
}
