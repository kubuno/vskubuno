using Kubuno.VisualStudio.Designer.DesignSurface;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.VisualStudio.Designer.Tests.DesignSurface
{
    /// <summary>
    /// DSG-6: <see cref="DesignSurfaceProtocol"/> is the pure encode/parse half of the design-surface
    /// IPC protocol (`vskubuno/docs/DESIGNER.md`'s "DSG-6 protocol" section) - no live process, no
    /// event, no WPF <c>Dispatcher</c>, so every wire shape is checked here directly against the exact
    /// strings `kubuno-views/src/protocol.rs`'s own tests assert on the Rust side
    /// (`kubuno-views/src/protocol.rs`'s `tests` module), so the two sides cannot silently drift.
    /// </summary>
    [TestClass]
    public class RustDesignSurfaceHostProtocolTests
    {
        // ── Encoding (host → surface) ───────────────────────────────────

        [TestMethod]
        public void EncodeSetText_MatchesTheRustWireShape()
        {
            Assert.AreEqual(@"{""type"":""setText"",""text"":""<Button/>""}", DesignSurfaceProtocol.EncodeSetText("<Button/>"));
        }

        [TestMethod]
        public void EncodeSetDesignMode_MatchesTheRustWireShape()
        {
            Assert.AreEqual(@"{""type"":""setDesignMode"",""on"":true}", DesignSurfaceProtocol.EncodeSetDesignMode(true));
            Assert.AreEqual(@"{""type"":""setDesignMode"",""on"":false}", DesignSurfaceProtocol.EncodeSetDesignMode(false));
        }

        [TestMethod]
        public void EncodeSelect_WithAnIdMatchesTheRustWireShape()
        {
            Assert.AreEqual(@"{""type"":""select"",""id"":""0.1""}", DesignSurfaceProtocol.EncodeSelect("0.1"));
        }

        // ── docs/DESIGNER.md §13: multi-selection and Layout commands ────

        [TestMethod]
        public void EncodeSelectManyAndFormat_MatchTheRustWireShapes()
        {
            Assert.AreEqual(@"{""type"":""selectMany"",""ids"":[""0"",""1""],""primary"":""1""}", DesignSurfaceProtocol.EncodeSelectMany(new[] { "0", "1" }, "1"));
            Assert.AreEqual(@"{""type"":""format"",""command"":""horizontalSpacingEqual""}", DesignSurfaceProtocol.EncodeFormat("horizontalSpacingEqual"));
        }

        [TestMethod]
        public void TryParseSelectionChanged_PutsThePrimaryFirstThenTheRestOfTheSelection()
        {
            Assert.IsTrue(DesignSurfaceProtocol.TryParseSelectionChanged(
                @"{""type"":""selectionChanged"",""id"":""0.1"",""bounds"":null,""ids"":[""0.0"",""0.1"",""0.2""]}", out var ids));
            CollectionAssert.AreEqual(new[] { "0.1", "0.0", "0.2" }, System.Linq.Enumerable.ToArray(ids));

            Assert.IsTrue(DesignSurfaceProtocol.TryParseSelectionChanged(@"{""type"":""selectionChanged"",""id"":null,""bounds"":null,""ids"":[]}", out var none));
            Assert.AreEqual(0, none.Count);
        }

        [TestMethod]
        public void EncodeSelect_WithNoIdSerializesANullNotAMissingField()
        {
            Assert.AreEqual(@"{""type"":""select"",""id"":null}", DesignSurfaceProtocol.EncodeSelect(null));
        }

        // ── Parsing selectionChanged (surface → host) ───────────────────

        [TestMethod]
        public void TryParseSelectionChanged_WithAnId_ReturnsASingleElementList()
        {
            var ok = DesignSurfaceProtocol.TryParseSelectionChanged(
                @"{""type"":""selectionChanged"",""id"":""0.1"",""bounds"":{""left"":1.0,""top"":2.0,""right"":3.0,""bottom"":4.0}}",
                out var ids);

            Assert.IsTrue(ok);
            CollectionAssert.AreEqual(new[] { "0.1" }, (System.Collections.ICollection)ids);
        }

        [TestMethod]
        public void TryParseSelectionChanged_WithANullId_ReturnsAnEmptyListButStillParses()
        {
            var ok = DesignSurfaceProtocol.TryParseSelectionChanged(@"{""type"":""selectionChanged"",""id"":null,""bounds"":null}", out var ids);

            Assert.IsTrue(ok);
            Assert.AreEqual(0, ids.Count);
        }

        [TestMethod]
        public void TryParseSelectionChanged_IgnoresBoundsAndOnlyReadsId()
        {
            // `bounds` is present on the wire (DESIGNER.md) but `DesignSurfaceSelectionChangedEventArgs`
            // only ever carries ids - a missing/extra `bounds` field must not affect parsing either way.
            var ok = DesignSurfaceProtocol.TryParseSelectionChanged(@"{""type"":""selectionChanged"",""id"":""0""}", out var ids);

            Assert.IsTrue(ok);
            CollectionAssert.AreEqual(new[] { "0" }, (System.Collections.ICollection)ids);
        }

        [TestMethod]
        public void TryParseSelectionChanged_RejectsWrongTypeBlankAndGarbageLines()
        {
            Assert.IsFalse(DesignSurfaceProtocol.TryParseSelectionChanged(@"{""type"":""editRequest""}", out _));
            Assert.IsFalse(DesignSurfaceProtocol.TryParseSelectionChanged("", out _));
            Assert.IsFalse(DesignSurfaceProtocol.TryParseSelectionChanged("   ", out _));
            Assert.IsFalse(DesignSurfaceProtocol.TryParseSelectionChanged("not json at all", out _));
            Assert.IsFalse(DesignSurfaceProtocol.TryParseSelectionChanged(@"{""type"":""selectionChanged"" this is not valid json", out _));
        }

        // ── Parsing editRequest (surface → host) ────────────────────────

        [TestMethod]
        public void TryParseEditRequest_SetAttribute_MatchesTheDsg2ApplyEditOpShape()
        {
            var ok = DesignSurfaceProtocol.TryParseEditRequest(
                @"{""type"":""editRequest"",""op"":{""kind"":""setAttribute"",""elementId"":""0.1"",""name"":""X"",""value"":""42""}}",
                out var op);

            Assert.IsTrue(ok);
            Assert.IsNotNull(op);
            Assert.AreEqual(DesignSurfaceEditOpKind.SetAttribute, op!.Kind);
            Assert.AreEqual("0.1", op.ElementId);
            Assert.AreEqual("X", op.Name);
            Assert.AreEqual("42", op.Value);
        }

        [TestMethod]
        public void TryParseEditRequest_RemoveElement_MatchesTheDsg2ApplyEditOpShape()
        {
            var ok = DesignSurfaceProtocol.TryParseEditRequest(
                @"{""type"":""editRequest"",""op"":{""kind"":""removeElement"",""elementId"":""0.1""}}",
                out var op);

            Assert.IsTrue(ok);
            Assert.IsNotNull(op);
            Assert.AreEqual(DesignSurfaceEditOpKind.RemoveElement, op!.Kind);
            Assert.AreEqual("0.1", op.ElementId);
            Assert.IsNull(op.Name);
            Assert.IsNull(op.Value);
        }

        [TestMethod]
        public void TryParseEditRequest_UnknownOpKindIsRejected()
        {
            var ok = DesignSurfaceProtocol.TryParseEditRequest(
                @"{""type"":""editRequest"",""op"":{""kind"":""somethingElse"",""elementId"":""0""}}",
                out var op);

            Assert.IsFalse(ok);
            Assert.IsNull(op);
        }

        [TestMethod]
        public void TryParseEditRequest_SetAttributeMissingValueIsRejected()
        {
            var ok = DesignSurfaceProtocol.TryParseEditRequest(
                @"{""type"":""editRequest"",""op"":{""kind"":""setAttribute"",""elementId"":""0"",""name"":""X""}}",
                out var op);

            Assert.IsFalse(ok);
            Assert.IsNull(op);
        }

        [TestMethod]
        public void TryParseEditRequest_RejectsWrongTypeBlankAndGarbageLines()
        {
            Assert.IsFalse(DesignSurfaceProtocol.TryParseEditRequest(@"{""type"":""selectionChanged""}", out _));
            Assert.IsFalse(DesignSurfaceProtocol.TryParseEditRequest("", out _));
            Assert.IsFalse(DesignSurfaceProtocol.TryParseEditRequest("not json at all", out _));
        }

        [TestMethod]
        public void TryParseEditRequest_MissingElementIdIsRejected()
        {
            var ok = DesignSurfaceProtocol.TryParseEditRequest(
                @"{""type"":""editRequest"",""op"":{""kind"":""removeElement""}}",
                out var op);

            Assert.IsFalse(ok);
            Assert.IsNull(op);
        }

        // ── DesignSurfaceEditOp itself ───────────────────────────────────

        [TestMethod]
        public void DesignSurfaceEditOp_RejectsANullElementId()
        {
            Assert.ThrowsExactly<System.ArgumentNullException>(() => new DesignSurfaceEditOp(DesignSurfaceEditOpKind.RemoveElement, null!));
        }

        [TestMethod]
        public void DesignSurfaceEditRequestedEventArgs_RejectsANullOp()
        {
            Assert.ThrowsExactly<System.ArgumentNullException>(() => new DesignSurfaceEditRequestedEventArgs(null!));
        }
    }
}
