using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.Win32;

namespace Kubuno.VisualStudio.Views.Options
{
    /// <summary>
    /// An options page whose values live in Visual Studio 2026's unified settings (Tools &gt; Options &gt; Kubuno, see
    /// <c>UnifiedSettings/kubuno.registration.json</c>). The page object stays the code's view of the options; it
    /// (1) loads its classic values and, once, copies the ones the user had changed into the unified store,
    /// (2) reads the unified store, (3) follows the store's changes live. Without unified settings (Visual Studio 2022,
    /// classic mode) it behaves as a plain <see cref="DialogPage"/>.
    /// </summary>
    public abstract class KubunoDialogPage : DialogPage
    {
        private const string MigrationKey = @"Kubuno\UnifiedSettingsMigrated";
        private IDisposable? _subscription;

        /// <summary>Loads the classic storage (the registry, unless the page keeps its values elsewhere).</summary>
        protected virtual void LoadLegacySettings() => base.LoadSettingsFromStorage();

        /// <summary>Saves the classic storage.</summary>
        protected virtual void SaveLegacySettings() => base.SaveSettingsToStorage();

        /// <summary>Called after the settings changed in the unified store (on the UI thread), or after they were first loaded.</summary>
        protected virtual void OnSettingsChanged()
        {
        }

        public sealed override void LoadSettingsFromStorage()
        {
            LoadLegacySettings();
            if (KubunoUnifiedSettings.GetManager() is null)
            {
                OnSettingsChanged();
                return;
            }

            if (!IsMigrated())
            {
                KubunoUnifiedSettings.Store(this, onlyDifferentFromDefault: true);
                MarkMigrated();
            }

            KubunoUnifiedSettings.TryLoad(this);
            OnSettingsChanged();
            _subscription ??= KubunoUnifiedSettings.Subscribe(this, OnStoreChanged);
        }

        /// <summary>The store changed (settings page, JSON file, another window): reload on the UI thread and tell the page.</summary>
        private void OnStoreChanged()
        {
#pragma warning disable VSSDK007 // fire-and-forget from a change notification; FileAndForget reports faults to the log.
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (KubunoUnifiedSettings.TryLoad(this))
                {
                    OnSettingsChanged();
                }
            }).FileAndForget("kubuno/options/unifiedSettingsChanged");
#pragma warning restore VSSDK007
        }

        public sealed override void SaveSettingsToStorage()
        {
            SaveLegacySettings();
            if (KubunoUnifiedSettings.GetManager() is not null)
            {
                KubunoUnifiedSettings.Store(this, onlyDifferentFromDefault: false);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _subscription?.Dispose();
                _subscription = null;
            }

            base.Dispose(disposing);
        }

        private static RegistryKey? OpenRoot() =>
            VSRegistry.RegistryRoot(ServiceProvider.GlobalProvider, Microsoft.VisualStudio.Shell.Interop.__VsLocalRegistryType.RegType_UserSettings, writable: true);

        private bool IsMigrated()
        {
            try
            {
                using var key = OpenRoot()?.OpenSubKey(MigrationKey);
                return key?.GetValue(GetType().Name) is not null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void MarkMigrated()
        {
            try
            {
                using var key = OpenRoot()?.CreateSubKey(MigrationKey);
                key?.SetValue(GetType().Name, 1, RegistryValueKind.DWord);
            }
            catch (Exception)
            {
                // Best effort: without the mark the next start copies the classic values again (they only fill in defaults).
            }
        }
    }
}
