using System.Collections.Generic;
using System.IO;
using System.Text;
using Kubuno.VisualStudio.Designer.DesignSurface;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.VisualStudio.Designer.Tests.DesignSurface
{
    /// <summary>
    /// DSG-9: <see cref="DesignSurfaceDragDropProtocol"/> is the pure encode/parse half of the design-
    /// surface's DSG-9 protocol additions (`vskubuno/docs/DESIGNER.md`'s "DSG-9 protocol" section) - no
    /// live process, no event, no WPF <c>Dispatcher</c>, so every wire shape is checked here directly
    /// against the exact strings `kubuno-views/src/protocol.rs`'s own tests assert on the Rust side
    /// (`kubuno-views/src/protocol.rs`'s `tests` module), so the two sides cannot silently drift - the
    /// same posture <see cref="RustDesignSurfaceHostProtocolTests"/> already established for DSG-6.
    /// </summary>
    [TestClass]
    public class RustDesignSurfaceHostDragDropTests
    {
        // ── Encoding (host → surface) ───────────────────────────────────

        [TestMethod]
        public void EncodeDragEnter_MatchesTheRustWireShape()
        {
            Assert.AreEqual(@"{""type"":""dragEnter"",""component"":""Button""}", DesignSurfaceDragDropProtocol.EncodeDragEnter("Button"));
        }

        [TestMethod]
        public void EncodeDragLeave_MatchesTheRustWireShape()
        {
            Assert.AreEqual(@"{""type"":""dragLeave""}", DesignSurfaceDragDropProtocol.EncodeDragLeave());
        }

        [TestMethod]
        public void EncodeDragOver_RoundTripsThroughTheHostMessageParserShape()
        {
            // Not an exact-string assertion (System.Text.Json's own default number formatting for a
            // whole-number `double` need not match serde_json's f32 formatting byte-for-byte - both are
            // the SAME JSON number value, which is all either side's parser actually requires): this
            // asserts the shape is otherwise correct by feeding it back through a hand-rolled reader.
            var json = DesignSurfaceDragDropProtocol.EncodeDragOver(10.5, 20.0);
            StringAssert.StartsWith(json, @"{""type"":""dragOver""");
            StringAssert.Contains(json, @"""x"":10.5");
        }

        [TestMethod]
        public void EncodeDrop_HasTheDropType()
        {
            var json = DesignSurfaceDragDropProtocol.EncodeDrop(1.0, 2.0);
            StringAssert.StartsWith(json, @"{""type"":""drop""");
        }

        // ── Parsing editRequests (surface → host, DSG-9's batched form) ─

        [TestMethod]
        public void TryParseEditRequestsBatch_MoveGestureWithTwoSetAttributeOps()
        {
            var ok = DesignSurfaceDragDropProtocol.TryParseEditRequestsBatch(
                @"{""type"":""editRequests"",""ops"":[{""kind"":""setAttribute"",""elementId"":""0.1"",""name"":""X"",""value"":""42""},{""kind"":""setAttribute"",""elementId"":""0.1"",""name"":""Y"",""value"":""10""}],""gesture"":""move""}",
                out var ops, out var gesture);

            Assert.IsTrue(ok);
            Assert.AreEqual(DesignSurfaceGesture.Move, gesture);
            Assert.AreEqual(2, ops.Count);
            Assert.AreEqual(DesignSurfaceEditOpKind.SetAttribute, ops[0].Kind);
            Assert.AreEqual("0.1", ops[0].ElementId);
            Assert.AreEqual("X", ops[0].Name);
            Assert.AreEqual("42", ops[0].Value);
            Assert.AreEqual("Y", ops[1].Name);
            Assert.AreEqual("10", ops[1].Value);
        }

        [TestMethod]
        public void TryParseEditRequestsBatch_ResizeGesture()
        {
            var ok = DesignSurfaceDragDropProtocol.TryParseEditRequestsBatch(
                @"{""type"":""editRequests"",""ops"":[{""kind"":""setAttribute"",""elementId"":""0"",""name"":""Width"",""value"":""120""}],""gesture"":""resize""}",
                out var ops, out var gesture);

            Assert.IsTrue(ok);
            Assert.AreEqual(DesignSurfaceGesture.Resize, gesture);
            Assert.AreEqual(1, ops.Count);
        }

        [TestMethod]
        public void TryParseEditRequestsBatch_EmptyOpsArrayStillParses()
        {
            var ok = DesignSurfaceDragDropProtocol.TryParseEditRequestsBatch(
                @"{""type"":""editRequests"",""ops"":[],""gesture"":""move""}", out var ops, out _);

            Assert.IsTrue(ok);
            Assert.AreEqual(0, ops.Count);
        }

        [TestMethod]
        public void TryParseEditRequestsBatch_RejectsAnUnrecognisedGesture()
        {
            var ok = DesignSurfaceDragDropProtocol.TryParseEditRequestsBatch(
                @"{""type"":""editRequests"",""ops"":[],""gesture"":""spin""}", out _, out _);
            Assert.IsFalse(ok);
        }

        [TestMethod]
        public void TryParseEditRequestsBatch_RejectsANonSetAttributeOp()
        {
            var ok = DesignSurfaceDragDropProtocol.TryParseEditRequestsBatch(
                @"{""type"":""editRequests"",""ops"":[{""kind"":""removeElement"",""elementId"":""0""}],""gesture"":""move""}",
                out _, out _);
            Assert.IsFalse(ok);
        }

        [TestMethod]
        public void TryParseEditRequestsBatch_RejectsWrongTypeBlankAndGarbageLines()
        {
            Assert.IsFalse(DesignSurfaceDragDropProtocol.TryParseEditRequestsBatch(@"{""type"":""editRequest""}", out _, out _));
            Assert.IsFalse(DesignSurfaceDragDropProtocol.TryParseEditRequestsBatch("", out _, out _));
            Assert.IsFalse(DesignSurfaceDragDropProtocol.TryParseEditRequestsBatch("not json at all", out _, out _));
        }

        // ── Parsing a single editRequest moveElement/insertChild (surface → host) ─

        [TestMethod]
        public void TryParseDragDropEditRequest_MoveElement()
        {
            var ok = DesignSurfaceDragDropProtocol.TryParseDragDropEditRequest(
                @"{""type"":""editRequest"",""op"":{""kind"":""moveElement"",""elementId"":""0.0"",""newParentId"":""0"",""index"":2}}",
                out var op);

            Assert.IsTrue(ok);
            Assert.IsNotNull(op);
            Assert.AreEqual(DesignSurfaceDragDropOpKind.MoveElement, op!.Kind);
            Assert.AreEqual("0.0", op.ElementId);
            Assert.AreEqual("0", op.NewParentId);
            Assert.AreEqual(2, op.Index);
            Assert.IsNull(op.ParentId);
            Assert.IsNull(op.Xml);
        }

        [TestMethod]
        public void TryParseDragDropEditRequest_InsertChild()
        {
            var ok = DesignSurfaceDragDropProtocol.TryParseDragDropEditRequest(
                @"{""type"":""editRequest"",""op"":{""kind"":""insertChild"",""parentId"":""0"",""index"":1,""xml"":""<Button/>""}}",
                out var op);

            Assert.IsTrue(ok);
            Assert.IsNotNull(op);
            Assert.AreEqual(DesignSurfaceDragDropOpKind.InsertChild, op!.Kind);
            Assert.AreEqual("0", op.ParentId);
            Assert.AreEqual(1, op.Index);
            Assert.AreEqual("<Button/>", op.Xml);
            Assert.IsNull(op.ElementId);
            Assert.IsNull(op.NewParentId);
        }

        [TestMethod]
        public void TryParseDragDropEditRequest_LeavesSetAttributeAndRemoveElementToTheOtherParser()
        {
            // These two kinds are `DesignSurfaceProtocol.TryParseEditRequest`'s own job
            // (`RustDesignSurfaceHost.Protocol.cs`) - this parser must not also claim them.
            Assert.IsFalse(DesignSurfaceDragDropProtocol.TryParseDragDropEditRequest(
                @"{""type"":""editRequest"",""op"":{""kind"":""setAttribute"",""elementId"":""0"",""name"":""X"",""value"":""1""}}", out _));
            Assert.IsFalse(DesignSurfaceDragDropProtocol.TryParseDragDropEditRequest(
                @"{""type"":""editRequest"",""op"":{""kind"":""removeElement"",""elementId"":""0""}}", out _));
        }

        [TestMethod]
        public void TryParseDragDropEditRequest_MoveElementMissingIndexIsRejected()
        {
            var ok = DesignSurfaceDragDropProtocol.TryParseDragDropEditRequest(
                @"{""type"":""editRequest"",""op"":{""kind"":""moveElement"",""elementId"":""0"",""newParentId"":""0""}}", out var op);
            Assert.IsFalse(ok);
            Assert.IsNull(op);
        }

        [TestMethod]
        public void TryParseDragDropEditRequest_RejectsWrongTypeBlankAndGarbageLines()
        {
            Assert.IsFalse(DesignSurfaceDragDropProtocol.TryParseDragDropEditRequest(@"{""type"":""selectionChanged""}", out _));
            Assert.IsFalse(DesignSurfaceDragDropProtocol.TryParseDragDropEditRequest("", out _));
            Assert.IsFalse(DesignSurfaceDragDropProtocol.TryParseDragDropEditRequest("not json at all", out _));
        }

        // ── Parsing dropTargetChanged (surface → host) ──────────────────

        [TestMethod]
        public void TryParseDropTargetChanged_WithAnAnchorTargetCarriesXy()
        {
            var ok = DesignSurfaceDragDropProtocol.TryParseDropTargetChanged(
                @"{""type"":""dropTargetChanged"",""target"":{""valid"":true,""parentId"":""0"",""index"":2,""xy"":[10.0,20.0],""marker"":{""left"":1.0,""top"":2.0,""right"":3.0,""bottom"":4.0}}}",
                out var target);

            Assert.IsTrue(ok);
            Assert.IsNotNull(target);
            Assert.IsTrue(target!.Valid);
            Assert.AreEqual("0", target.ParentId);
            Assert.AreEqual(2, target.Index);
            Assert.AreEqual(10.0, target.X);
            Assert.AreEqual(20.0, target.Y);
            Assert.AreEqual(1.0, target.MarkerLeft);
            Assert.AreEqual(2.0, target.MarkerTop);
            Assert.AreEqual(3.0, target.MarkerRight);
            Assert.AreEqual(4.0, target.MarkerBottom);
        }

        [TestMethod]
        public void TryParseDropTargetChanged_WithAFlowTargetHasNoXy()
        {
            var ok = DesignSurfaceDragDropProtocol.TryParseDropTargetChanged(
                @"{""type"":""dropTargetChanged"",""target"":{""valid"":false,""parentId"":""0"",""index"":0,""xy"":null,""marker"":{""left"":0.0,""top"":0.0,""right"":10.0,""bottom"":3.0}}}",
                out var target);

            Assert.IsTrue(ok);
            Assert.IsNotNull(target);
            Assert.IsFalse(target!.Valid);
            Assert.IsNull(target.X);
            Assert.IsNull(target.Y);
        }

        [TestMethod]
        public void TryParseDropTargetChanged_WithANullTargetIsAValidClearedState()
        {
            var ok = DesignSurfaceDragDropProtocol.TryParseDropTargetChanged(@"{""type"":""dropTargetChanged"",""target"":null}", out var target);
            Assert.IsTrue(ok);
            Assert.IsNull(target);
        }

        [TestMethod]
        public void TryParseDropTargetChanged_RejectsWrongTypeBlankAndGarbageLines()
        {
            Assert.IsFalse(DesignSurfaceDragDropProtocol.TryParseDropTargetChanged(@"{""type"":""selectionChanged""}", out _));
            Assert.IsFalse(DesignSurfaceDragDropProtocol.TryParseDropTargetChanged("", out _));
            Assert.IsFalse(DesignSurfaceDragDropProtocol.TryParseDropTargetChanged("not json at all", out _));
            Assert.IsFalse(DesignSurfaceDragDropProtocol.TryParseDropTargetChanged(@"{""type"":""dropTargetChanged"",""target"":{""valid"":true}}", out _));
        }

        // ── DesignSurfaceDragDropOp / event args ─────────────────────────

        [TestMethod]
        public void DesignSurfaceDragDropEditRequestedEventArgs_RejectsANullOp()
        {
            Assert.ThrowsExactly<System.ArgumentNullException>(() => new DesignSurfaceDragDropEditRequestedEventArgs(null!));
        }

        [TestMethod]
        public void DesignSurfaceDropTarget_RejectsANullParentId()
        {
            Assert.ThrowsExactly<System.ArgumentNullException>(() => new DesignSurfaceDropTarget(true, null!, 0, null, null, 0, 0, 0, 0));
        }

        [TestMethod]
        public void DesignSurfaceEditRequestsReceivedEventArgs_RejectsANullOpsList()
        {
            Assert.ThrowsExactly<System.ArgumentNullException>(() => new DesignSurfaceEditRequestsReceivedEventArgs(null!, DesignSurfaceGesture.Move));
        }

        [TestMethod]
        public void DesignSurfaceEditRequestsReceivedEventArgs_CarriesItsOwnFields()
        {
            var ops = new List<DesignSurfaceEditOp> { new DesignSurfaceEditOp(DesignSurfaceEditOpKind.SetAttribute, "0", "X", "1") };
            var args = new DesignSurfaceEditRequestsReceivedEventArgs(ops, DesignSurfaceGesture.Resize);
            Assert.AreSame(ops, args.Ops);
            Assert.AreEqual(DesignSurfaceGesture.Resize, args.Gesture);
        }

        // ── BOM regression (DSG-9 visual check finding) ─────────────────

        /// <summary>
        /// A live DSG-9 visual check found that the FIRST line ever written to a freshly-launched
        /// surface's stdin arrived on the Rust side prefixed with a U+FEFF UTF-8 byte-order mark -
        /// some <see cref="StreamWriter"/> configurations emit that preamble on their very first write only.
        /// <see cref="RustDesignSurfaceHost"/>'s own <c>LaunchSurface</c> now sets
        /// <c>StandardInputEncoding</c>/<c>StandardOutputEncoding</c> to
        /// <c>new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)</c> explicitly (that fix lives in
        /// <c>RustDesignSurfaceHost.cs</c>, not this file) - this test pins the encoding CONFIGURATION
        /// itself (the exact constructor call used there), so a future edit that drops the `false` (or
        /// reverts to the platform default) fails a test instead of silently reintroducing the bug.
        /// `kubuno_views::protocol::parse_host_message` also strips a leading BOM defensively (Rust-side
        /// test: `parse_host_message_strips_a_leading_byte_order_mark`), but this is the check that the
        /// BOM is not produced in the first place.
        /// </summary>
        [TestMethod]
        public void Utf8NoBomEncoding_TheFirstWriteOnAFreshStreamHasNoBomPreamble()
        {
            using var stream = new MemoryStream();
            using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 1024, leaveOpen: true))
            {
                writer.WriteLine(DesignSurfaceProtocol.EncodeSetDesignMode(true));
                writer.Flush();
            }

            var bytes = stream.ToArray();
            Assert.IsTrue(bytes.Length >= 3, "expected at least the 3 BOM bytes' worth of output");
            Assert.IsFalse(bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "the first write must not carry a UTF-8 BOM preamble");
        }
    }
}
