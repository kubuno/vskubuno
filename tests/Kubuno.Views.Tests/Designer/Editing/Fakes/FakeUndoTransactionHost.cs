using System.Collections.Generic;
using Kubuno.Views.Designer.Editing;

namespace Kubuno.Views.Tests.Designer.Editing.Fakes
{
    /// <summary>
    /// Records every transaction it was asked to open and whether each was completed or cancelled - for
    /// asserting <see cref="CompoundEditCoordinator"/>'s undo-coalescing behavior without a real
    /// <c>ITextUndoHistory</c>. Deliberately does NOT simulate the real
    /// <c>ITextUndoTransaction.Cancel()</c>'s buffer-text rollback - only the real
    /// <see cref="Kubuno.Views.Designer.Editing.Infrastructure.DesignerUndoScope"/> does that, and
    /// it needs a live VS undo history to verify (manual Ctrl+Z check). What this fake verifies is the
    /// pure part: that <see cref="CompoundEditCoordinator"/> only calls <c>Complete()</c> when every
    /// request in the batch actually applied.
    /// </summary>
    internal sealed class FakeUndoTransactionHost : IUndoTransactionHost
    {
        public List<FakeUndoTransactionScope> Scopes { get; } = new List<FakeUndoTransactionScope>();

        public IUndoTransactionScope BeginTransaction(string description)
        {
            var scope = new FakeUndoTransactionScope(description);
            Scopes.Add(scope);
            return scope;
        }
    }

    internal sealed class FakeUndoTransactionScope : IUndoTransactionScope
    {
        public FakeUndoTransactionScope(string description)
        {
            Description = description;
        }

        public string Description { get; }

        public bool Completed { get; private set; }

        public bool Disposed { get; private set; }

        public void Complete() => Completed = true;

        public void Dispose() => Disposed = true;
    }
}
