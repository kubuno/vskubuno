using System.Collections.Generic;

namespace Kubuno.Desktop.Designer.Editing
{
    /// <summary>
    /// The narrow seam <see cref="BufferEditCore"/> depends on instead of the full
    /// <c>Microsoft.VisualStudio.Text.ITextBuffer</c> surface, so the version-check + plan + apply
    /// algorithm is unit-testable with a fake (see tests/.../Editing/Fakes/FakeEditableTextBuffer.cs) -
    /// no live VS needed, per this package's own test-strategy note.
    /// <see cref="Infrastructure.BufferEditApplier"/> is the real, VS-dependent implementation.
    /// </summary>
    public interface IEditableTextBuffer
    {
        /// <summary>A monotonically increasing version number, bumped by every <see cref="ApplyEdits"/> call - <c>ITextSnapshot.Version.VersionNumber</c> on the real buffer.</summary>
        int CurrentVersion { get; }

        string GetCurrentText();

        /// <summary>
        /// Applies every planned edit as ONE buffer edit (docs/DESIGNER.md §2: "one surgical edit
        /// applied to the live VS ITextBuffer" / this task's "in ONE ITextEdit (one undo unit)"). Offsets
        /// in <paramref name="edits"/> are against the text <see cref="GetCurrentText"/> returned just
        /// before this call, in the same version.
        /// </summary>
        void ApplyEdits(IReadOnlyList<PlannedTextEdit> edits);
    }
}
