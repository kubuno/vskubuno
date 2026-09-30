using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Kubuno.Core.Logic.Lsp;
using Kubuno.Rust.Logic.IntelliSense;
using Kubuno.Core.Logging;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Newtonsoft.Json.Linq;

namespace Kubuno.Rust.LanguageService.IntelliSense
{
    /// <summary>
    /// Commits <see cref="RustCompletionSource"/>'s items: rust-analyzer's text edit (snippets expanded, with a Tab
    /// session over their placeholders), its <c>additionalTextEdits</c> (the <c>use</c> of an auto-import item,
    /// resolved on demand), and Parameter Info reopened after a completed call. Committed by a punctuation
    /// character (<c>(</c>, <c>.</c>, <c>;</c>...) a function or macro is inserted as its bare name, the way C#
    /// does, and the character is then typed.
    /// </summary>
    internal sealed class RustCompletionCommitManager : IAsyncCompletionCommitManager
    {
        private static readonly ImmutableArray<char> CommitCharacters = RustCompletionPresentation.CommitCharacters.ToImmutableArray();

        private readonly ITextView _view;
        private readonly RustCompletionCommitManagerProvider _provider;

        public RustCompletionCommitManager(ITextView view, RustCompletionCommitManagerProvider provider)
        {
            _view = view;
            _provider = provider;
        }

        public IEnumerable<char> PotentialCommitCharacters => CommitCharacters;

        public bool ShouldCommitCompletion(IAsyncCompletionSession session, SnapshotPoint location, char typedChar, CancellationToken token)
        {
            foreach (var language in _provider.EmbeddedLanguages)
            {
                if (language.CommitCharactersFor(session) is { } commitCharacters)
                {
                    return commitCharacters.IndexOf(typedChar) >= 0;
                }
            }

            return RustCompletionPresentation.CommitCharacters.IndexOf(typedChar) >= 0;
        }

        public CommitResult TryCommit(IAsyncCompletionSession session, ITextBuffer buffer, CompletionItem item, char typedChar, CancellationToken token)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!item.Properties.TryGetProperty(RustCompletionData.Key, out RustCompletionData data))
            {
                return CommitResult.Unhandled;
            }

            try
            {
                return Commit(session, buffer, data, typedChar, token);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OperationCanceledException)
            {
                KubunoLog.WriteLine("Completion: commit failed, inserting the name only: " + exception.Message);
                return CommitResult.Unhandled;
            }
        }

        private CommitResult Commit(IAsyncCompletionSession session, ITextBuffer buffer, RustCompletionData data, char typedChar, CancellationToken token)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            bool byCharacter = typedChar != '\0' && typedChar != '\t' && typedChar != '\n' && typedChar != '\r';
            RustCompletionSource.Dbg($"Commit '{data.DisplayText}' char=0x{(int)typedChar:X} import={data.ImportPath}");
            if (data.ImportPath != null && _view.TextBuffer == buffer)
            {
                var caret = _view.Caret.Position.BufferPosition;
                var fresh = ThreadHelper.JoinableTaskFactory.Run(() => RustCompletionSource.RefreshAsync(data, caret, token));
                if (fresh != null)
                {
                    data = fresh;
                }
            }

            if (data.NeedsResolve)
            {
                ThreadHelper.JoinableTaskFactory.Run(() => RustCompletionSource.EnsureResolvedAsync(data, token));
            }

            var item = data.Item;
            var current = buffer.CurrentSnapshot;
            var applicable = session.ApplicableToSpan.GetSpan(current);
            var eol = LineEnding(current);

            string newText;
            var target = applicable;
            if (item["textEdit"] is JObject textEdit)
            {
                newText = (string?)textEdit["newText"] ?? data.DisplayText;
                var serverSpan = RustLsp.Span(data.ServerSnapshot, textEdit["range"] ?? textEdit["replace"]);
                if (serverSpan is { } span)
                {
                    var translated = span.TranslateTo(current, SpanTrackingMode.EdgeInclusive);
                    int start = Math.Min(translated.Start.Position, applicable.Start.Position);
                    int end = Math.Max(translated.End.Position, applicable.End.Position);
                    target = new SnapshotSpan(current, start, end - start);
                }
            }
            else
            {
                newText = (string?)item["insertText"] ?? data.DisplayText;
            }

            bool snippet = (int?)item["insertTextFormat"] == 2;
            if (byCharacter && data.Category != RustCompletionCategory.Snippet)
            {
                // Like C#: `name(` or `Vec<` types the bracket itself - the item's own brackets would double it.
                newText = RustCompletionPresentation.BareName((string?)item["label"] ?? data.DisplayText);
                snippet = false;
            }

            newText = newText.Replace("\r\n", "\n").Replace("\n", eol);
            var parsed = snippet ? LspSnippet.Parse(newText) : null;
            var text = parsed?.Text ?? newText;

            var additional = new List<(Span Span, string Text)>();
            if (item["additionalTextEdits"] is JArray edits)
            {
                foreach (var edit in edits.OfType<JObject>())
                {
                    if (RustLsp.Span(data.ServerSnapshot, edit["range"]) is { } editSpan)
                    {
                        var translated = editSpan.TranslateTo(current, SpanTrackingMode.EdgeExclusive);
                        if (translated.End <= target.Start || translated.Start >= target.End)
                        {
                            additional.Add((translated.Span, ((string?)edit["newText"] ?? string.Empty).Replace("\r\n", "\n").Replace("\n", eol)));
                        }
                    }
                }
            }

            var insertionStart = current.CreateTrackingPoint(target.Start.Position, PointTrackingMode.Negative);
            using (var transaction = _provider.UndoHistoryRegistry.RegisterHistory(buffer).CreateTransaction(RustCompletionText.UndoDescription))
            {
                using (var edit = buffer.CreateEdit())
                {
                    edit.Replace(target.Span, text);
                    foreach (var (span, value) in additional)
                    {
                        edit.Replace(span, value);
                    }

                    edit.Apply();
                }

                transaction.Complete();
            }

            var after = buffer.CurrentSnapshot;
            int insertedAt = insertionStart.GetPosition(after);
            if (!ReferenceEquals(_view.TextBuffer, buffer))
            {
                return new CommitResult(true, CommitBehavior.None);
            }

            if (parsed is { HasPlaceholders: true } && !byCharacter)
            {
                RustSnippetSession.Start(_view, after, insertedAt, parsed);
            }
            else
            {
                int caret = insertedAt + (parsed?.FinalCaret ?? text.Length);
                _view.Caret.MoveTo(new SnapshotPoint(after, Math.Min(caret, after.Length)));
            }

            _view.Caret.EnsureVisible();

            if (!byCharacter && (string?)item["command"]?["command"] == RustAnalyzerHandshake.TriggerParameterHintsCommand)
            {
                TriggerParameterInfo();
            }

            return new CommitResult(true, CommitBehavior.None);
        }

        private static string LineEnding(ITextSnapshot snapshot)
        {
            for (int i = 0; i < Math.Min(snapshot.LineCount, 50); i++)
            {
                var lineBreak = snapshot.GetLineFromLineNumber(i).GetLineBreakText();
                if (lineBreak.Length > 0)
                {
                    return lineBreak;
                }
            }

            return Environment.NewLine;
        }

        /// <summary>Opens Parameter Info (Ctrl+Shift+Space) at the caret, once the commit is over.</summary>
        private static void TriggerParameterInfo()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (Package.GetGlobalService(typeof(SVsUIShell)) is IVsUIShell shell)
            {
                var group = VSConstants.VSStd2K;
                object? arg = null;
                shell.PostExecCommand(ref group, (uint)VSConstants.VSStd2KCmdID.PARAMINFO, 0, ref arg);
            }
        }
    }

    /// <summary>Undo labels of the completion commit (Edit &gt; Undo shows it).</summary>
    internal static class RustCompletionText
    {
        public static string UndoDescription => Kubuno.Core.Logic.Localization.UiLanguage.IsFrench ? "Saisie semi-automatique" : "IntelliSense";
    }
}
