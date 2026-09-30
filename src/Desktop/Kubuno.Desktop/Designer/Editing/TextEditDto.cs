using System;

namespace Kubuno.Desktop.Designer.Editing
{
    /// <summary>
    /// One LSP-style text edit exactly as <c>kubuno/applyEdit</c> returns it (docs/DESIGNER.md §2: "that
    /// return value needs to travel as {range, newText}, not just the new full-text string, so the VS
    /// side can apply a single minimal ITextEdit.Replace instead of diffing two whole-file strings").
    /// </summary>
    public sealed class TextEditDto
    {
        public TextEditDto(LspRange range, string newText)
        {
            Range = range;
            NewText = newText ?? throw new ArgumentNullException(nameof(newText));
        }

        public LspRange Range { get; }

        public string NewText { get; }
    }
}
