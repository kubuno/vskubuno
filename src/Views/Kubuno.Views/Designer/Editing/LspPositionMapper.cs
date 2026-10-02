using System;

namespace Kubuno.Desktop.Designer.Editing
{
    /// <summary>
    /// Pure range/position -&gt; offset mapping over a plain <see cref="string"/> - no VS type involved,
    /// so it is fully unit-testable (see tests/.../Editing/LspPositionMapperTests.cs), per this
    /// package's own test-strategy note ("Unit-test the pure parts (range→offset mapping, edit ordering,
    /// conflict detection) without VS"). <see cref="Infrastructure.BufferEditApplier"/> calls this same
    /// code against a real <c>ITextSnapshot</c>'s text, so there is exactly one implementation of the
    /// mapping, not two that could drift.
    ///
    /// Recognizes "\n", "\r\n" and "\r" as line terminators (a lone "\r" is rare but valid); an
    /// out-of-range <see cref="LspPosition.Character"/> for its line clamps to end-of-line rather than
    /// throwing, mirroring how most LSP servers/clients treat "past end of line" as "end of line" for a
    /// pure-insertion edit.
    /// </summary>
    public static class LspPositionMapper
    {
        public static int ToOffset(string text, LspPosition position)
        {
            if (text is null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            int offset = 0;
            int line = 0;

            while (line < position.Line)
            {
                int lineEnd = FindLineEnd(text, offset, out int terminatorLength);
                if (lineEnd < 0)
                {
                    // Requested a line past the end of the document - clamp defensively rather than
                    // throw; a well-formed kubuno/applyEdit response should never do this, but a stale
                    // request racing a concurrent edit could.
                    return text.Length;
                }

                offset = lineEnd + terminatorLength;
                line++;
            }

            int currentLineEnd = FindLineEnd(text, offset, out _);
            int lineLength = (currentLineEnd < 0 ? text.Length : currentLineEnd) - offset;
            int character = Math.Min(position.Character, lineLength);
            return offset + character;
        }

        /// <summary>
        /// The inverse of <see cref="ToOffset"/>: the position (line, UTF-16 column) of <paramref name="offset"/> in
        /// <paramref name="text"/>, a <c>\r\n</c> counting as one line break. Clamped to the text.
        /// </summary>
        public static LspPosition FromOffset(string text, int offset)
        {
            if (text is null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            offset = Math.Max(0, Math.Min(offset, text.Length));
            int line = 0, lineStart = 0;
            for (var i = 0; i < offset; i++)
            {
                var loneCr = text[i] == '\r' && (i + 1 >= text.Length || text[i + 1] != '\n');
                if (text[i] == '\n' || loneCr)
                {
                    line++;
                    lineStart = i + 1;
                }
            }

            return new LspPosition(line, offset - lineStart);
        }

        public static (int Start, int End) ToOffsetRange(string text, LspRange range)
        {
            int start = ToOffset(text, range.Start);
            int end = ToOffset(text, range.End);
            if (end < start)
            {
                throw new ArgumentException($"Range end {range.End} resolves before its start {range.Start}.", nameof(range));
            }

            return (start, end);
        }

        /// <summary>Returns the index of the first line-terminator character at or after <paramref name="start"/>, or -1 if the text ends before one is found. <paramref name="terminatorLength"/> is 1 for "\n"/"\r" or 2 for "\r\n".</summary>
        private static int FindLineEnd(string text, int start, out int terminatorLength)
        {
            for (int i = start; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\n')
                {
                    terminatorLength = 1;
                    return i;
                }

                if (c == '\r')
                {
                    terminatorLength = (i + 1 < text.Length && text[i + 1] == '\n') ? 2 : 1;
                    return i;
                }
            }

            terminatorLength = 0;
            return -1;
        }
    }
}
