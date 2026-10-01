using System;
using System.Collections.Generic;
using System.Linq;

namespace Kubuno.Core.DevAssistant.Logic.Changes
{
    /// <summary>
    /// Where a change set is applied: Visual Studio's text buffers in the extension (open documents, or files opened
    /// invisibly), an in-memory fake in the tests. Writes never go behind the editor's back on disk.
    /// </summary>
    public interface IChangeSetBufferHost
    {
        /// <summary>The current text of <paramref name="path"/> (its buffer when open), or null when the file does not exist.</summary>
        string? GetCurrentText(string path);

        /// <summary>Opens ONE undo unit spanning every file of the apply (a single Ctrl+Z undoes it all); disposing it closes the unit.</summary>
        IDisposable BeginUndoUnit(string description, IReadOnlyList<string> paths);

        /// <summary>Replaces ranges of <paramref name="path"/>'s current text (non-overlapping, in the current coordinates).</summary>
        void Replace(string path, IReadOnlyList<TextReplacement> replacements);

        /// <summary>Creates a new file with <paramref name="text"/>.</summary>
        void CreateFile(string path, string text);
    }

    /// <summary>A hunk that could not be applied: the file changed since the proposal and its anchor no longer matches once.</summary>
    public sealed class HunkConflict
    {
        public HunkConflict(string path, int hunkIndex, string reason)
        {
            Path = path;
            HunkIndex = hunkIndex;
            Reason = reason;
        }

        public string Path { get; }

        public int HunkIndex { get; }

        public string Reason { get; }
    }

    /// <summary>What an apply did.</summary>
    public sealed class ApplyReport
    {
        public int AppliedHunks { get; internal set; }

        public int FilesTouched { get; internal set; }

        public List<HunkConflict> Conflicts { get; } = new List<HunkConflict>();
    }

    /// <summary>
    /// Applies the accepted hunks of a <see cref="ChangeSet"/> (docs/AI-ASSISTANT.md section 5.5). When a file is still
    /// exactly the snapshot the proposal was computed on, the hunks map directly to ranges. When it changed meanwhile,
    /// each accepted hunk is re-anchored on its old lines plus one line of context on each side; a hunk whose anchor is
    /// missing or ambiguous becomes a <see cref="HunkConflict"/> and is not applied - nothing is applied silently.
    /// </summary>
    public static class ChangeSetApplier
    {
        public static ApplyReport Apply(IChangeSetBufferHost host, ChangeSet changeSet, string description)
        {
            var report = new ApplyReport();
            var plans = new List<(FileChange File, List<TextReplacement> Replacements, string? NewFileText)>();
            foreach (var file in changeSet.Files)
            {
                var accepted = file.Hunks.Where(h => file.Accepted.Contains(h.Index)).ToList();
                if (accepted.Count == 0)
                {
                    continue;
                }

                var current = host.GetCurrentText(file.Path);
                if (file.IsNewFile)
                {
                    if (current is not null)
                    {
                        report.Conflicts.Add(new HunkConflict(file.Path, accepted[0].Index, "the file exists now"));
                        continue;
                    }

                    plans.Add((file, new List<TextReplacement>(), LineDiff.ApplySelected(file.OriginalText, file.Hunks, file.Accepted)));
                    report.AppliedHunks += accepted.Count;
                    continue;
                }

                if (current is null)
                {
                    report.Conflicts.Add(new HunkConflict(file.Path, accepted[0].Index, "the file no longer exists"));
                    continue;
                }

                var replacements = new List<TextReplacement>();
                foreach (var hunk in accepted)
                {
                    var replacement = Locate(file, hunk, current, out var reason);
                    if (replacement is null)
                    {
                        report.Conflicts.Add(new HunkConflict(file.Path, hunk.Index, reason));
                        continue;
                    }

                    if (replacements.Any(r => Overlaps(r, replacement.Value)))
                    {
                        report.Conflicts.Add(new HunkConflict(file.Path, hunk.Index, "overlaps another hunk"));
                        continue;
                    }

                    replacements.Add(replacement.Value);
                    report.AppliedHunks++;
                }

                if (replacements.Count > 0)
                {
                    plans.Add((file, replacements.OrderBy(r => r.Start).ToList(), null));
                }
            }

            if (plans.Count == 0)
            {
                return report;
            }

            using (host.BeginUndoUnit(description, plans.Select(p => p.File.Path).ToList()))
            {
                foreach (var (file, replacements, newFileText) in plans)
                {
                    if (newFileText is not null)
                    {
                        host.CreateFile(file.Path, newFileText);
                    }
                    else
                    {
                        host.Replace(file.Path, replacements);
                    }

                    report.FilesTouched++;
                }
            }

            return report;
        }

        private static bool Overlaps(TextReplacement a, TextReplacement b) =>
            a.Start < b.Start + b.Length && b.Start < a.Start + a.Length || (a.Length == 0 && b.Length == 0 && a.Start == b.Start);

        /// <summary>The range of <paramref name="current"/> the hunk replaces, or null with the conflict reason.</summary>
        private static TextReplacement? Locate(FileChange file, DiffHunk hunk, string current, out string reason)
        {
            reason = string.Empty;
            var oldLines = LineDiff.SplitLines(file.OriginalText);
            var newText = string.Concat(hunk.NewLines);
            if (string.Equals(current, file.OriginalText, StringComparison.Ordinal))
            {
                int start = Offset(oldLines, hunk.OldStart);
                int end = Offset(oldLines, hunk.OldStart + hunk.OldCount);
                return new TextReplacement(start, end - start, newText);
            }

            // Re-anchor: old lines with one line of context on each side, found exactly once in the current text.
            int before = hunk.OldStart > 0 ? 1 : 0;
            int after = hunk.OldStart + hunk.OldCount < oldLines.Count ? 1 : 0;
            var prefix = before == 1 ? oldLines[hunk.OldStart - 1] : string.Empty;
            var body = string.Concat(hunk.OldLines);
            var suffix = after == 1 ? oldLines[hunk.OldStart + hunk.OldCount] : string.Empty;
            var anchor = prefix + body + suffix;
            if (anchor.Length == 0)
            {
                reason = "empty anchor";
                return null;
            }

            int index = current.IndexOf(anchor, StringComparison.Ordinal);
            if (index < 0)
            {
                reason = "the lines around this change were modified since the proposal";
                return null;
            }

            if (current.IndexOf(anchor, index + 1, StringComparison.Ordinal) >= 0)
            {
                reason = "the lines around this change now occur more than once";
                return null;
            }

            return new TextReplacement(index + prefix.Length, body.Length, newText);
        }

        private static int Offset(IReadOnlyList<string> lines, int lineIndex)
        {
            int offset = 0;
            for (int i = 0; i < lineIndex && i < lines.Count; i++)
            {
                offset += lines[i].Length;
            }

            return offset;
        }
    }
}
