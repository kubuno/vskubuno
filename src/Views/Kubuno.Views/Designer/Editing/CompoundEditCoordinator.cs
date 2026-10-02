using System;
using System.Collections.Generic;

namespace Kubuno.Views.Designer.Editing
{
    /// <summary>
    /// Coalesces a gesture that needs more than one <c>kubuno/applyEdit</c> round trip into ONE
    /// user-visible undo unit (this task's "coalescing of a drag gesture into a single undo transaction
    /// (ITextUndoHistory / linked undo)"). A single edit is already one undo unit for free the moment
    /// <see cref="BufferEditCore"/> applies it (one <c>ITextEdit</c> = one undo primitive), so this class
    /// only matters when a gesture legitimately produces several separate requests - e.g. a
    /// <c>MoveChild</c> the language server decomposes into a remove splice followed by a re-computed
    /// insert splice against the post-remove tree, because its buffer-synced rowan tree (docs/DESIGNER.md
    /// §3) only reflects the first edit once it has actually been applied, so the second request cannot
    /// be computed until the first one lands. Opening the transaction first, applying every request
    /// through it, and completing it only once all of them succeed links their individual undo
    /// primitives into one <c>Ctrl+Z</c> step; any failure (version mismatch or conflict) leaves the
    /// transaction uncompleted, so disposing it cancels - rolling back whatever was already applied in
    /// this batch.
    ///
    /// Pure orchestration over <see cref="IEditableTextBuffer"/>/<see cref="IUndoTransactionHost"/> - unit
    /// -tested with fakes, no VS needed (tests/.../Editing/CompoundEditCoordinatorTests.cs).
    /// </summary>
    public sealed class CompoundEditCoordinator
    {
        private readonly IEditableTextBuffer _buffer;
        private readonly IUndoTransactionHost _undoHost;

        public CompoundEditCoordinator(IEditableTextBuffer buffer, IUndoTransactionHost undoHost)
        {
            _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
            _undoHost = undoHost ?? throw new ArgumentNullException(nameof(undoHost));
        }

        public BufferEditResult ApplyAll(string description, IReadOnlyList<ApplyEditRequest> requests)
        {
            if (description is null)
            {
                throw new ArgumentNullException(nameof(description));
            }

            if (requests is null)
            {
                throw new ArgumentNullException(nameof(requests));
            }

            if (requests.Count == 0)
            {
                return BufferEditResult.Empty();
            }

            using var scope = _undoHost.BeginTransaction(description);

            foreach (var request in requests)
            {
                var result = BufferEditCore.TryApply(_buffer, request);
                if (!result.Succeeded)
                {
                    // Not completing the scope cancels it on Dispose, rolling back any prior requests
                    // already applied within this batch.
                    return result;
                }
            }

            scope.Complete();
            return BufferEditResult.Applied();
        }
    }
}
