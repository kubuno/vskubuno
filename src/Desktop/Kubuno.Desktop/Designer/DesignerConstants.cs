namespace Kubuno.Desktop.Designer
{
    /// <summary>
    /// Shared identifiers for this library's editor factory. The "kbview" content type/file
    /// extension themselves are NOT duplicated here - they live in
    /// <c>Kubuno.Desktop.Views.KbviewConstants</c> (this project references that library, see
    /// its own csproj comment), and this factory targets exactly that content type/extension so the
    /// XML pane's <c>IVsTextLines</c> buffer keeps colorizing and talking to kubuno-views-ls through
    /// the same MEF content-type routing the plain text editor already uses.
    /// </summary>
    public static class DesignerConstants
    {
        /// <summary>
        /// CLSID of <see cref="EditorFactory.KbviewEditorFactory"/>, referenced by the VSIX's
        /// <c>[ProvideEditorFactory(typeof(KbviewEditorFactory), ...)]</c> attribute once integrated
        /// (see INTEGRATION.md). Generated once and hard-coded - an editor factory's CLSID must be
        /// stable across versions, exactly like a package's own GUID.
        /// </summary>
        public const string EditorFactoryGuidString = "1DCF5C91-A51E-4DE8-B066-680D6636C094";

        /// <summary>
        /// GUID of the command UI context shown while this editor is active (returned as
        /// <c>pguidCmdUI</c> from <see cref="EditorFactory.KbviewEditorFactory.CreateEditorInstance"/>).
        /// Not yet bound to any VSCT command visibility rule - reserved for DSG-4's toolbox/property
        /// grid tool windows, which will want to auto-show only while a <c>.kbview</c> designer pane
        /// has focus (see INTEGRATION.md).
        /// </summary>
        public const string CommandUiContextGuidString = "B634717B-8D65-47A1-BCAD-DA3C18B559C1";

        /// <summary>User-facing name of this editor, shown in "Open With..." and as the default editor caption.</summary>
        public const string EditorName = "Kubuno View Designer";

        /// <summary>
        /// Registration priority this editor factory should be given for the "kbview" extension
        /// (see INTEGRATION.md, "Open With / default editor registration", for the full reasoning and
        /// the open question this value cannot fully resolve on its own). Deliberately higher
        /// (lower-precedence) than the conventional 0x32 default-editor priority used by many VSSDK
        /// samples, so this editor shows up in "Open With..." without preempting the plain XML/text
        /// editor as the double-click default while the designer is a placeholder.
        /// </summary>
        public const int EditorExtensionPriority = 0x60;

        /// <summary>Tools &gt; Options page name for this library's own options (see <see cref="Options.KbviewDesignerOptionsPage"/>).</summary>
        public const string OptionsPageName = "Designer";

        /// <summary>GUID of <see cref="ToolWindows.OutlineToolWindow"/>, referenced by the VSIX's
        /// <c>[ProvideToolWindow(typeof(OutlineToolWindow))]</c> attribute (INTEGRATION.md §9, point 6).</summary>
        public const string OutlineToolWindowGuidString = "9A3D2C10-4E71-4F6B-8E5C-2D6A8B1F3C90";
    }
}
