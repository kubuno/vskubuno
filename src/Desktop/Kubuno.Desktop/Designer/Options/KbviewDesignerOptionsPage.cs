using System.ComponentModel;
using System.Runtime.InteropServices;
using Kubuno.Core.Settings;
using Microsoft.VisualStudio.Shell;

namespace Kubuno.Desktop.Designer.Options
{
    /// <summary>
    /// Tools &gt; Options &gt; Kubuno &gt; Designer. Mirrors the shape of
    /// <c>Kubuno.Desktop.Views.Options.KbviewOptionsPage</c>: this type lives in this library (not
    /// in the VSIX) so it ships with the rest of the designer support, but a <c>DialogPage</c> only
    /// becomes a real Tools &gt; Options page once a VSIX package class declares it via
    /// <c>[ProvideOptionPage]</c>/<c>[ProvideProfile]</c> - see INTEGRATION.md, "Options page
    /// registration", for the exact attributes the VSIX's <c>KubunoPackage</c> must add, and how its
    /// <c>InitializeAsync</c> should publish this page into <see cref="DesignerOptionsHost.Current"/>.
    /// </summary>
    // The identity Visual Studio stored this page under before the layers (docs/ARCHITECTURE.md, "Layers (as built)"):
    // the page GUID of the pkgdef and profile (formerly derived from the type's full name) and the classic settings
    // key (DialogPage's default is derived from the full name too), kept so existing settings and exports still apply.
    [Guid("42ec8286-59ea-3edd-884c-c3fddadb25b3")]
    public sealed class KbviewDesignerOptionsPage : KubunoDialogPage, IDesignerOptions
    {
        public KbviewDesignerOptionsPage()
            : base(legacyTypeFullName: "Kubuno.VisualStudio.Designer.Options.KbviewDesignerOptionsPage")
        {
        }

        [Category("Kubuno View Designer")]
        [DisplayName("Use as default editor")]
        [Description("When enabled, the Kubuno View Designer (Design | XML split view) becomes the " +
            "editor a double-click opens for .kbview files, instead of the plain XML/text editor. " +
            "Off by default: the design surface is a placeholder until DSG-6/DSG-7 land a real, " +
            "embedded rendering surface. Either way, the designer is always reachable via " +
            "Open With... > " + DesignerConstants.EditorName + ".")]
        [DefaultValue(false)]
        [UnifiedSetting("kubuno.designer.editor.useAsDefault")]
        public bool UseDesignerAsDefaultEditor { get; set; } = false;

        [Category("Kubuno View Designer")]
        [DisplayName("Show design outlines")]
        [Description("Draws a faint dashed outline around the containers that are otherwise invisible " +
            "(a Panel or Stack with no surface, border or background), so they can be found and dropped " +
            "into, like the dotted border Windows Forms shows around a borderless Panel. Controls always " +
            "look exactly as at run time. Applies to the designers opened afterwards.")]
        [DefaultValue(true)]
        [UnifiedSetting("kubuno.designer.surface.showDesignOutlines")]
        public bool ShowDesignOutlines { get; set; } = true;
    }
}
