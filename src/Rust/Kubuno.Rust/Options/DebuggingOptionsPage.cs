using System.ComponentModel;
using System.Runtime.InteropServices;
using Kubuno.Shared.Logging;
using Kubuno.Rust.ProjectSystem;
using Kubuno.Shared.Settings;
using Microsoft.VisualStudio.Shell;

namespace Kubuno.Rust.Options
{
    /// <summary>
    /// Tools &gt; Options &gt; Kubuno &gt; Debugging (docs/DEBUGGING.md). The value lives in the user settings store
    /// (<see cref="RustDebuggerSettings"/>), where the <c>.rsproj</c> and Open Folder launch providers read it at F5.
    /// </summary>
    // The identity Visual Studio stored this page under before the layers (docs/ARCHITECTURE.md, "Layers (as built)"):
    // the page GUID of the pkgdef and profile (formerly derived from the type's full name) and the classic settings
    // key (DialogPage's default is derived from the full name too), kept so existing settings and exports still apply.
    [Guid("f8f76810-3c61-3c8b-b61c-9caecf9760c8")]
    public sealed class DebuggingOptionsPage : KubunoDialogPage
    {
        public DebuggingOptionsPage()
            : base(legacyTypeFullName: "Kubuno.VisualStudio.Options.DebuggingOptionsPage")
        {
        }

        [Category("Just My Code")]
        [DisplayName("Treat the Kubuno framework as external code")]
        [Description("Like Windows Forms for a C# application: with Just My Code on, the Call Stack collapses the Kubuno " +
            "framework (kubuno_views, kubuno_controls, kubuno_ui, the event-handler dispatch glue) into [External Code] " +
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
