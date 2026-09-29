using System.ComponentModel;
using Kubuno.VisualStudio.Logging;
using Kubuno.VisualStudio.RustProjectSystem;
using Microsoft.VisualStudio.Shell;

namespace Kubuno.VisualStudio.Options
{
    /// <summary>
    /// Tools &gt; Options &gt; Kubuno &gt; Debugging (docs/DEBUGGING.md). The value lives in the user settings store
    /// (<see cref="RustDebuggerSettings"/>), where the <c>.rsproj</c> and Open Folder launch providers read it at F5.
    /// </summary>
    public sealed class DebuggingOptionsPage : DialogPage
    {
        [Category("Just My Code")]
        [DisplayName("Treat the Kubuno framework as external code")]
        [Description("Like Windows Forms for a C# application: with Just My Code on, the Call Stack collapses the Kubuno " +
            "framework (kubuno_views, kubuno_controls, kubuno_ui.dll, the event-handler dispatch glue) into [External Code] " +
            "and Step Into (F11) goes over it, straight to your handlers. Turn it off to debug Kubuno itself. The Rust " +
            "standard library is always stepped over. Takes effect at the next debug session.")]
        [DefaultValue(true)]
        public bool KubunoFrameworkIsExternalCode { get; set; } = true;

        public override void LoadSettingsFromStorage()
        {
            KubunoFrameworkIsExternalCode = RustDebuggerSettings.FrameworkIsExternalCode;
        }

        public override void SaveSettingsToStorage()
        {
            RustDebuggerSettings.FrameworkIsExternalCode = KubunoFrameworkIsExternalCode;
            RustDebuggerSettings.EnsureDebuggerFilesInstalled(KubunoLog.WriteLine);
        }
    }
}
