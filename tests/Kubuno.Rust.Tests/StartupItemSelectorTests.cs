using System.Collections.Generic;
using Kubuno.Cargo.Metadata;
using Kubuno.VisualStudio.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.VisualStudio.Tests
{
    [TestClass]
    public class StartupItemSelectorTests
    {
        private static CargoTarget Bin(string name) => new()
        {
            Name = name,
            Kind = new[] { CargoTargetKind.Bin },
        };

        private static CargoTarget Example(string name) => new()
        {
            Name = name,
            Kind = new[] { CargoTargetKind.Example },
        };

        private static CargoPackage Package(string name, string? defaultRun, IReadOnlyList<CargoTarget> targets) => new()
        {
            Name = name,
            DefaultRun = defaultRun,
            Targets = targets,
        };

        [TestMethod]
        public void SelectDefaultBinTarget_ReturnsNull_WhenNoBinTargets()
        {
            var package = Package("mylib", defaultRun: null, targets: new[] { Example("demo") });

            Assert.IsNull(StartupItemSelector.SelectDefaultBinTarget(package));
        }

        [TestMethod]
        public void SelectDefaultBinTarget_PrefersDefaultRun_WhenItNamesAnExistingBin()
        {
            var package = Package("app", defaultRun: "tool", targets: new[] { Bin("app"), Bin("tool") });

            Assert.AreEqual("tool", StartupItemSelector.SelectDefaultBinTarget(package));
        }

        [TestMethod]
        public void SelectDefaultBinTarget_IgnoresDefaultRun_WhenItNamesANonExistentBin()
        {
            // A stale/typo'd `default-run` should not make selection silently return null or throw -
            // fall through the rest of the chain as if it were unset.
            var package = Package("app", defaultRun: "does-not-exist", targets: new[] { Bin("app") });

            Assert.AreEqual("app", StartupItemSelector.SelectDefaultBinTarget(package));
        }

        [TestMethod]
        public void SelectDefaultBinTarget_ReturnsTheOnlyBin_WhenThereIsExactlyOne()
        {
            var package = Package("hello-rust", defaultRun: null, targets: new[] { Bin("hello-rust") });

            Assert.AreEqual("hello-rust", StartupItemSelector.SelectDefaultBinTarget(package));
        }

        [TestMethod]
        public void SelectDefaultBinTarget_ReturnsTheOnlyBin_EvenWhenItIsNotNamedAfterThePackage()
        {
            var package = Package("hello-rust", defaultRun: null, targets: new[] { Bin("greet") });

            Assert.AreEqual("greet", StartupItemSelector.SelectDefaultBinTarget(package));
        }

        [TestMethod]
        public void SelectDefaultBinTarget_PrefersTheBinNamedAfterThePackage_WithMultipleBinsAndNoDefaultRun()
        {
            var package = Package("app", defaultRun: null, targets: new[] { Bin("helper"), Bin("app"), Bin("tool") });

            Assert.AreEqual("app", StartupItemSelector.SelectDefaultBinTarget(package));
        }

        [TestMethod]
        public void SelectDefaultBinTarget_FallsBackToTheFirstBin_WithMultipleBinsAndNoOtherSignal()
        {
            var package = Package("app", defaultRun: null, targets: new[] { Bin("alpha"), Bin("beta") });

            Assert.AreEqual("alpha", StartupItemSelector.SelectDefaultBinTarget(package));
        }
    }
}
