using System;

namespace Kubuno.VisualStudio.Designer.Editing
{
    /// <summary>A <see cref="TextEditDto"/> with its LSP range already resolved to plain character offsets against one specific text snapshot (see <see cref="TextEditPlanner"/>).</summary>
    public readonly struct PlannedTextEdit
    {
        public PlannedTextEdit(int startOffset, int endOffset, string newText)
        {
            if (startOffset < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(startOffset), startOffset, "Start offset must be >= 0.");
            }

            if (endOffset < startOffset)
            {
                throw new ArgumentOutOfRangeException(nameof(endOffset), endOffset, "End offset must be >= start offset.");
            }

            StartOffset = startOffset;
            EndOffset = endOffset;
            NewText = newText ?? throw new ArgumentNullException(nameof(newText));
        }

        public int StartOffset { get; }

        public int EndOffset { get; }

        public int Length => EndOffset - StartOffset;

        public string NewText { get; }

        public override string ToString() => $"[{StartOffset},{EndOffset}) -> \"{NewText}\"";
    }
}
