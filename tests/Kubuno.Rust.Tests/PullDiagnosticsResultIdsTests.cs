using Kubuno.VisualStudio.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.VisualStudio.Tests
{
    [TestClass]
    public sealed class PullDiagnosticsResultIdsTests
    {
        [TestMethod]
        public void SameServerIdGivesADifferentIdEveryTime()
        {
            var ids = new PullDiagnosticsResultIds();

            var first = ids.MakeUnique("rust-analyzer");
            var second = ids.MakeUnique("rust-analyzer");

            Assert.AreNotEqual(first, second);
            Assert.AreNotEqual("rust-analyzer", first);
            Assert.IsTrue(first!.StartsWith("rust-analyzer", System.StringComparison.Ordinal));
        }

        [TestMethod]
        public void NoServerIdStaysNoId()
        {
            Assert.IsNull(new PullDiagnosticsResultIds().MakeUnique(null));
        }
    }
}
