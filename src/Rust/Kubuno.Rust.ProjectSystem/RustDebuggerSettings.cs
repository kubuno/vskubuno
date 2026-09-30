using System;
using Kubuno.VisualStudio.Core;
using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Settings;

namespace Kubuno.VisualStudio.RustProjectSystem
{
    /// <summary>
    /// The Kubuno debugging settings (Tools &gt; Options &gt; Kubuno &gt; Rust, "Debugging" group), kept in Visual
    /// Studio's user settings store so the launch providers of both the <c>.rsproj</c> project system and Open
    /// Folder read the same value without loading the options page (docs/DEBUGGING.md).
    /// </summary>
    public static class RustDebuggerSettings
    {
        private const string Collection = @"Kubuno\Debugging";
        private const string FrameworkIsExternalCodeProperty = "FrameworkIsExternalCode";

        /// <summary>
        /// Treat the Kubuno framework (kubuno_views, kubuno_controls, kubuno_ui.dll...) as external code: Just My
        /// Code collapses it in the Call Stack and Step Into goes over it. On by default, like Windows Forms for a C#
        /// application; off for people who work on Kubuno itself.
        /// </summary>
        public static bool FrameworkIsExternalCode
        {
            get
            {
                try
                {
                    var store = new ShellSettingsManager(ServiceProvider.GlobalProvider).GetReadOnlySettingsStore(SettingsScope.UserSettings);
                    return store.GetBoolean(Collection, FrameworkIsExternalCodeProperty, true);
                }
                catch (Exception)
                {
                    return true;
                }
            }

            set
            {
                try
                {
                    var store = new ShellSettingsManager(ServiceProvider.GlobalProvider).GetWritableSettingsStore(SettingsScope.UserSettings);
                    if (!store.CollectionExists(Collection))
                    {
                        store.CreateCollection(Collection);
                    }

                    store.SetBoolean(Collection, FrameworkIsExternalCodeProperty, value);
                }
                catch (Exception)
                {
                    // Not persisted: the default applies next time.
                }
            }
        }

        /// <summary>The Exception Settings group Rust panics are listed in (not localized: DTE's group names are keys).</summary>
        public const string CppExceptionsGroup = "C++ Exceptions";

        /// <summary>
        /// The Exception Settings entry of Rust panics: <c>rust_panic</c> as Visual Studio undecorates it (see
        /// Kubuno.VisualStudio.Debugger's <c>RustPanicPayload.VisualStudioExceptionName</c> and debugging.pkgdef).
        /// </summary>
        public const string RustPanicExceptionName = " ?? ::st_panic";

        /// <summary>
        /// Makes sure the debugger engine receives the Rust panic entry of Exception Settings. debugging.pkgdef
        /// registers it checked ("Break When Thrown"), and the Exception Settings window shows it so, but a
        /// registry default that was never touched was found live not to reach the native engine (the panic did
        /// not stop the debugger until the entry was set once through <c>Debugger3.ExceptionGroups</c>). Re-applying
        /// the entry's own current state - never changing it - before each launch fixes that, and adds the entry
        /// when a profile has lost it. Must run on the UI thread; never throws.
        /// </summary>
        public static void EnsurePanicExceptionSetting(Action<string>? log = null)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                if (Package.GetGlobalService(typeof(EnvDTE.DTE)) is not EnvDTE80.DTE2 dte
                    || dte.Debugger is not EnvDTE90.Debugger3 debugger)
                {
                    return;
                }

                var group = debugger.ExceptionGroups.Item(CppExceptionsGroup);
                EnvDTE90.ExceptionSetting? setting = null;
                foreach (EnvDTE90.ExceptionSetting candidate in group)
                {
                    if (candidate.Name == RustPanicExceptionName)
                    {
                        setting = candidate;
                        break;
                    }
                }

                if (setting == null)
                {
                    setting = group.NewException(RustPanicExceptionName, 0);
                    group.SetBreakWhenThrown(true, setting);
                    return;
                }

                group.SetBreakWhenThrown(setting.BreakWhenThrown, setting);
            }
            catch (Exception exception)
            {
                log?.Invoke($"Kubuno: could not apply the Rust panic exception setting: {exception.Message}");
            }
        }

        /// <summary>
        /// Installs the Just My Code and step-filter files for the current setting (<see cref="RustDebuggerFiles"/>).
        /// Never throws.
        /// </summary>
        public static void EnsureDebuggerFilesInstalled(Action<string>? log = null)
        {
            try
            {
                RustDebuggerFiles.EnsureInstalled(FrameworkIsExternalCode, log);
            }
            catch (Exception exception)
            {
                log?.Invoke($"Kubuno: failed to install the Rust debugger files: {exception.Message}");
            }
        }
    }
}
