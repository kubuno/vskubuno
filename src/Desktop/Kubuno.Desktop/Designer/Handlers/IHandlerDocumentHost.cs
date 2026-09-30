using Kubuno.VisualStudio.Designer.Editing;

namespace Kubuno.VisualStudio.Designer.Handlers
{
    /// <summary>
    /// The seam <see cref="HandlerCreationService"/> depends on instead of raw VS document/editor
    /// services, mirroring <see cref="IEditableTextBuffer"/>'s own reasoning: unit-testable with a fake
    /// (see tests/Kubuno.VisualStudio.Designer.Tests/Handlers/Fakes/FakeHandlerDocumentHost.cs).
    /// <see cref="Infrastructure.VsHandlerDocumentHost"/> is the real implementation.
    /// </summary>
    public interface IHandlerDocumentHost
    {
        /// <summary>
        /// Opens (or reuses an already-open) document at <paramref name="fileUri"/> and returns its
        /// editable buffer. Called once per file in a <c>kubuno/createHandler</c> response's
        /// <c>edit.changes</c> map - <see cref="HandlerCreationService"/> applies each file's edits as
        /// its own buffer edit, per this package's own "one undo unit per file" requirement (never one
        /// buffer spanning both the <c>.kbview</c> and the code-behind <c>.rs</c> file).
        /// </summary>
        IEditableTextBuffer OpenBuffer(string fileUri);

        /// <summary>Opens (or activates) <paramref name="fileUri"/> and moves the caret to <paramref name="position"/> - "opens the .rs at the new fn" (docs/DESIGNER.md §6/§8, DSG-10's C# half).</summary>
        void NavigateTo(string fileUri, LspPosition position);
    }
}
