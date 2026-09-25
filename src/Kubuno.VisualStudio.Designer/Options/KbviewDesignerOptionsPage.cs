using System.ComponentModel;
using Microsoft.VisualStudio.Shell;

namespace Kubuno.VisualStudio.Designer.Options
{
    /// <summary>
    /// Tools &gt; Options &gt; Kubuno &gt; Designer. Mirrors the shape of
    /// <c>Kubuno.VisualStudio.Views.Options.KbviewOptionsPage</c>: this type lives in this library (not
    /// in the VSIX) so it ships with the rest of the designer support, but a <c>DialogPage</c> only
    /// becomes a real Tools &gt; Options page once a VSIX package class declares it via
    /// <c>[ProvideOptionPage]</c>/<c>[ProvideProfile]</c> - see INTEGRATION.md, "Options page
    /// registration", for the exact attributes the VSIX's <c>KubunoPackage</c> must add, and how its
    /// <c>InitializeAsync</c> should publish this page into <see cref="DesignerOptionsHost.Current"/>.
    /// </summary>
    public sealed class KbviewDesignerOptionsPage : DialogPage, IDesignerOptions
    {
        [Category("Kubuno View Designer")]
        [DisplayName("Use as default editor")]
        [Description("When enabled, the Kubuno View Designer (Design | XML split view) becomes the " +
            "editor a double-click opens for .kbview files, instead of the plain XML/text editor. " +
            "Off by default: the design surface is a placeholder until DSG-6/DSG-7 land a real, " +
            "embedded rendering surface. Either way, the designer is always reachable via " +
            "Open With... > " + DesignerConstants.EditorName + ".")]
        public bool UseDesignerAsDefaultEditor { get; set; } = false;
    }
}
