using System;

namespace Kubuno.VisualStudio.Designer.Editing
{
    /// <summary>
    /// An LSP-style, zero-based text position: <see cref="Line"/> counts newline-terminated lines,
    /// <see cref="Character"/> counts UTF-16 code units within that line (the LSP spec's own unit,
    /// which also happens to be what a .NET <see cref="string"/> and
    /// <c>Microsoft.VisualStudio.Text.ITextSnapshot</c> both index by, so no UTF-8/UTF-16 conversion is
    /// needed on this side). Produced by <c>kubuno/applyEdit</c>'s <c>{range, newText}</c> result
    /// (docs/DESIGNER.md §2/§3) - this is the C# side of that JSON shape, not a VS type.
    /// </summary>
    public readonly struct LspPosition : IEquatable<LspPosition>
    {
        public LspPosition(int line, int character)
        {
            if (line < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(line), line, "Line must be >= 0.");
            }

            if (character < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(character), character, "Character must be >= 0.");
            }

            Line = line;
            Character = character;
        }

        public int Line { get; }

        public int Character { get; }

        public bool Equals(LspPosition other) => Line == other.Line && Character == other.Character;

        public override bool Equals(object? obj) => obj is LspPosition other && Equals(other);

        public override int GetHashCode() => unchecked((Line * 397) ^ Character);

        public override string ToString() => $"({Line}:{Character})";

        public static bool operator ==(LspPosition left, LspPosition right) => left.Equals(right);

        public static bool operator !=(LspPosition left, LspPosition right) => !left.Equals(right);
    }
}
