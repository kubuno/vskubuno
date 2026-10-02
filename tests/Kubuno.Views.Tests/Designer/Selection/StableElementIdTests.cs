using Kubuno.Views.Designer.Selection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Views.Tests.Designer.Selection
{
    [TestClass]
    public class StableElementIdTests
    {
        [TestMethod]
        public void Child_OfRoot_IsJustTheIndex()
        {
            Assert.AreEqual("3", StableElementId.Child(StableElementId.Root, 3));
        }

        [TestMethod]
        public void Child_OfNonRoot_AppendsWithADot()
        {
            Assert.AreEqual("2.0.3", StableElementId.Child("2.0", 3));
        }

        [TestMethod]
        public void TryParseSegments_EmptyString_IsTheRootWithNoSegments()
        {
            Assert.IsTrue(StableElementId.TryParseSegments("", out var segments));
            CollectionAssert.AreEqual(System.Array.Empty<int>(), segments);
        }

        [TestMethod]
        public void TryParseSegments_MultiLevelId_SplitsEachOrdinal()
        {
            Assert.IsTrue(StableElementId.TryParseSegments("2.0.3", out var segments));
            CollectionAssert.AreEqual(new[] { 2, 0, 3 }, segments);
        }

        [TestMethod]
        public void TryParseSegments_NonNumericSegment_Fails()
        {
            Assert.IsFalse(StableElementId.TryParseSegments("2.x.3", out _));
        }

        [TestMethod]
        public void TryParseSegments_NegativeSegment_Fails()
        {
            Assert.IsFalse(StableElementId.TryParseSegments("2.-1", out _));
        }

        [TestMethod]
        public void ChildThenParseSegments_RoundTrips()
        {
            var id = StableElementId.Child(StableElementId.Child(StableElementId.Root, 2), 5);
            Assert.AreEqual("2.5", id);
            Assert.IsTrue(StableElementId.TryParseSegments(id, out var segments));
            CollectionAssert.AreEqual(new[] { 2, 5 }, segments);
        }
    }
}
