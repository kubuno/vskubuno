using System;
using System.Collections.Generic;

namespace Kubuno.VisualStudio.Designer.Editing
{
    /// <summary>
    /// The pure version-check + plan + apply algorithm behind DSG-5, over the narrow
    /// <see cref="IEditableTextBuffer"/> seam rather than a real VS type - unit-tested against
    /// <c>FakeEditableTextBuffer</c> (tests/.../Editing/BufferEditCoreTests.cs).
    /// <see cref="Infrastructure.BufferEditApplier"/> is the thin real-VS adapter that calls this same
    /// code against <c>Microsoft.VisualStudio.Text.ITextBuffer</c>.
    /// </summary>
    public static class BufferEditCore
    {
        public static BufferEditResult TryApply(IEditableTextBuffer buffer, ApplyEditRequest request)
        {
            if (buffer is null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            if (request is null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (request.Edits.Count == 0)
            {
                return BufferEditResult.Empty();
            }

            int currentVersion = buffer.CurrentVersion;
            if (currentVersion != request.BaseVersion)
            {
                return BufferEditResult.VersionMismatch(request.BaseVersion, currentVersion);
            }

            string text = buffer.GetCurrentText();

            IReadOnlyList<PlannedTextEdit> planned;
            try
            {
                planned = TextEditPlanner.Plan(text, request.Edits);
            }
            catch (TextEditConflictException ex)
            {
                return BufferEditResult.Conflict(ex.Message);
            }

            buffer.ApplyEdits(planned);
            return BufferEditResult.Applied();
        }
    }
}
