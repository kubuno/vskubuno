using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
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
    /// The placeholders of an expanded completion snippet (argument names, a postfix template's condition...),
    /// visited with Tab / Shift+Tab like a C# snippet's fields; Enter jumps to the snippet's end (<c>$0</c>) and
    /// Esc leaves the placeholders as they are. The session ends by itself when the caret leaves the snippet.
    /// </summary>
    internal sealed class RustSnippetSession
    {
        private readonly ITextView _view;
        private readonly List<ITrackingSpan> _stops;
        private readonly ITrackingPoint _end;
        private readonly ITrackingSpan _whole;
        private int _current;

        private RustSnippetSession(ITextView view, List<ITrackingSpan> stops, ITrackingPoint end, ITrackingSpan whole)
        {
            _view = view;
            _stops = stops;
            _end = end;
            _whole = whole;
        }

        public static void Start(ITextView view, ITextSnapshot snapshot, int insertedAt, LspSnippet snippet)
        {
            End(view);
            var stops = snippet.Stops
                .Where(s => s.Index != 0)
                .Select(s => snapshot.CreateTrackingSpan(insertedAt + s.Start, s.Length, SpanTrackingMode.EdgeInclusive))
                .ToList();
            var end = snapshot.CreateTrackingPoint(insertedAt + snippet.FinalCaret, PointTrackingMode.Positive);
            var whole = snapshot.CreateTrackingSpan(insertedAt, snippet.Text.Length, SpanTrackingMode.EdgeInclusive);
            var session = new RustSnippetSession(view, stops, end, whole);
            view.Properties[typeof(RustSnippetSession)] = session;
            session.Select(0);
        }

        public static RustSnippetSession? Get(ITextView view)
        {
            if (!view.Properties.TryGetProperty(typeof(RustSnippetSession), out RustSnippetSession session))
            {
                return null;
            }

            // Ended by the caret leaving the snippet.
            var caret = view.Caret.Position.BufferPosition;
            var whole = session._whole.GetSpan(caret.Snapshot);
            if (caret.Position < whole.Start.Position || caret.Position > whole.End.Position)
            {
                End(view);
                return null;
            }

            return session;
        }

        public static void End(ITextView view) => view.Properties.RemoveProperty(typeof(RustSnippetSession));

        public void Next()
        {
            if (_current + 1 >= _stops.Count)
            {
                Finish();
                return;
            }

            Select(_current + 1);
        }

        public void Previous() => Select(_current > 0 ? _current - 1 : 0);

        /// <summary>Caret to the snippet's end (<c>$0</c>), session over.</summary>
        public void Finish()
        {
            var snapshot = _view.TextBuffer.CurrentSnapshot;
            _view.Selection.Clear();
            _view.Caret.MoveTo(_end.GetPoint(snapshot));
            _view.Caret.EnsureVisible();
            End(_view);
        }

        private void Select(int index)
        {
            _current = index;
            var span = _stops[index].GetSpan(_view.TextBuffer.CurrentSnapshot);
            _view.Selection.Select(span, false);
            _view.Caret.MoveTo(span.End);
            _view.Caret.EnsureVisible();
        }
    }

    /// <summary>Tab / Shift+Tab / Enter / Esc inside a <see cref="RustSnippetSession"/> (the completion list keeps them while open).</summary>
    [Export(typeof(ICommandHandler))]
    [Name("Kubuno Rust Snippet Placeholders")]
    [ContentType(Constants.RustContentType)]
    [TextViewRole(PredefinedTextViewRoles.Interactive)]
    internal sealed class RustSnippetCommandHandler :
        ICommandHandler<TabKeyCommandArgs>,
        ICommandHandler<BackTabKeyCommandArgs>,
        ICommandHandler<ReturnKeyCommandArgs>,
        ICommandHandler<EscapeKeyCommandArgs>
    {
        [Import]
        internal IAsyncCompletionBroker CompletionBroker { get; set; } = null!;

        public string DisplayName => "Kubuno Rust Snippet Placeholders";

        public CommandState GetCommandState(TabKeyCommandArgs args) => CommandState.Unspecified;

        public CommandState GetCommandState(BackTabKeyCommandArgs args) => CommandState.Unspecified;

        public CommandState GetCommandState(ReturnKeyCommandArgs args) => CommandState.Unspecified;

        public CommandState GetCommandState(EscapeKeyCommandArgs args) => CommandState.Unspecified;

        public bool ExecuteCommand(TabKeyCommandArgs args, CommandExecutionContext executionContext) =>
            Handle(args.TextView, session => session.Next());

        public bool ExecuteCommand(BackTabKeyCommandArgs args, CommandExecutionContext executionContext) =>
            Handle(args.TextView, session => session.Previous());

        public bool ExecuteCommand(ReturnKeyCommandArgs args, CommandExecutionContext executionContext) =>
            Handle(args.TextView, session => session.Finish());

        public bool ExecuteCommand(EscapeKeyCommandArgs args, CommandExecutionContext executionContext)
        {
            if (!CompletionBroker.IsCompletionActive(args.TextView) && RustSnippetSession.Get(args.TextView) != null)
            {
                RustSnippetSession.End(args.TextView);
            }

            return false;
        }

        private bool Handle(ITextView view, System.Action<RustSnippetSession> action)
        {
            if (CompletionBroker.IsCompletionActive(view) || RustSnippetSession.Get(view) is not { } session)
            {
                return false;
            }

            action(session);
            return true;
        }
    }
}
