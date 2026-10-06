namespace Kubuno.Views.Designer.Options
{
    /// <summary>
    /// The options the designer's own registration logic reads. Implemented by
    /// <see cref="KbviewDesignerOptionsPage"/> (a <c>Microsoft.VisualStudio.Shell.DialogPage</c>), kept
    /// as a separate interface for the same reason
    /// <c>Kubuno.Views.Options.IKubunoViewsOptions</c> is: so <see cref="DesignerOptionsHost"/>
    /// does not have to name the concrete <c>DialogPage</c> type at every call site.
    /// </summary>
    public interface IDesignerOptions
    {
        /// <summary>
        /// When <see langword="true"/> (the default), the design surface draws a faint dashed outline
        /// around the containers that are otherwise invisible (a <c>Panel</c> or <c>Stack</c> with no
        /// surface, border or background), like the dotted border Windows Forms shows around a
        /// borderless <c>Panel</c>. Controls themselves always look exactly as at run time.
        /// </summary>
        bool ShowDesignOutlines { get; }
    }
}
