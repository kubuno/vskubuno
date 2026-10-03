using System.Collections.Generic;
using Kubuno.Rust.Cargo.Metadata;
using Kubuno.Rust.Logic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Rust.Tests
{
    /// <summary>
    /// <see cref="StartupItemSelector.SelectDefaultBinTargetForWorkspace"/> - the fallback used when
    /// the opened folder's own Cargo.toml is a virtual <c>[workspace]</c> manifest with no
    /// <c>[package]</c> of its own, so <see cref="StartupItemSelector.SelectDefaultBinTarget"/> has no
    /// single "current package" to work from (see that method's own tests in
    /// <see cref="StartupItemSelectorTests"/>).
    /// </summary>
    [TestClass]
    public class StartupItemSelectorForWorkspaceTests
    {
        private static CargoTarget Bin(string name) => new()
        {
            Name = name,
            Kind = new[] { CargoTargetKind.Bin },
        };

        private static CargoPackage Package(string id, string name, string manifestDirectory, IReadOnlyList<CargoTarget> targets) => new()
        {
            Id = id,
            Name = name,
            ManifestPath = manifestDirectory + "\\Cargo.toml",
            Targets = targets,
        };

        private static CargoMetadata Metadata(IReadOnlyList<CargoPackage> packages, IReadOnlyList<string>? workspaceDefaultMembers = null) => new()
        {
            Packages = packages,
            WorkspaceDefaultMembers = workspaceDefaultMembers ?? System.Array.Empty<string>(),
        };

        [TestMethod]
        public void ReturnsNull_WhenNoPackageHasABinTarget()
        {
            var metadata = Metadata(new[] { Package("lib#0.1.0", "lib", @"Z:\ws\src\lib", targets: System.Array.Empty<CargoTarget>()) });

            Assert.IsNull(StartupItemSelector.SelectDefaultBinTargetForWorkspace(metadata));
        }

        [TestMethod]
        public void ReturnsTheOnlyBin_AcrossTheWholeWorkspace_EvenInADifferentPackageThanItsName()
        {
            var metadata = Metadata(new[]
            {
                Package("lib#0.1.0", "lib", @"Z:\ws\src\lib", targets: System.Array.Empty<CargoTarget>()),
                Package("app#0.1.0", "app", @"Z:\ws\src\app", targets: new[] { Bin("kubuno-desktop") }),
            });

            var result = StartupItemSelector.SelectDefaultBinTargetForWorkspace(metadata);

            Assert.IsNotNull(result);
            Assert.AreEqual("app", result!.Value.Package.Name);
            Assert.AreEqual("kubuno-desktop", result.Value.BinTarget);
        }

        [TestMethod]
        public void PrefersThePackageInAShellDirectory_WithMultipleBinsAndNoNarrowerSignal()
        {
            var metadata = Metadata(new[]
            {
                Package("chat#0.1.0", "kubuno-chat", @"Z:\ws\src\chat", targets: new[] { Bin("kubuno-chat") }),
                Package("shell#0.1.0", "kubuno-desktop", @"Z:\ws\src\shell", targets: new[] { Bin("kubuno-desktop") }),
                Package("drive#0.1.0", "drive", @"Z:\ws\src\drive\crates\kubuno-drive-desktop", targets: new[] { Bin("drive") }),
            });

            var result = StartupItemSelector.SelectDefaultBinTargetForWorkspace(metadata);

            Assert.IsNotNull(result);
            Assert.AreEqual("kubuno-desktop", result!.Value.BinTarget);
        }

        [TestMethod]
        public void FallsBackToTheFirstBinInOrder_WithMultipleBinsAndNoShellDirectory()
        {
            var metadata = Metadata(new[]
            {
                Package("chat#0.1.0", "kubuno-chat", @"Z:\ws\src\chat", targets: new[] { Bin("kubuno-chat") }),
                Package("drive#0.1.0", "drive", @"Z:\ws\src\drive", targets: new[] { Bin("drive") }),
            });

            var result = StartupItemSelector.SelectDefaultBinTargetForWorkspace(metadata);

            Assert.IsNotNull(result);
            Assert.AreEqual("kubuno-chat", result!.Value.BinTarget);
        }

        [TestMethod]
        public void RestrictsToWorkspaceDefaultMembers_WhenTheyNarrowTheCandidates()
        {
            // Only "shell" is a declared `[workspace] default-members` entry - "drive" is a bin
            // target too, but must be ignored because Cargo itself would not build it by default.
            var metadata = Metadata(
                new[]
                {
                    Package("shell#0.1.0", "kubuno-desktop", @"Z:\ws\src\shell", targets: new[] { Bin("kubuno-desktop") }),
                    Package("drive#0.1.0", "drive", @"Z:\ws\src\drive", targets: new[] { Bin("drive") }),
                },
                workspaceDefaultMembers: new[] { "shell#0.1.0" });

            var result = StartupItemSelector.SelectDefaultBinTargetForWorkspace(metadata);

            Assert.IsNotNull(result);
            Assert.AreEqual("kubuno-desktop", result!.Value.BinTarget);
        }
    }
}
