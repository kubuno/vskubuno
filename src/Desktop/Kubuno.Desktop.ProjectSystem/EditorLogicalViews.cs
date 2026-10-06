using System;

namespace Kubuno.Desktop.ProjectSystem
{
    /// <summary>
    /// The logical view the project-specific editors of a <c>.rsproj</c> (<see cref="KbresEditorProvider"/>,
    /// <see cref="KbsettingsEditorProvider"/>) open by default.
    /// </summary>
    /// <remarks>
    /// The <c>.kbview</c> / <c>.kbcontrol</c> designer has no project-specific provider any more: its editor factory is the
    /// global default editor of views (<c>DesignerConstants.EditorExtensionPriority</c> above the text editor's), so a
    /// double-click opens it in every project system, Open Folder and a loose file alike - and Visual Studio's own
    /// per-user "Open With... &gt; Set as Default" choice still wins, which a CPS provider marked as default would
    /// silently override inside a <c>.rsproj</c>.
    /// </remarks>
    internal static class EditorLogicalViews
    {
        /// <summary><c>VSConstants.LOGVIEWID_Designer</c> (verified by reflection on Microsoft.VisualStudio.Shell.15.0).</summary>
        internal static readonly Guid Designer = new Guid("7651a702-06e5-11d1-8ebd-00a0c90f26ea");
    }
}