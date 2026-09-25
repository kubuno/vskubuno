using System.ComponentModel;
using System.Windows.Forms.Design;
using Microsoft.VisualStudio.Shell;

namespace Kubuno.VisualStudio.Views.Options
{
    /// <summary>
    /// Tools &gt; Options &gt; Kubuno &gt; Views. Mirrors the shape of the sibling VSIX project's
    /// <c>Kubuno.VisualStudio.Options.RustOptionsPage</c> for the Rust language server.
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
    public sealed class KbviewOptionsPage : DialogPage, IKubunoViewsOptions
    {
        [Category("kubuno-views-ls")]
        [DisplayName("Path override")]
        [Description("Full path to kubuno-views-ls.exe. When empty, Kubuno tries the extension's own " +
            "'tools' folder, then the PATH environment variable, then the local dev build output folders " +
            "(C:\\kubuno-build\\desktop-target\\debug\\ and C:\\kubuno-build\\agent-views-ls\\debug\\).")]
        [Editor(typeof(FileNameEditor), typeof(System.Drawing.Design.UITypeEditor))]
        public string LanguageServerPathOverride { get; set; } = string.Empty;
    }
}
