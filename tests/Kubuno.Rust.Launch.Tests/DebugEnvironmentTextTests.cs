using Kubuno.Launch;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Launch.Tests
{
    [TestClass]
    public sealed class DebugEnvironmentTextTests
    {
        [TestMethod]
        public void Parse_EmptyOrNull_ReturnsNoEntries()
        {
            Assert.AreEqual(0, DebugEnvironmentText.Parse(null).Count);
            Assert.AreEqual(0, DebugEnvironmentText.Parse("  \r\n ").Count);
        }

        [TestMethod]
        public void Parse_OnePairPerLine_BothLineEndings()
        {
            var result = DebugEnvironmentText.Parse("RUST_LOG=debug\r\nKUBUNO_SERVER=http://localhost:8080\nEMPTY=");

            Assert.AreEqual(3, result.Count);
            Assert.AreEqual("debug", result["RUST_LOG"]);
            Assert.AreEqual("http://localhost:8080", result["KUBUNO_SERVER"]);
            Assert.AreEqual(string.Empty, result["EMPTY"]);
        }

        [TestMethod]
        public void Parse_ValueKeepsLaterEqualsAndSemicolons()
        {
            var result = DebugEnvironmentText.Parse(@"PATH=C:\a;C:\b=c");

            Assert.AreEqual(@"C:\a;C:\b=c", result["PATH"]);
        }

        [TestMethod]
        public void Parse_IgnoresCommentsAndNamelessLines_LaterDuplicateWins()
        {
            var result = DebugEnvironmentText.Parse("# comment\n=novalue\nnoequals\n rust_backtrace = 0\nRUST_BACKTRACE=full");

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("full", result["RUST_BACKTRACE"]);
        }
    }
}
