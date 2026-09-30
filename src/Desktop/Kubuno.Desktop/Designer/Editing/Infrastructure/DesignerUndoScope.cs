using System;
using Microsoft.VisualStudio.Text.Operations;

namespace Kubuno.VisualStudio.Designer.Editing.Infrastructure
{
    /// <summary>
    /// The real <c>ITextUndoHistory</c>-backed <see cref="IUndoTransactionHost"/> (docs/DESIGNER.md §2:
    /// "wrapped in one IOleUndoManager compound action (or ITextUndoHistory transaction) so Ctrl+Z undoes
    /// the whole drag in one step"). See <see cref="CompoundEditCoordinator"/>'s own doc comment for why
    /// a gesture may need this at all instead of relying on the free one-undo-unit-per-edit behavior
    /// <see cref="BufferEditApplier"/> already gives a single request.
    ///
    /// Not unit-tested here - needs a live <c>ITextUndoHistory</c> (registered per-buffer by VS's editor
    /// host, typically via <c>ITextUndoHistoryRegistry</c>); see
    /// tests/.../Editing/CompoundEditCoordinatorTests.cs for the fake-backed coverage of the
    /// orchestration logic this wraps, and this library's own test-strategy note in
    /// Kubuno.VisualStudio.Designer.Tests.csproj for the general pattern.
    /// </summary>
    public sealed class DesignerUndoScope : IUndoTransactionHost
    {
        private readonly ITextUndoHistory _history;

        public DesignerUndoScope(ITextUndoHistory history)
        {
            _history = history ?? throw new ArgumentNullException(nameof(history));
        }

        public IUndoTransactionScope BeginTransaction(string description) => new Scope(_history.CreateTransaction(description));

        private sealed class Scope : IUndoTransactionScope
        {
            private readonly ITextUndoTransaction _transaction;
            private bool _finished;

            public Scope(ITextUndoTransaction transaction)
            {
                _transaction = transaction;
            }

            public void Complete()
            {
                if (_finished)
                {
                    return;
                }

                _transaction.Complete();
                _finished = true;
            }

            public void Dispose()
            {
                if (_finished)
                {
                    return;
                }

                _transaction.Cancel();
                _finished = true;
            }
        }
    }
}
