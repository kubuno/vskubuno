using System.Text.RegularExpressions;

namespace Kubuno.VisualStudio.Core.IntelliSense
{
    /// <summary>What Enter continues in a Rust comment (see the VSIX's <c>RustDocCommentContinuation</c>).</summary>
    public static class CommentContinuation
    {
        private static readonly Regex DocComment = new Regex(@"^(\s*)(///|//!)(?!/)", RegexOptions.CultureInvariant);
        private static readonly Regex LineComment = new Regex(@"^(\s*)//(?![/!])", RegexOptions.CultureInvariant);

        /// <summary>
        /// The text that starts the new line when Enter is pressed between <paramref name="beforeCaret"/> and
        /// <paramref name="afterCaret"/> (the two halves of the caret's line), or null for a plain Enter:
        /// <c>/// </c> or <c>//! </c> anywhere after a doc comment's marker, <c>// </c> when Enter splits a line comment.
        /// </summary>
        public static string? PrefixFor(string beforeCaret, string afterCaret)
        {
            var doc = DocComment.Match(beforeCaret);
            if (doc.Success)
            {
                return doc.Groups[1].Value + doc.Groups[2].Value + " ";
            }

            var comment = LineComment.Match(beforeCaret);
            if (comment.Success && beforeCaret.TrimEnd().Length > comment.Length && afterCaret.Trim().Length > 0)
            {
                return comment.Groups[1].Value + "// ";
            }

            return null;
        }
    }
}
