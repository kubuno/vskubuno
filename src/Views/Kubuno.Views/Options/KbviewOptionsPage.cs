using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms.Design;
using Microsoft.VisualStudio.Shell;
using Kubuno.Shared.Settings;

namespace Kubuno.Views.Options
{
    /// <summary>
    /// Tools &gt; Options &gt; Kubuno &gt; Views. Mirrors the shape of the sibling VSIX project's
    /// <c>Kubuno.Rust.Options.RustOptionsPage</c> for the Rust language server.
    ///
    /// This type lives in this library (not in the VSIX) so it ships with the rest of the views
    /// support, but a <c>DialogPage</c> only becomes a real Tools &gt; Options page once a VSIX
    /// package class declares it via <c>[ProvideOptionPage]</c>/<c>[ProvideProfile]</c> - see
    /// INTEGRATION.md, "Options page registration", for the exact attributes the VSIX's
    /// <c>KubunoPackage</c> must add, and how its <c>InitializeAsync</c> should publish this page's
    /// values into <see cref="KubunoViewsOptionsHost.Current"/> so
    /// <see cref="LanguageService.KubunoViewsLanguageClient"/> (MEF-constructed, independent of the
    /// package) can read them.
    /// </summary>
    // The identity Visual Studio stored this page under before the layers (docs/ARCHITECTURE.md, "Layers (as built)"):
    // the page GUID of the pkgdef and profile (formerly derived from the type's full name) and the classic settings
    // key (DialogPage's default is derived from the full name too), kept so existing settings and exports still apply.
    [Guid("fd2f7bb5-05d7-39f9-beb1-d403ed82a121")]
    public sealed class KbviewOptionsPage : KubunoDialogPage, IKubunoViewsOptions
    {
        public KbviewOptionsPage()
            : base(legacyTypeFullName: "Kubuno.VisualStudio.Views.Options.KbviewOptionsPage")
        {
        }

        [Category("kubuno-views-ls")]
        [DisplayName("Path override")]
        [Description("Full path to kubuno-views-ls.exe. When empty, Kubuno tries the extension's own " +
            "'tools' folder, then the PATH environment variable, then the local dev build output folders " +
            "(C:\\kubuno-build\\desktop-target\\debug\\ and C:\\kubuno-build\\agent-views-ls\\debug\\).")]
        [Editor(typeof(FileNameEditor), typeof(System.Drawing.Design.UITypeEditor))]
        [UnifiedSetting("kubuno.views.languageServer.pathOverride")]
        public string LanguageServerPathOverride { get; set; } = string.Empty;
    }
}
