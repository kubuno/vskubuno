using System.Collections.Generic;
using Kubuno.Desktop.Designer.Editing;
using Kubuno.Desktop.Tests.Designer.Editing.Fakes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Desktop.Tests.Designer.Editing
{
    [TestClass]
    public class BufferEditCoreTests
    {
        private static LspRange Range(int startChar, int endChar) =>
            new LspRange(new LspPosition(0, startChar), new LspPosition(0, endChar));

        [TestMethod]
        public void TryApply_MatchingVersion_AppliesAllEditsInOneCall()
        {
            var buffer = new FakeEditableTextBuffer("hello world", initialVersion: 3);
            var request = new ApplyEditRequest(3, new[] { new TextEditDto(Range(6, 11), "there") });

            var result = BufferEditCore.TryApply(buffer, request);

            Assert.AreEqual(BufferEditOutcome.Applied, result.Outcome);
            Assert.AreEqual("hello there", buffer.GetCurrentText());
            Assert.AreEqual(1, buffer.ApplyEditsCallCount);
        }

        [TestMethod]
        public void TryApply_MismatchedVersion_RejectsWithoutTouchingBuffer()
        {
            var buffer = new FakeEditableTextBuffer("hello world", initialVersion: 5);
            var request = new ApplyEditRequest(3, new[] { new TextEditDto(Range(0, 5), "bye") });

            var result = BufferEditCore.TryApply(buffer, request);

            Assert.AreEqual(BufferEditOutcome.VersionMismatch, result.Outcome);
            Assert.AreEqual(3, result.ExpectedVersion);
            Assert.AreEqual(5, result.ActualVersion);
            Assert.AreEqual("hello world", buffer.GetCurrentText());
            Assert.AreEqual(0, buffer.ApplyEditsCallCount);
        }

        [TestMethod]
        public void TryApply_ConflictingEdits_RejectsWithoutTouchingBuffer()
        {
            var buffer = new FakeEditableTextBuffer("0123456789", initialVersion: 0);
            var request = new ApplyEditRequest(0, new[]
            {
                new TextEditDto(Range(0, 5), "A"),
                new TextEditDto(Range(3, 6), "B"),
            });

            var result = BufferEditCore.TryApply(buffer, request);

            Assert.AreEqual(BufferEditOutcome.Conflict, result.Outcome);
            Assert.IsFalse(string.IsNullOrEmpty(result.ConflictMessage));
            Assert.AreEqual(0, buffer.ApplyEditsCallCount);
        }

        [TestMethod]
        public void TryApply_NoEdits_ReturnsEmpty_WithoutVersionCheck()
        {
            var buffer = new FakeEditableTextBuffer("text", initialVersion: 42);
            var request = new ApplyEditRequest(0, new List<TextEditDto>());

            var result = BufferEditCore.TryApply(buffer, request);

            Assert.AreEqual(BufferEditOutcome.Empty, result.Outcome);
            Assert.AreEqual(0, buffer.ApplyEditsCallCount);
        }

        [TestMethod]
        public void TryApply_MultipleEdits_ApplyAsOneBufferEditCall()
        {
            var buffer = new FakeEditableTextBuffer("0123456789", initialVersion: 0);
            var request = new ApplyEditRequest(0, new[]
            {
                new TextEditDto(Range(0, 1), "A"),
                new TextEditDto(Range(9, 10), "Z"),
            });

            var result = BufferEditCore.TryApply(buffer, request);

            Assert.AreEqual(BufferEditOutcome.Applied, result.Outcome);
            Assert.AreEqual("A12345678Z", buffer.GetCurrentText());
            Assert.AreEqual(1, buffer.ApplyEditsCallCount);
        }
    }
}
