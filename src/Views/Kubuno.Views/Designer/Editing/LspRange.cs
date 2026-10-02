using System;

namespace Kubuno.Views.Designer.Editing
{
    /// <summary>A half-open <c>[Start, End)</c> LSP-style range - the <c>range</c> half of <c>kubuno/applyEdit</c>'s <c>{range, newText}</c> result (docs/DESIGNER.md §2).</summary>
    public readonly struct LspRange : IEquatable<LspRange>
    {
        public LspRange(LspPosition start, LspPosition end)
        {
            Start = start;
            End = end;
        }

        public LspPosition Start { get; }

        public LspPosition End { get; }

        public bool Equals(LspRange other) => Start.Equals(other.Start) && End.Equals(other.End);

        public override bool Equals(object? obj) => obj is LspRange other && Equals(other);

        public override int GetHashCode() => unchecked((Start.GetHashCode() * 397) ^ End.GetHashCode());

        public override string ToString() => $"{Start}-{End}";

        public static bool operator ==(LspRange left, LspRange right) => left.Equals(right);

        public static bool operator !=(LspRange left, LspRange right) => !left.Equals(right);
    }
}
