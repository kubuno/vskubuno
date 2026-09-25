using System.Collections.Generic;
using Kubuno.VisualStudio.Designer.Editing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.VisualStudio.Designer.Tests.Editing
{
    [TestClass]
    public class TextEditPlannerTests
    {
        private static LspRange Range(int startLine, int startChar, int endLine, int endChar) =>
            new LspRange(new LspPosition(startLine, startChar), new LspPosition(endLine, endChar));

        [TestMethod]
        public void Plan_SingleEdit_ResolvesOffsets()
        {
            var text = "abcdef";
            var edits = new[] { new TextEditDto(Range(0, 1, 0, 4), "XYZ") };

            var planned = TextEditPlanner.Plan(text, edits);

            Assert.AreEqual(1, planned.Count);
            Assert.AreEqual(1, planned[0].StartOffset);
            Assert.AreEqual(4, planned[0].EndOffset);
            Assert.AreEqual("XYZ", planned[0].NewText);
        }

        [TestMethod]
        public void Plan_MultipleNonOverlappingEdits_OrdersDescendingByStart()
        {
            var text = "0123456789";
            var edits = new[]
            {
                new TextEditDto(Range(0, 0, 0, 2), "A"), // [0,2)
                new TextEditDto(Range(0, 5, 0, 7), "B"), // [5,7)
                new TextEditDto(Range(0, 8, 0, 9), "C"), // [8,9)
            };

            var planned = TextEditPlanner.Plan(text, edits);

            Assert.AreEqual(3, planned.Count);
            Assert.AreEqual(8, planned[0].StartOffset);
            Assert.AreEqual(5, planned[1].StartOffset);
            Assert.AreEqual(0, planned[2].StartOffset);
        }

        [TestMethod]
        public void Plan_DirectlyOverlappingEdits_Throws()
        {
            var text = "0123456789";
            var edits = new[]
            {
                new TextEditDto(Range(0, 0, 0, 5), "A"), // [0,5)
                new TextEditDto(Range(0, 3, 0, 6), "B"), // [3,6) overlaps [0,5)
            };

            Assert.ThrowsExactly<TextEditConflictException>(() => TextEditPlanner.Plan(text, edits));
        }

        [TestMethod]
        public void Plan_NonAdjacentOverlap_IsStillDetected()
        {
            // A = [0,20), B = [2,5), C = [10,15) - B and C don't overlap each other, but both sit
            // inside A: a naive "only check adjacent pairs after sorting by start" scan would miss the
            // A/C overlap. Regression test for that.
            var text = new string('x', 30);
            var edits = new[]
            {
                new TextEditDto(Range(0, 0, 0, 20), "A"),
                new TextEditDto(Range(0, 2, 0, 5), "B"),
                new TextEditDto(Range(0, 10, 0, 15), "C"),
            };

            Assert.ThrowsExactly<TextEditConflictException>(() => TextEditPlanner.Plan(text, edits));
        }

        [TestMethod]
        public void Plan_TouchingButNotOverlappingEdits_AreAllowed()
        {
            var text = "0123456789";
            var edits = new[]
            {
                new TextEditDto(Range(0, 0, 0, 5), "A"), // [0,5)
                new TextEditDto(Range(0, 5, 0, 8), "B"), // [5,8) - starts exactly where A ends
            };

            var planned = TextEditPlanner.Plan(text, edits);

            Assert.AreEqual(2, planned.Count);
        }

        [TestMethod]
        public void Plan_ZeroLengthInsertsAtSameOffset_AreAllowed()
        {
            var text = "0123456789";
            var edits = new[]
            {
                new TextEditDto(Range(0, 3, 0, 3), "A"),
                new TextEditDto(Range(0, 3, 0, 3), "B"),
            };

            var planned = TextEditPlanner.Plan(text, edits);

            Assert.AreEqual(2, planned.Count);
        }

        [TestMethod]
        public void Plan_EmptyEditList_ReturnsEmpty()
        {
            var planned = TextEditPlanner.Plan("abc", new List<TextEditDto>());

            Assert.AreEqual(0, planned.Count);
        }

        [TestMethod]
        public void ApplyToPlainText_SingleReplacement()
        {
            var result = TextEditPlanner.ApplyToPlainText("hello world", new[] { new TextEditDto(Range(0, 6, 0, 11), "there") });

            Assert.AreEqual("hello there", result);
        }

        [TestMethod]
        public void ApplyToPlainText_MultipleEdits_AppliedWithoutOffsetDrift()
        {
            // Replacing [0,1) with a longer string must not shift the still-pending [5,6) edit's
            // offset - this is exactly what the descending application order guards against.
            var result = TextEditPlanner.ApplyToPlainText(
                "abcdef",
                new[]
                {
                    new TextEditDto(Range(0, 0, 0, 1), "AAAA"),
                    new TextEditDto(Range(0, 5, 0, 6), "F"),
                });

            Assert.AreEqual("AAAAbcdeF", result);
        }

        [TestMethod]
        public void ApplyToPlainText_PureInsertion_ZeroLengthRange()
        {
            var result = TextEditPlanner.ApplyToPlainText("ac", new[] { new TextEditDto(Range(0, 1, 0, 1), "b") });

            Assert.AreEqual("abc", result);
        }

        [TestMethod]
        public void ApplyToPlainText_PureDeletion_EmptyNewText()
        {
            var result = TextEditPlanner.ApplyToPlainText("abcdef", new[] { new TextEditDto(Range(0, 1, 0, 4), "") });

            Assert.AreEqual("aef", result);
        }
    }
}
