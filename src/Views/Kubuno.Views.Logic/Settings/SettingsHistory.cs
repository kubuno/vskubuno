using System.Collections.Generic;

namespace Kubuno.Views.Logic.Settings
{
    /// <summary>
    /// The undo/redo history of the settings editor (docs/STORAGE-COMPONENTS.md §5.3): the file's text before each change
    /// the grid made. Undo puts the previous text back (and remembers the current one for redo); a change made outside
    /// the grid (the code view, a reload) clears it, so an undo never reverts someone else's edit.
    /// </summary>
    public sealed class SettingsHistory
    {
        /// <summary>The most steps kept.</summary>
        public const int Capacity = 200;

        private readonly LinkedList<string> _undo = new LinkedList<string>();
        private readonly Stack<string> _redo = new Stack<string>();

        public bool CanUndo => _undo.Count > 0;

        public bool CanRedo => _redo.Count > 0;

        /// <summary>The grid changed the text from <paramref name="before"/> to <paramref name="after"/>.</summary>
        public void Record(string before, string after)
        {
            if (before == after)
            {
                return;
            }

            _undo.AddLast(before);
            if (_undo.Count > Capacity)
            {
                _undo.RemoveFirst();
            }

            _redo.Clear();
        }

        /// <summary>The text to put back for an undo of <paramref name="current"/>, null when there is none.</summary>
        public string? Undo(string current)
        {
            if (_undo.Last is not { } last)
            {
                return null;
            }

            _undo.RemoveLast();
            _redo.Push(current);
            return last.Value;
        }

        /// <summary>The text to put back for a redo, null when there is none.</summary>
        public string? Redo(string current)
        {
            if (_redo.Count == 0)
            {
                return null;
            }

            _undo.AddLast(current);
            return _redo.Pop();
        }

        /// <summary>Forgets everything (a change made outside the grid).</summary>
        public void Clear()
        {
            _undo.Clear();
            _redo.Clear();
        }
    }
}
