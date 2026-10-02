using System;
using System.Collections.Generic;
using System.Linq;

namespace Kubuno.Shared.DevAssistant.Logic.Changes
{
    /// <summary>
    /// Every write the model proposed during one answer (docs/AI-ASSISTANT.md section 5.5): one <see cref="FileChange"/>
    /// per file, reviewed hunk by hunk, applied to the Visual Studio buffers as one undo unit. Later proposals on the same
    /// file are folded into its change (the original snapshot stays the one the first proposal was computed on).
    /// </summary>
    public sealed class ChangeSet
    {
        private readonly List<FileChange> _files = new List<FileChange>();

        public string Id { get; } = Guid.NewGuid().ToString("N");

        public IReadOnlyList<FileChange> Files => _files;

        public bool IsEmpty => _files.All(f => f.Hunks.Count == 0);

        /// <summary>Records that <paramref name="path"/> should become <paramref name="proposedText"/>; <paramref name="originalText"/> is the snapshot it was computed on (null: a new file).</summary>
        public FileChange Propose(string path, string? originalText, string proposedText)
        {
            var existing = _files.FirstOrDefault(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                existing.UpdateProposal(proposedText);
                return existing;
            }

            var change = new FileChange(path, originalText, proposedText);
            _files.Add(change);
            return change;
        }

        /// <summary>The current proposal for <paramref name="path"/> (so a second edit builds on the first), or null.</summary>
        public FileChange? Find(string path) =>
            _files.FirstOrDefault(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase));

        public int TotalAdded => _files.Sum(f => f.Hunks.Sum(h => h.Added));

        public int TotalRemoved => _files.Sum(f => f.Hunks.Sum(h => h.Removed));
    }

    /// <summary>The proposed change of one file: the snapshot, the proposal and their hunks.</summary>
    public sealed class FileChange
    {
        internal FileChange(string path, string? originalText, string proposedText)
        {
            Path = path;
            IsNewFile = originalText is null;
            OriginalText = originalText ?? string.Empty;
            UpdateProposal(proposedText);
        }

        public string Path { get; }

        public bool IsNewFile { get; }

        /// <summary>The text the proposal was computed on (empty for a new file).</summary>
        public string OriginalText { get; }

        public string ProposedText { get; private set; } = string.Empty;

        public IReadOnlyList<DiffHunk> Hunks { get; private set; } = Array.Empty<DiffHunk>();

        /// <summary>Hunk indexes the reviewer accepted (all by default).</summary>
        public ISet<int> Accepted { get; } = new HashSet<int>();

        internal void UpdateProposal(string proposedText)
        {
            ProposedText = proposedText;
            Hunks = LineDiff.Compute(OriginalText, proposedText);
            Accepted.Clear();
            foreach (var hunk in Hunks)
            {
                Accepted.Add(hunk.Index);
            }
        }
    }
}
