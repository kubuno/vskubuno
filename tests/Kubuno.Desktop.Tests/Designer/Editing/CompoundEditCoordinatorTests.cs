using Kubuno.Desktop.Designer.Editing;
using Kubuno.Desktop.Tests.Designer.Editing.Fakes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Desktop.Tests.Designer.Editing
{
    [TestClass]
    public class CompoundEditCoordinatorTests
    {
        private static LspRange Range(int startChar, int endChar) =>
            new LspRange(new LspPosition(0, startChar), new LspPosition(0, endChar));

        [TestMethod]
        public void ApplyAll_EveryRequestSucceeds_CompletesOneTransaction()
        {
            var buffer = new FakeEditableTextBuffer("0123456789", initialVersion: 0);
            var undoHost = new FakeUndoTransactionHost();
            var coordinator = new CompoundEditCoordinator(buffer, undoHost);

            var requests = new[]
            {
                new ApplyEditRequest(0, new[] { new TextEditDto(Range(0, 1), "A") }),
                new ApplyEditRequest(1, new[] { new TextEditDto(Range(9, 10), "Z") }),
            };

            var result = coordinator.ApplyAll("Move control", requests);

            Assert.AreEqual(BufferEditOutcome.Applied, result.Outcome);
            Assert.AreEqual("A12345678Z", buffer.GetCurrentText());
            Assert.AreEqual(1, undoHost.Scopes.Count);
            Assert.AreEqual("Move control", undoHost.Scopes[0].Description);
            Assert.IsTrue(undoHost.Scopes[0].Completed);
            Assert.IsTrue(undoHost.Scopes[0].Disposed, "the using-scope must still dispose after completing");
        }

        [TestMethod]
        public void ApplyAll_SecondRequestFails_TransactionIsNotCompleted()
        {
            var buffer = new FakeEditableTextBuffer("0123456789", initialVersion: 0);
            var undoHost = new FakeUndoTransactionHost();
            var coordinator = new CompoundEditCoordinator(buffer, undoHost);

            var requests = new[]
            {
                new ApplyEditRequest(0, new[] { new TextEditDto(Range(0, 1), "A") }),
                // Stale base version (buffer is now at version 1 after the first request applied) -
                // this must be rejected, and the transaction must not be completed.
                new ApplyEditRequest(0, new[] { new TextEditDto(Range(9, 10), "Z") }),
            };

            var result = coordinator.ApplyAll("Move control", requests);

            Assert.AreEqual(BufferEditOutcome.VersionMismatch, result.Outcome);
            Assert.AreEqual(1, undoHost.Scopes.Count);
            Assert.IsFalse(undoHost.Scopes[0].Completed);
            Assert.IsTrue(undoHost.Scopes[0].Disposed, "an uncompleted scope must still be disposed (cancelled) by the using-block");

            // The first request's edit did land in the fake buffer (this fake does not simulate real
            // undo-rollback of buffer text - see FakeUndoTransactionHost's own doc comment); what this
            // test actually guards is that the transaction itself was never completed, which is what the
            // real DesignerUndoScope/ITextUndoTransaction needs to know to roll it back.
            Assert.AreEqual(1, buffer.ApplyEditsCallCount);
        }

        [TestMethod]
        public void ApplyAll_NoRequests_ReturnsEmpty_OpensNoTransaction()
        {
            var buffer = new FakeEditableTextBuffer("text", initialVersion: 0);
            var undoHost = new FakeUndoTransactionHost();
            var coordinator = new CompoundEditCoordinator(buffer, undoHost);

            var result = coordinator.ApplyAll("No-op", new ApplyEditRequest[0]);

            Assert.AreEqual(BufferEditOutcome.Empty, result.Outcome);
            Assert.AreEqual(0, undoHost.Scopes.Count);
        }

        [TestMethod]
        public void ApplyAll_SingleRequest_StillOpensAndCompletesATransaction()
        {
            var buffer = new FakeEditableTextBuffer("0123456789", initialVersion: 0);
            var undoHost = new FakeUndoTransactionHost();
            var coordinator = new CompoundEditCoordinator(buffer, undoHost);

            var result = coordinator.ApplyAll("Set attribute", new[]
            {
                new ApplyEditRequest(0, new[] { new TextEditDto(Range(0, 1), "A") }),
            });

            Assert.AreEqual(BufferEditOutcome.Applied, result.Outcome);
            Assert.AreEqual(1, undoHost.Scopes.Count);
            Assert.IsTrue(undoHost.Scopes[0].Completed);
        }
    }
}
