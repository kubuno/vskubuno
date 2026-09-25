namespace Kubuno.VisualStudio.Designer.Editing
{
    public enum BufferEditOutcome
    {
        /// <summary>All edits were applied as one buffer edit / one undo unit.</summary>
        Applied,

        /// <summary>The request had no edits - nothing to do, not an error.</summary>
        Empty,

        /// <summary>Rejected: the buffer's current version does not match <see cref="ApplyEditRequest.BaseVersion"/> (docs/DESIGNER.md §2's "reject if the buffer changed since the request snapshot").</summary>
        VersionMismatch,

        /// <summary>Rejected: two edits in the request overlap (see <see cref="TextEditConflictException"/>).</summary>
        Conflict,
    }
}
