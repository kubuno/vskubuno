using Kubuno.VisualStudio.Designer.Editing;

namespace Kubuno.VisualStudio.Designer.Handlers
{
    public enum HandlerCreationOutcome
    {
        /// <summary>A new handler was created: both files' edits applied, the code-behind opened at the new <c>fn</c>.</summary>
        Created,

        /// <summary>The event already named a handler - no edit; just navigated to its existing definition.</summary>
        OpenedExisting,

        /// <summary>The server returned neither a location nor an edit (docs/DESIGNER.md's own "degrade to no-op" convention: an unresolved element id, an undeclared event, no code-behind file could be identified...).</summary>
        NothingToDo,

        /// <summary>The RPC call itself failed or returned nothing this library could parse (server not running, malformed response).</summary>
        RequestFailed,

        /// <summary>The edit was computed but could not be applied to one of the buffers (a stale version, an overlapping edit) - see <see cref="HandlerCreationResult.FailedFile"/>/<see cref="HandlerCreationResult.FailedOutcome"/>.</summary>
        ApplyFailed,
    }

    /// <summary>The result of one <see cref="HandlerCreationService.CreateAsync"/> call.</summary>
    public sealed class HandlerCreationResult
    {
        private HandlerCreationResult(HandlerCreationOutcome outcome, string? handlerName, string? failedFile, BufferEditOutcome? failedOutcome)
        {
            Outcome = outcome;
            HandlerName = handlerName;
            FailedFile = failedFile;
            FailedOutcome = failedOutcome;
        }

        public HandlerCreationOutcome Outcome { get; }

        public string? HandlerName { get; }

        /// <summary>Set only for <see cref="HandlerCreationOutcome.ApplyFailed"/>: which file's edit failed to apply.</summary>
        public string? FailedFile { get; }

        /// <summary>Set only for <see cref="HandlerCreationOutcome.ApplyFailed"/>: the underlying <see cref="BufferEditCore"/> rejection.</summary>
        public BufferEditOutcome? FailedOutcome { get; }

        public static HandlerCreationResult Created(string handlerName) => new HandlerCreationResult(HandlerCreationOutcome.Created, handlerName, null, null);

        public static HandlerCreationResult OpenedExisting(string handlerName) => new HandlerCreationResult(HandlerCreationOutcome.OpenedExisting, handlerName, null, null);

        public static HandlerCreationResult NothingToDo() => new HandlerCreationResult(HandlerCreationOutcome.NothingToDo, null, null, null);

        public static HandlerCreationResult RequestFailed() => new HandlerCreationResult(HandlerCreationOutcome.RequestFailed, null, null, null);

        public static HandlerCreationResult ApplyFailed(string handlerName, string failedFile, BufferEditOutcome outcome) =>
            new HandlerCreationResult(HandlerCreationOutcome.ApplyFailed, handlerName, failedFile, outcome);
    }
}
