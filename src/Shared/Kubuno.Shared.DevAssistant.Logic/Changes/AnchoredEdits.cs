using System;
using System.Collections.Generic;
using System.Linq;
using Kubuno.Shared.DevAssistant.Logic.Secrets;

namespace Kubuno.Shared.DevAssistant.Logic.Changes
{
    /// <summary>
    /// One edit the model proposes for a file (the <c>edit_propose</c> tool): <see cref="OldText"/> must occur exactly
    /// once in the file and is replaced by <see cref="NewText"/>. Anchors, never line numbers: line numbers go stale.
    /// </summary>
    public sealed class AnchoredEdit
    {
        public string OldText { get; set; } = string.Empty;

        public string NewText { get; set; } = string.Empty;
    }

    /// <summary>A replacement of <see cref="Length"/> characters at <see cref="Start"/> of a text.</summary>
    public readonly struct TextReplacement
    {
        public TextReplacement(int start, int length, string newText)
        {
            Start = start;
            Length = length;
            NewText = newText;
        }

        public int Start { get; }

        public int Length { get; }

        public string NewText { get; }
    }

    /// <summary>The outcome of turning model edits into a proposed file text.</summary>
    public sealed class EditProposal
    {
        private EditProposal(string? text, string? error)
        {
            Text = text;
            Error = error;
        }

        /// <summary>The proposed text with the original secret values restored, or null on error.</summary>
        public string? Text { get; }

        /// <summary>Why the edits cannot be applied (sent back to the model), or null.</summary>
        public string? Error { get; }

        public bool Succeeded => Text is not null;

        public static EditProposal Success(string text) => new EditProposal(text, null);

        public static EditProposal Failure(string error) => new EditProposal(null, error);
    }

    /// <summary>Applies anchored edits to a file text (docs/AI-ASSISTANT.md section 5.5).</summary>
    public static class AnchoredEditApplier
    {
        /// <summary>
        /// Applies <paramref name="edits"/> in order to <paramref name="currentText"/> (each anchor is searched in the text
        /// produced by the previous edits). The model only ever saw masked text, so placeholders in anchors and replacements
        /// are first restored with <paramref name="masker"/>: an anchor can then match a line holding a real secret, and a
        /// replacement that keeps a placeholder keeps the real value. A placeholder the masker does not know (invented by
        /// the model) fails the whole proposal rather than land in the file.
        /// </summary>
        public static EditProposal Apply(string currentText, IReadOnlyList<AnchoredEdit> edits, SecretMasker? masker)
        {
            if (edits.Count == 0)
            {
                return EditProposal.Failure("No edit given.");
            }

            var text = currentText;
            for (int i = 0; i < edits.Count; i++)
            {
                var oldText = Restore(edits[i].OldText, masker, out var unknownOld);
                var newText = Restore(edits[i].NewText, masker, out var unknownNew);
                var unknown = unknownOld.Concat(unknownNew).Distinct().ToList();
                if (unknown.Count > 0)
                {
                    return EditProposal.Failure($"Edit {i + 1}: unknown secret placeholder(s) {string.Join(", ", unknown)}; never invent «secret:…» markers, keep the ones you were given.");
                }

                if (oldText.Length == 0)
                {
                    return EditProposal.Failure($"Edit {i + 1}: old_text is empty; give a unique anchor from the file.");
                }

                var first = text.IndexOf(oldText, StringComparison.Ordinal);
                if (first < 0)
                {
                    // Tolerate a line-ending mismatch (the model usually writes \n).
                    var normalized = ConvertLineEndings(oldText, DetectLineEnding(text));
                    first = text.IndexOf(normalized, StringComparison.Ordinal);
                    if (first >= 0)
                    {
                        oldText = normalized;
                        newText = ConvertLineEndings(newText, DetectLineEnding(text));
                    }
                }

                if (first < 0)
                {
                    return EditProposal.Failure($"Edit {i + 1}: old_text was not found in the file; read the file again and copy the anchor exactly.");
                }

                if (text.IndexOf(oldText, first + 1, StringComparison.Ordinal) >= 0)
                {
                    return EditProposal.Failure($"Edit {i + 1}: old_text occurs more than once; extend it with surrounding lines until it is unique.");
                }

                text = text.Substring(0, first) + newText + text.Substring(first + oldText.Length);
            }

            return EditProposal.Success(text);
        }

        /// <summary>Restores placeholders in a whole new file's content (same rule as <see cref="Apply"/>).</summary>
        public static EditProposal RestoreNewFile(string content, SecretMasker? masker)
        {
            var restored = Restore(content, masker, out var unknown);
            return unknown.Count > 0
                ? EditProposal.Failure($"Unknown secret placeholder(s) {string.Join(", ", unknown)}; never invent «secret:…» markers.")
                : EditProposal.Success(restored);
        }

        public static string DetectLineEnding(string text)
        {
            var index = text.IndexOf('\n');
            return index > 0 && text[index - 1] == '\r' ? "\r\n" : "\n";
        }

        public static string ConvertLineEndings(string text, string lineEnding) =>
            text.Replace("\r\n", "\n").Replace("\n", lineEnding);

        private static string Restore(string text, SecretMasker? masker, out IReadOnlyList<string> unknown)
        {
            if (masker is null)
            {
                unknown = SecretMasker.ContainsPlaceholder(text) ? new[] { "«secret:…»" } : Array.Empty<string>();
                return text;
            }

            var result = masker.Restore(text);
            unknown = result.UnknownPlaceholders;
            return result.Text;
        }
    }
}
