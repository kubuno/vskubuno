namespace Kubuno.Desktop.Designer.Editing
{
    /// <summary>Result of <see cref="BufferEditCore.TryApply"/> / <see cref="CompoundEditCoordinator.ApplyAll"/> - never throws for an expected rejection (version mismatch, conflict); the caller decides the UX (e.g. re-request against the fresh buffer).</summary>
    public sealed class BufferEditResult
    {
        private BufferEditResult(BufferEditOutcome outcome, int? expectedVersion, int? actualVersion, string? conflictMessage)
        {
            Outcome = outcome;
            ExpectedVersion = expectedVersion;
            ActualVersion = actualVersion;
            ConflictMessage = conflictMessage;
        }

        public BufferEditOutcome Outcome { get; }

        /// <summary>Set only for <see cref="BufferEditOutcome.VersionMismatch"/>.</summary>
        public int? ExpectedVersion { get; }

        /// <summary>Set only for <see cref="BufferEditOutcome.VersionMismatch"/>.</summary>
        public int? ActualVersion { get; }

        /// <summary>Set only for <see cref="BufferEditOutcome.Conflict"/>.</summary>
        public string? ConflictMessage { get; }

        public bool Succeeded => Outcome is BufferEditOutcome.Applied or BufferEditOutcome.Empty;

        public static BufferEditResult Applied() => new BufferEditResult(BufferEditOutcome.Applied, null, null, null);

        public static BufferEditResult Empty() => new BufferEditResult(BufferEditOutcome.Empty, null, null, null);

        public static BufferEditResult VersionMismatch(int expected, int actual) =>
            new BufferEditResult(BufferEditOutcome.VersionMismatch, expected, actual, null);

        public static BufferEditResult Conflict(string message) =>
            new BufferEditResult(BufferEditOutcome.Conflict, null, null, message);
    }
}
