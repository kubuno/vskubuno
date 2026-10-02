using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Kubuno.Views.Designer.Editing
{
    /// <summary>
    /// Pure edit ordering + conflict detection, the other two "pure parts" this package's test strategy
    /// calls out ("edit ordering, conflict detection"). Operates purely on offsets computed by
    /// <see cref="LspPositionMapper"/> against a given full-text snapshot - no VS type involved, so it is
    /// fully unit-testable (see tests/.../Editing/TextEditPlannerTests.cs).
    /// </summary>
    public static class TextEditPlanner
    {
        /// <summary>
        /// Resolves every edit's range to offsets, validates none of them overlap
        /// (<see cref="TextEditConflictException"/> if they do - LSP allows several zero-length inserts
        /// at the very same offset, applied in the array's own order, but never a genuine positive-length
        /// overlap), and returns them ordered from the highest start offset to the lowest.
        ///
        /// That descending order is not required by <c>Microsoft.VisualStudio.Text.ITextEdit</c> itself -
        /// it resolves every <c>Replace</c> span against the *original* snapshot regardless of call order
        /// (docs/DESIGNER.md §2) - but it is exactly the order a naive sequential splice needs (see
        /// <see cref="ApplyToPlainText"/>), so this method's contract simply always returns that order,
        /// and <see cref="Infrastructure.BufferEditApplier"/> reuses it rather than re-deriving its own.
        /// </summary>
        public static IReadOnlyList<PlannedTextEdit> Plan(string text, IReadOnlyList<TextEditDto> edits)
        {
            if (text is null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            if (edits is null)
            {
                throw new ArgumentNullException(nameof(edits));
            }

            if (edits.Count == 0)
            {
                return Array.Empty<PlannedTextEdit>();
            }

            var planned = new List<PlannedTextEdit>(edits.Count);
            foreach (var edit in edits)
            {
                var (start, end) = LspPositionMapper.ToOffsetRange(text, edit.Range);
                planned.Add(new PlannedTextEdit(start, end, edit.NewText));
            }

            DetectConflicts(planned);

            return planned.OrderByDescending(p => p.StartOffset).ToList();
        }

        /// <summary>Applies <paramref name="edits"/> to <paramref name="text"/> as a plain, sequential string splice - a pure reference implementation used both by tests and as a fallback for any caller that has no <c>ITextBuffer</c> to hand.</summary>
        public static string ApplyToPlainText(string text, IReadOnlyList<TextEditDto> edits)
        {
            var planned = Plan(text, edits);
            var builder = new StringBuilder(text);
            foreach (var edit in planned)
            {
                builder.Remove(edit.StartOffset, edit.Length);
                builder.Insert(edit.StartOffset, edit.NewText);
            }

            return builder.ToString();
        }

        /// <summary>Classic sorted-interval overlap scan: sort ascending by start (stable, so equal-start edits keep their input order per LSP's "multiple inserts at the same position" allowance), then track the running maximum end seen so far.</summary>
        private static void DetectConflicts(IReadOnlyList<PlannedTextEdit> planned)
        {
            var byStart = planned.OrderBy(p => p.StartOffset).ToList();
            int maxEndSoFar = -1;

            foreach (var edit in byStart)
            {
                if (maxEndSoFar >= 0 && edit.StartOffset < maxEndSoFar)
                {
                    throw new TextEditConflictException(
                        $"Edit [{edit.StartOffset},{edit.EndOffset}) overlaps a preceding edit ending at offset {maxEndSoFar}.");
                }

                if (edit.EndOffset > maxEndSoFar)
                {
                    maxEndSoFar = edit.EndOffset;
                }
            }
        }
    }
}
