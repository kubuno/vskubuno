using System.ComponentModel.Composition;
using Kubuno.VisualStudio.Core.IntelliSense;
using Microsoft.VisualStudio.Commanding;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Editor.Commanding.Commands;
using Microsoft.VisualStudio.Utilities;

namespace Kubuno.VisualStudio.LanguageService.IntelliSense
{
    /// <summary>
    /// Enter inside a Rust doc comment continues it, like <c>///</c> in C#: the new line starts with the same
    /// indentation and <c>/// </c> (or <c>//! </c>); a plain <c>//</c> comment is continued when Enter splits it.
    /// </summary>
    [Export(typeof(ICommandHandler))]
    [Name("Kubuno Rust Doc Comment Continuation")]
    [ContentType(Constants.RustContentType)]
    [TextViewRole(PredefinedTextViewRoles.Interactive)]
    internal sealed class RustDocCommentContinuation : ICommandHandler<ReturnKeyCommandArgs>
    {
        [Import]
        internal IAsyncCompletionBroker CompletionBroker { get; set; } = null!;

        public string DisplayName => "Kubuno Rust Doc Comment Continuation";

        public CommandState GetCommandState(ReturnKeyCommandArgs args) => CommandState.Unspecified;

        public bool ExecuteCommand(ReturnKeyCommandArgs args, CommandExecutionContext executionContext)
        {
            var view = args.TextView;
            if (CompletionBroker.IsCompletionActive(view) || !view.Selection.IsEmpty || RustSnippetSession.Get(view) != null)
            {
                return false;
            }

            var caret = view.Caret.Position.BufferPosition;
            if (!ReferenceEquals(caret.Snapshot.TextBuffer, args.SubjectBuffer))
            {
                return false;
            }

            var line = caret.GetContainingLine();
            var before = caret.Snapshot.GetText(line.Start.Position, caret.Position - line.Start.Position);
            var after = caret.Snapshot.GetText(caret.Position, line.End.Position - caret.Position);
            var prefix = CommentContinuation.PrefixFor(before, after);
            if (prefix is null)
            {
                return false;
            }

            var lineBreak = line.GetLineBreakText();
            if (lineBreak.Length == 0)
            {
                lineBreak = view.Options.GetOptionValue(DefaultOptions.NewLineCharacterOptionId);
            }

            // Spaces around the caret go, like the editor's own Enter (but not the comment marker's own).
            int from = caret.Position;
            int markerEnd = line.Start.Position + prefix.TrimEnd().Length;
            while (from > markerEnd && caret.Snapshot[from - 1] == ' ')
            {
                from--;
            }

            int to = caret.Position;
            while (to < line.End.Position && caret.Snapshot[to] == ' ')
            {
                to++;
            }

            var inserted = lineBreak + prefix;
            var snapshot = args.SubjectBuffer.Replace(new Span(from, to - from), inserted);
            view.Caret.MoveTo(new SnapshotPoint(snapshot, from + inserted.Length));
            view.Caret.EnsureVisible();
            return true;
        }
    }
}
