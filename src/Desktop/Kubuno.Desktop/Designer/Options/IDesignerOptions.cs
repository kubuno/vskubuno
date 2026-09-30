namespace Kubuno.Desktop.Designer.Options
{
    /// <summary>
    /// The options the designer's own registration logic reads. Implemented by
    /// <see cref="KbviewDesignerOptionsPage"/> (a <c>Microsoft.VisualStudio.Shell.DialogPage</c>), kept
    /// as a separate interface for the same reason
    /// <c>Kubuno.Desktop.Views.Options.IKubunoViewsOptions</c> is: so <see cref="DesignerOptionsHost"/>
    /// does not have to name the concrete <c>DialogPage</c> type at every call site.
    /// </summary>
    public interface IDesignerOptions
    {
        /// <summary>
        /// When <see langword="true"/>, the designer should be treated as the preferred editor for
        /// <c>.kbview</c> files instead of the plain XML/text editor. Defaults to
        /// <see langword="false"/> in <see cref="KbviewDesignerOptionsPage"/>: until the design surface
        /// is more than a placeholder (DSG-6/DSG-7), the plain editor stays the one a double-click
        /// opens. See INTEGRATION.md, "Open With / default editor registration", for exactly how (and
        /// how far) the VSIX can act on this flag today.
        /// </summary>
        bool UseDesignerAsDefaultEditor { get; }
    }
}
