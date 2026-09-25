using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.Text;

namespace Kubuno.VisualStudio.Designer.Editing.Infrastructure
{
    /// <summary>
    /// The only VS-dependent piece of this file: adapts a real <c>Microsoft.VisualStudio.Text.ITextBuffer</c>
    /// to <see cref="IEditableTextBuffer"/> so <see cref="BufferEditCore"/>'s already-unit-tested
    /// version-check + plan + apply algorithm runs unchanged against it. Not unit-tested here - it needs
    /// a live <c>ITextBuffer</c> from a real editor document, the same reasoning
    /// UI/CodeWindowHost.cs and EditorFactory/DesignerWindowPane.cs already give for staying out of
    /// tests/Kubuno.VisualStudio.Designer.Tests (see that project's own csproj comment); a manual
    /// Ctrl+Z check in the experimental instance is this class's test strategy, per docs/DESIGNER.md's
    /// DSG-5 row.
    ///
    /// docs/DESIGNER.md §2: "ends as one surgical edit applied to the live VS ITextBuffer ... a small
    /// ITextEdit.Replace instead of diffing two whole-file strings". <see cref="ApplyEdits"/> is exactly
    /// that: every planned edit from one <c>kubuno/applyEdit</c> response goes into ONE
    /// <c>ITextEdit</c>, so VS records ONE undo unit for the whole response no matter how many
    /// attributes/children it touched.
    /// </summary>
    public sealed class BufferEditApplier : IEditableTextBuffer
    {
        private readonly ITextBuffer _buffer;

        public BufferEditApplier(ITextBuffer buffer)
        {
            _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        }

        public int CurrentVersion => _buffer.CurrentSnapshot.Version.VersionNumber;

        public string GetCurrentText() => _buffer.CurrentSnapshot.GetText();

        public void ApplyEdits(IReadOnlyList<PlannedTextEdit> edits)
        {
            using (var edit = _buffer.CreateEdit())
            {
                foreach (var plannedEdit in edits)
                {
                    edit.Replace(new Span(plannedEdit.StartOffset, plannedEdit.Length), plannedEdit.NewText);
                }

                edit.Apply();
            }
        }

        /// <summary>Convenience entry point: version-check + plan + apply in one call against the real buffer this instance wraps.</summary>
        public BufferEditResult Apply(ApplyEditRequest request) => BufferEditCore.TryApply(this, request);
    }
}
