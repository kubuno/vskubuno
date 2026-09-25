using System;
using System.Collections.Generic;
using Kubuno.VisualStudio.Designer.Editing;
using Kubuno.VisualStudio.Designer.Selection;

namespace Kubuno.VisualStudio.Designer.Tests.Selection.Fakes
{
    internal sealed class FakeTextViewSelectionAdapter : ITextViewSelectionAdapter
    {
        public LspPosition CaretPosition { get; set; } = new LspPosition(0, 0);

        public string CurrentText { get; set; } = string.Empty;

        public List<LspRange> Selections { get; } = new List<LspRange>();

        public event EventHandler? CaretMoved;

        public LspPosition GetCaretPosition() => CaretPosition;

        public string GetCurrentText() => CurrentText;

        public void SelectElementRange(LspRange range) => Selections.Add(range);

        public void RaiseCaretMoved() => CaretMoved?.Invoke(this, EventArgs.Empty);
    }
}
