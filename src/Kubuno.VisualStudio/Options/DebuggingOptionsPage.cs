using System.ComponentModel;
using Kubuno.VisualStudio.Logging;
using Kubuno.VisualStudio.RustProjectSystem;
using Kubuno.VisualStudio.Views.Options;
using Microsoft.VisualStudio.Shell;

namespace Kubuno.VisualStudio.Options
{
    /// <summary>
    /// Tools &gt; Options &gt; Kubuno &gt; Debugging (docs/DEBUGGING.md). The value lives in the user settings store
    /// (<see cref="RustDebuggerSettings"/>), where the <c>.rsproj</c> and Open Folder launch providers read it at F5.
    /// </summary>
    public sealed class DebuggingOptionsPage : KubunoDialogPage
    {
        [Category("Just My Code")]
        [DisplayName("Treat the Kubuno framework as external code")]
        [Description("Like Windows Forms for a C# application: with Just My Code on, the Call Stack collapses the Kubuno " +
            "framework (kubuno_views, kubuno_controls, kubuno_ui.dll, the event-handler dispatch glue) into [External Code] " +
            "and Step Into (F11) goes over it, straight to your handlers. Turn it off to debug Kubuno itself. The Rust " +
            "standard library is always stepped over. Takes effect at the next debug session.")]
        [DefaultValue(true)]
        [UnifiedSetting("kubuno.debugging.justMyCode.frameworkIsExternalCode")]
        public bool KubunoFrameworkIsExternalCode { get; set; } = true;

        // The debugger side reads RustDebuggerSettings, not the options store: every load or change of the unified
        // setting is mirrored there (and the visualizer/step-filter files regenerated) - "takes effect at the next session".
        protected override void LoadLegacySettings()
        {
            KubunoFrameworkIsExternalCode = RustDebuggerSettings.FrameworkIsExternalCode;
        }

        protected override void SaveLegacySettings()
        {
            RustDebuggerSettings.FrameworkIsExternalCode = KubunoFrameworkIsExternalCode;
            RustDebuggerSettings.EnsureDebuggerFilesInstalled(KubunoLog.WriteLine);
        }

        protected override void OnSettingsChanged()
        {
            if (KubunoFrameworkIsExternalCode != RustDebuggerSettings.FrameworkIsExternalCode)
            {
                SaveLegacySettings();
            }
        }
    }
}
