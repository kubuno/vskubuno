using System;

namespace Kubuno.Cargo.Toml
{
    /// <summary>
    /// One contiguous replacement, expressed against the text as it was BEFORE the edit: the range
    /// [<see cref="Start"/>, <see cref="Start"/> + <see cref="OldLength"/>) is replaced by
    /// <see cref="NewText"/>. The common prefix and suffix are trimmed, so it is minimal.
    /// </summary>
    public readonly struct TomlEdit
    {
        /// <summary>Creates an edit.</summary>
        public TomlEdit(int start, int oldLength, string newText)
        {
            Start = start;
            OldLength = oldLength;
            NewText = newText ?? throw new ArgumentNullException(nameof(newText));
        }

        /// <summary>Offset of the replaced range in the pre-edit text.</summary>
        public int Start { get; }

        /// <summary>Length of the replaced range in the pre-edit text.</summary>
        public int OldLength { get; }

        /// <summary>Replacement text.</summary>
        public string NewText { get; }

        /// <summary>Applies this edit to <paramref name="oldText"/> (the text the edit was computed against).</summary>
        public string Apply(string oldText)
        {
            if (oldText == null) throw new ArgumentNullException(nameof(oldText));
            return oldText.Substring(0, Start) + NewText + oldText.Substring(Start + OldLength);
        }

        internal static TomlEdit Diff(string oldText, string newText)
        {
            int max = Math.Min(oldText.Length, newText.Length);
            int p = 0;
            while (p < max && oldText[p] == newText[p]) p++;
            int s = 0;
            while (s < max - p && oldText[oldText.Length - 1 - s] == newText[newText.Length - 1 - s]) s++;
            return new TomlEdit(p, oldText.Length - p - s, newText.Substring(p, newText.Length - p - s));
        }
    }
}
