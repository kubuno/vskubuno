using System.Collections.Generic;
using System.Text;
using Kubuno.Desktop.Designer.Editing;

namespace Kubuno.Desktop.Tests.Designer.Editing.Fakes
{
    /// <summary>In-memory <see cref="IEditableTextBuffer"/> for unit-testing <see cref="BufferEditCore"/>/<see cref="CompoundEditCoordinator"/> without a real VS <c>ITextBuffer</c>.</summary>
    internal sealed class FakeEditableTextBuffer : IEditableTextBuffer
    {
        private string _text;

        public FakeEditableTextBuffer(string initialText, int initialVersion = 0)
        {
            _text = initialText;
            CurrentVersion = initialVersion;
        }

        public int CurrentVersion { get; private set; }

        public int ApplyEditsCallCount { get; private set; }

        public string GetCurrentText() => _text;

        public void ApplyEdits(IReadOnlyList<PlannedTextEdit> edits)
        {
            ApplyEditsCallCount++;

            var builder = new StringBuilder(_text);
            foreach (var edit in edits)
            {
                builder.Remove(edit.StartOffset, edit.Length);
                builder.Insert(edit.StartOffset, edit.NewText);
            }

            _text = builder.ToString();
            CurrentVersion++;
        }
    }
}
