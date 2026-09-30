using Kubuno.VisualStudio.Core.ProjectGeneration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.VisualStudio.Tests.ProjectGeneration
{
    [TestClass]
    public class DeterministicGuidTests
    {
        [TestMethod]
        public void From_IsStable_ForTheSameSeed()
        {
            var first = DeterministicGuid.From(@"C:\ws\app\app.rsproj");
            var second = DeterministicGuid.From(@"C:\ws\app\app.rsproj");

            Assert.AreEqual(first, second);
        }

        [TestMethod]
        public void From_DiffersAcrossSeeds()
        {
            var app = DeterministicGuid.From(@"C:\ws\app\app.rsproj");
            var tool = DeterministicGuid.From(@"C:\ws\tool\tool.rsproj");

            Assert.AreNotEqual(app, tool);
        }

        [TestMethod]
        public void From_IsCaseSensitive_ToTheSeed()
        {
            // The caller (RsprojSolutionGenerator) is responsible for normalizing the seed before
            // calling this - the helper itself must not silently paper over a difference.
            var lower = DeterministicGuid.From(@"c:\ws\app\app.rsproj");
            var upper = DeterministicGuid.From(@"C:\WS\APP\APP.RSPROJ");

            Assert.AreNotEqual(lower, upper);
        }

        [TestMethod]
        public void From_ProducesAWellFormedVersion3Guid()
        {
            var guid = DeterministicGuid.From("seed");
            // Guid.ToByteArray() is the documented exact inverse of `new Guid(byte[])`, so this
            // round-trips the same byte positions DeterministicGuid itself set.
            var bytes = guid.ToByteArray();

            Assert.AreEqual(0x30, bytes[6] & 0xF0, "Version nibble should be 3 (name-based, MD5).");
            Assert.AreEqual(0x80, bytes[8] & 0xC0, "Variant bits should be the RFC 4122 variant.");
        }
    }
}
