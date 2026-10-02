using System;
using System.Collections.Generic;
using System.Linq;

namespace Kubuno.Shared.DevAssistant.Logic.Changes
{
    /// <summary>
    /// One contiguous change between two versions of a file: <see cref="OldCount"/> lines starting at
    /// <see cref="OldStart"/> (0-based) are replaced by <see cref="NewLines"/>. Lines keep their own terminators, so
    /// applying a hunk never changes the file's line endings.
    /// </summary>
    public sealed class DiffHunk
    {
        public DiffHunk(int index, int oldStart, IReadOnlyList<string> oldLines, int newStart, IReadOnlyList<string> newLines)
        {
            Index = index;
            OldStart = oldStart;
            OldLines = oldLines;
            NewStart = newStart;
            NewLines = newLines;
        }

        /// <summary>Position of the hunk in its file (0-based), the key used to accept or reject it.</summary>
        public int Index { get; }

        public int OldStart { get; }

        public IReadOnlyList<string> OldLines { get; }

        public int OldCount => OldLines.Count;

        public int NewStart { get; }

        public IReadOnlyList<string> NewLines { get; }

        public int Added => NewLines.Count;

        public int Removed => OldLines.Count;
    }

    /// <summary>A line-based diff (longest common subsequence after trimming the common prefix and suffix).</summary>
    public static class LineDiff
    {
        /// <summary>Above this many lines of differing middle on both sides, the middle becomes one hunk (no quadratic table).</summary>
        private const long MaxCells = 4_000_000;

        /// <summary>Splits <paramref name="text"/> into lines that keep their terminator (\r\n, \n or \r).</summary>
        public static List<string> SplitLines(string text)
        {
            var lines = new List<string>();
            int start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n' || text[i] == '\r')
                {
                    if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        i++;
                    }

                    lines.Add(text.Substring(start, i + 1 - start));
                    start = i + 1;
                }
            }

            if (start < text.Length)
            {
                lines.Add(text.Substring(start));
            }

            return lines;
        }

        /// <summary>The hunks turning <paramref name="oldText"/> into <paramref name="newText"/>, in file order.</summary>
        public static IReadOnlyList<DiffHunk> Compute(string oldText, string newText)
        {
            var a = SplitLines(oldText);
            var b = SplitLines(newText);
            int prefix = 0;
            while (prefix < a.Count && prefix < b.Count && a[prefix] == b[prefix])
            {
                prefix++;
            }

            int suffix = 0;
            while (suffix < a.Count - prefix && suffix < b.Count - prefix && a[a.Count - 1 - suffix] == b[b.Count - 1 - suffix])
            {
                suffix++;
            }

            int n = a.Count - prefix - suffix;
            int m = b.Count - prefix - suffix;
            var hunks = new List<DiffHunk>();
            if (n == 0 && m == 0)
            {
                return hunks;
            }

            if ((long)n * m > MaxCells)
            {
                hunks.Add(new DiffHunk(0, prefix, a.GetRange(prefix, n), prefix, b.GetRange(prefix, m)));
                return hunks;
            }

            // LCS lengths of the suffixes (lcs[i, j] = LCS of a[prefix+i..] and b[prefix+j..]).
            var lcs = new int[n + 1, m + 1];
            for (int i = n - 1; i >= 0; i--)
            {
                for (int j = m - 1; j >= 0; j--)
                {
                    lcs[i, j] = a[prefix + i] == b[prefix + j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
                }
            }

            int x = 0;
            int y = 0;
            while (x < n || y < m)
            {
                if (x < n && y < m && a[prefix + x] == b[prefix + y])
                {
                    x++;
                    y++;
                    continue;
                }

                int oldStart = x;
                int newStart = y;
                while ((x < n || y < m) && !(x < n && y < m && a[prefix + x] == b[prefix + y]))
                {
                    if (y >= m || (x < n && lcs[x + 1, y] >= lcs[x, y + 1]))
                    {
                        x++;
                    }
                    else
                    {
                        y++;
                    }
                }

                hunks.Add(new DiffHunk(
                    hunks.Count,
                    prefix + oldStart,
                    a.GetRange(prefix + oldStart, x - oldStart),
                    prefix + newStart,
                    b.GetRange(prefix + newStart, y - newStart)));
            }

            return hunks;
        }

        /// <summary>
        /// The text obtained by applying only the hunks whose <see cref="DiffHunk.Index"/> is in <paramref name="accepted"/>
        /// to <paramref name="oldText"/> (the hunks must come from <see cref="Compute"/> on that same text).
        /// </summary>
        public static string ApplySelected(string oldText, IReadOnlyList<DiffHunk> hunks, ISet<int> accepted)
        {
            var lines = SplitLines(oldText);
            var result = new System.Text.StringBuilder(oldText.Length);
            int position = 0;
            foreach (var hunk in hunks.OrderBy(h => h.OldStart))
            {
                for (; position < hunk.OldStart; position++)
                {
                    result.Append(lines[position]);
                }

                if (accepted.Contains(hunk.Index))
                {
                    foreach (var line in hunk.NewLines)
                    {
                        result.Append(line);
                    }
                }
                else
                {
                    foreach (var line in hunk.OldLines)
                    {
                        result.Append(line);
                    }
                }

                position = hunk.OldStart + hunk.OldCount;
            }

            for (; position < lines.Count; position++)
            {
                result.Append(lines[position]);
            }

            return result.ToString();
        }
    }
}
