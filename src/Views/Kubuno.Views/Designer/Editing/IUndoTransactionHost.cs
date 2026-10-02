namespace Kubuno.Views.Designer.Editing
{
    /// <summary>
    /// The narrow seam <see cref="CompoundEditCoordinator"/> depends on instead of the real
    /// <c>Microsoft.VisualStudio.Text.Operations.ITextUndoHistory</c>, mirroring
    /// <see cref="IEditableTextBuffer"/>'s reasoning: unit-testable with a fake
    /// (tests/.../Editing/Fakes/FakeUndoTransactionHost.cs). <see cref="Infrastructure.DesignerUndoScope"/>
    /// is the real, VS-dependent implementation.
    /// </summary>
    public interface IUndoTransactionHost
    {
        IUndoTransactionScope BeginTransaction(string description);
    }
}
