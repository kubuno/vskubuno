using System;
using System.Collections.Generic;
using System.Linq;
using Kubuno.VisualStudio.Designer.Editing;

namespace Kubuno.VisualStudio.Designer.Handlers
{
    /// <summary>
    /// Where <see cref="WorkspaceEditApplier"/> writes: the buffer of an editor that has the file open (its edits
    /// then land as one undo unit, and the user saves them like any edit), else the file itself.
    /// <see cref="Infrastructure.VsWorkspaceFileHost"/> is the real one (the running document table); faked in tests.
    /// </summary>
    public interface IWorkspaceFileHost
    {
        /// <summary>The buffer of an open document for <paramref name="fileUri"/>, or null when no editor has it open.</summary>
        IEditableTextBuffer? TryGetOpenBuffer(string fileUri);

        /// <summary>The file's text on disk, or null when it cannot be read.</summary>
        string? ReadFile(string fileUri);

        void WriteFile(string fileUri, string text);
    }

    /// <summary>The outcome of <see cref="WorkspaceEditApplier.Apply"/>.</summary>
    public sealed class WorkspaceEditResult
    {
        public WorkspaceEditResult(IReadOnlyList<string> appliedFiles, string? failedFile, string? failure)
        {
            AppliedFiles = appliedFiles;
            FailedFile = failedFile;
            Failure = failure;
        }

        public IReadOnlyList<string> AppliedFiles { get; }

        public string? FailedFile { get; }

        public string? Failure { get; }

        public bool Succeeded => FailedFile is null;
    }

    /// <summary>
    /// Applies a language-server <c>WorkspaceEdit</c> (a handler rename, removal, conversion - docs/EVENTS.md §5.5,
    /// EVT-5) file by file: each file's edits are ONE buffer edit (one undo unit per file, like DSG-10's
    /// <see cref="HandlerCreationService"/>), through the open editor when there is one - the server computed the
    /// edits against that buffer's text, sent as <c>openFiles</c> - else spliced into the file on disk. Every file
    /// is checked first (its edits must not overlap and must fit its text), so a bad edit applies nothing.
    /// </summary>
    public static class WorkspaceEditApplier
    {
        public static WorkspaceEditResult Apply(HandlerWorkspaceEdit? edit, IWorkspaceFileHost host)
        {
            if (host is null)
            {
                throw new ArgumentNullException(nameof(host));
            }

            var changes = edit?.Changes;
            if (changes is null || changes.Count == 0)
            {
                return new WorkspaceEditResult(Array.Empty<string>(), null, null);
            }

            // Plan everything before touching anything.
            var plans = new List<(string Uri, IEditableTextBuffer? Buffer, string Text, IReadOnlyList<TextEditDto> Edits)>();
            foreach (var uri in changes.Keys.OrderBy(u => u, StringComparer.Ordinal))
            {
                var edits = changes[uri];
                if (edits.Count == 0)
                {
                    continue;
                }

                var buffer = host.TryGetOpenBuffer(uri);
                var text = buffer?.GetCurrentText() ?? host.ReadFile(uri);
                if (text is null)
                {
                    return new WorkspaceEditResult(Array.Empty<string>(), uri, "the file could not be read");
                }

                try
                {
                    TextEditPlanner.Plan(text, edits);
                }
                catch (Exception ex) when (ex is TextEditConflictException or ArgumentException)
                {
                    return new WorkspaceEditResult(Array.Empty<string>(), uri, ex.Message);
                }

                plans.Add((uri, buffer, text, edits));
            }

            var applied = new List<string>();
            foreach (var (uri, buffer, text, edits) in plans)
            {
                if (buffer is not null)
                {
                    var result = BufferEditCore.TryApply(buffer, new ApplyEditRequest(buffer.CurrentVersion, edits));
                    if (!result.Succeeded)
                    {
                        return new WorkspaceEditResult(applied, uri, result.Outcome.ToString());
                    }
                }
                else
                {
                    host.WriteFile(uri, TextEditPlanner.ApplyToPlainText(text, edits));
                }

                applied.Add(uri);
            }

            return new WorkspaceEditResult(applied, null, null);
        }
    }
}
