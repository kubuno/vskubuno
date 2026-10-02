using System;
using System.Linq;

namespace Kubuno.Desktop.Designer.Editing
{
    /// <summary>
    /// Gives a new child element (a Toolbox drop or double-click, <c>kubuno/applyEdit</c>'s
    /// <c>insertChild</c>) its own, properly indented line - the way the WinForms/XAML designers lay out
    /// what they add. <c>kubuno_views::edit::insert_child</c> deliberately inserts the fragment verbatim
    /// ("the caller controls formatting"): right before the next sibling, or right before the parent's
    /// end tag, which on its own gives <c>  &lt;Button/&gt;&lt;/Stack&gt;</c>. This only touches that one
    /// insertion's text, and only when the insertion point starts an otherwise blank-prefixed line (the
    /// usual one-element-per-line layout); anything else is left exactly as the server produced it.
    /// Pure (strings in, string out) - unit-tested.
    /// </summary>
    public static class InsertChildFormatter
    {
        /// <param name="linePrefix">The text of the insertion line before the insertion point.</param>
        /// <param name="lineRest">The text of the insertion line after the insertion point.</param>
        /// <param name="previousNonBlankLine">The closest non-blank line above the insertion line (null if none).</param>
        /// <param name="fragment">The element text the server inserts.</param>
        /// <param name="newLine">The document's line break.</param>
        public static string Format(string linePrefix, string lineRest, string? previousNonBlankLine, string fragment, string newLine)
        {
            if (linePrefix is null || lineRest is null || fragment is null || newLine is null)
            {
                throw new ArgumentNullException(linePrefix is null ? nameof(linePrefix) : lineRest is null ? nameof(lineRest) : fragment is null ? nameof(fragment) : nameof(newLine));
            }

            if (linePrefix.Any(c => c != ' ' && c != '\t') || fragment.IndexOf('\n') >= 0)
            {
                return fragment;
            }

            var indent = linePrefix;
            if (!lineRest.TrimStart().StartsWith("</", StringComparison.Ordinal))
            {
                // Before a sibling that starts this line: the new element takes the sibling's indentation.
                return fragment + newLine + indent;
            }

            // Before the parent's end tag: one level deeper than the end tag - the previous child's
            // indentation when there is one, else one indentation unit more than the parent.
            var previousIndent = LeadingWhitespace(previousNonBlankLine ?? string.Empty);
            var previousIsParentStart = previousNonBlankLine is not null && previousIndent.Length <= indent.Length;
            var childIndent = !previousIsParentStart && previousIndent.StartsWith(indent, StringComparison.Ordinal)
                ? previousIndent
                : indent + (indent.IndexOf('\t') >= 0 ? "\t" : "  ");
            return childIndent.Substring(indent.Length) + fragment + newLine + indent;
        }

        private static string LeadingWhitespace(string line) => new string(line.TakeWhile(c => c == ' ' || c == '\t').ToArray());
    }
}
