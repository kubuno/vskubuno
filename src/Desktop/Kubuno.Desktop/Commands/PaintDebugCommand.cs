using System;
using System.ComponentModel.Design;
using System.Globalization;
using Kubuno.Desktop.Logic.Painting;
using Kubuno.Shared.Logging;
using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Settings;

namespace Kubuno.Desktop.Commands
{
    /// <summary>
    /// Debug &gt; Kubuno &gt; "Paint debug" ("Débogage du rendu" in a French Visual Studio), docs/EVENTS.md EVT-8:
    /// a checkable command toggling the Kubuno runtime's paint-debug overlay. Toggling persists the setting
    /// (user settings store), mirrors it into this process's <c>KUBUNO_PAINT_DEBUG</c> environment variable
    /// (inherited by F5 / Ctrl+F5 debuggees and by newly started design surfaces, unless the user set their own
    /// value first) and posts the "Kubuno.PaintDebug" window message to every running <c>KubunoControlsHost</c>
    /// window. The pure logic lives in <see cref="PaintDebug"/>, the Win32 part in <see cref="PaintDebugBroadcaster"/>.
    /// </summary>
    internal static class PaintDebugCommand
    {
        private const string Collection = @"Kubuno\PaintDebug";
        private const string EnabledProperty = "Enabled";

        private static bool _enabled;

        public static void Initialize(OleMenuCommandService commandService)
        {
            _enabled = LoadEnabled();
            PaintDebugBroadcaster.ApplyToProcessEnvironment(PaintDebug.FlagsFor(_enabled));

            var id = new CommandID(PackageGuids.KubunoCommandSet, PackageIds.PaintDebugCommand);
#pragma warning disable VSTHRD010 // OleMenuCommand invoke/query events fire on the UI thread.
            var command = new OleMenuCommand((sender, args) => Toggle(), id);
            command.BeforeQueryStatus += (sender, args) =>
            {
                var menuCommand = (OleMenuCommand)sender!;
                menuCommand.Checked = _enabled;
                menuCommand.Text = IsFrench ? "Débogage du rendu" : "Paint debug";
            };
#pragma warning restore VSTHRD010
            commandService.AddCommand(command);
        }

        private static bool IsFrench => string.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "fr", StringComparison.OrdinalIgnoreCase);

        private static void Toggle()
        {
            _enabled = !_enabled;
            SaveEnabled(_enabled);

            var flags = PaintDebug.FlagsFor(_enabled);
            PaintDebugBroadcaster.ApplyToProcessEnvironment(flags);
            var reached = PaintDebugBroadcaster.Broadcast(flags);
            KubunoLog.WriteLine($"Paint debug {(_enabled ? "on" : "off")}: message posted to {reached} Kubuno host window(s).");
        }

        private static bool LoadEnabled()
        {
            try
            {
                var store = new ShellSettingsManager(ServiceProvider.GlobalProvider).GetReadOnlySettingsStore(SettingsScope.UserSettings);
                return store.CollectionExists(Collection) && store.PropertyExists(Collection, EnabledProperty)
                    && store.GetBoolean(Collection, EnabledProperty);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException)
            {
                return false;
            }
        }

        private static void SaveEnabled(bool enabled)
        {
            try
            {
                var store = new ShellSettingsManager(ServiceProvider.GlobalProvider).GetWritableSettingsStore(SettingsScope.UserSettings);
                if (!store.CollectionExists(Collection))
                {
                    store.CreateCollection(Collection);
                }

                store.SetBoolean(Collection, EnabledProperty, enabled);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException)
            {
                KubunoLog.WriteException("Saving the paint-debug setting failed", ex);
            }
        }
    }
}
