using System;
using System.Linq;
using Kubuno.Rust.Launch;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Rust.Launch.Tests
{
    [TestClass]
    public sealed class TestLaunchArgsTests
    {
        [TestMethod]
        public void Build_Defaults_ProduceExactFilterWithDebuggingFlags()
        {
            var args = TestLaunchArgs.Build("module::tests::it_works");

            CollectionAssert.AreEqual(
                new[] { "module::tests::it_works", "--exact", "--nocapture", "--test-threads=1" },
                args.ToArray());
        }

        [TestMethod]
        public void Build_ModuleFilter_OmitsExactWhenRequested()
        {
            var args = TestLaunchArgs.Build("module::tests::", exact: false);

            CollectionAssert.AreEqual(
                new[] { "module::tests::", "--nocapture", "--test-threads=1" },
                args.ToArray());
        }

        [TestMethod]
        public void Build_AllFlagsDisabled_YieldsBareFilter()
        {
            var args = TestLaunchArgs.Build("module::tests::it_works", exact: false, noCapture: false, singleThreaded: false);

            CollectionAssert.AreEqual(new[] { "module::tests::it_works" }, args.ToArray());
        }

        [TestMethod]
        public void Build_ThrowsOnEmptyFilter()
        {
            Assert.ThrowsExactly<ArgumentException>(() => TestLaunchArgs.Build(string.Empty));
        }
    }
}
