using System;

namespace Kubuno.VisualStudio.Designer.Editing
{
    /// <summary>One open undo transaction. <see cref="Complete"/> commits it as one user-visible undo unit; <see cref="IDisposable.Dispose"/> without a prior <see cref="Complete"/> call cancels it, rolling back anything applied through it.</summary>
    public interface IUndoTransactionScope : IDisposable
    {
        void Complete();
    }
}
