using System.Collections.Generic;
using Kubuno.Rust.Logic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Rust.Tests
{
    [TestClass]
    public class NonRustProjectExclusionScannerTests
    {
        private const string Root = @"Z:\ws";

        [TestMethod]
        public void ReturnsEmpty_WhenNoNonRustProjectFilesExist()
        {
            var files = new[] { @"Z:\ws\src\shell\main.rs", @"Z:\ws\Cargo.toml" };

            var result = NonRustProjectExclusionScanner.FindDirectoriesToExclude(Root, files, cargoPackageManifestPaths: new string[0]);

            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void ExcludesTheTwoLevelDeepDirectory_ForACsprojNestedSeveralLevelsDown()
        {
            var files = new[]
            {
                @"Z:\ws\tools\winforms-ref\parity\layout-winforms\layout-winforms.csproj",
                @"Z:\ws\tools\winforms-ref\parity\panels-winforms\panels-winforms.csproj",
            };

            var result = NonRustProjectExclusionScanner.FindDirectoriesToExclude(Root, files, cargoPackageManifestPaths: new string[0]);

            CollectionAssert.AreEqual(new[] { "tools/winforms-ref" }, (List<string>)result);
        }

        [TestMethod]
        public void NeverExcludesADirectoryThatIsAlsoAnAncestorOfARealCargoPackage()
        {
            // A stray .vcxproj generated inside `src/` (e.g. by some other tool) must not hide
            // real Rust source living under the same top-level directory.
            var files = new[] { @"Z:\ws\src\weird\generated.vcxproj" };
            var manifests = new[] { @"Z:\ws\src\shell\Cargo.toml" };

            var result = NonRustProjectExclusionScanner.FindDirectoriesToExclude(Root, files, manifests);

            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void IgnoresAProjectFileDirectlyAtTheWorkspaceRoot()
        {
            var files = new[] { @"Z:\ws\Legacy.csproj" };

            var result = NonRustProjectExclusionScanner.FindDirectoriesToExclude(Root, files, cargoPackageManifestPaths: new string[0]);

            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void NeverExcludesOurOwnGeneratedRsprojAndSolution_ForAWorkspaceMatchingGenerateRustProjectsCommandsLayout()
        {
            // Regression for docs/RSPROJ.md §5 / work package 6: once "Kubuno: Generate Visual Studio
            // Projects" (GenerateRustProjectsCommand) has run, the workspace root gains a `<folder>.sln`
            // and each member directory gains a `<member>.rsproj` right next to its own Cargo.toml -
            // exactly RsprojSolutionGenerator/RsprojTemplate's own output shape. None of that may ever
            // end up in ExcludedItems: `.rsproj` is not a "foreign" extension at all (ProjectFileExtensions
            // never lists it - Open Folder must keep navigating straight to it), and the generated `.sln`
            // always sits at the workspace root itself (0 relative segments, below the "needs 2+ segments"
            // threshold that makes a project file worth excluding a directory for in the first place).
            var files = new[]
            {
                @"Z:\ws\Cargo.toml",
                @"Z:\ws\ws.sln",
                @"Z:\ws\kubuno-drive-desktop\Cargo.toml",
                @"Z:\ws\kubuno-drive-desktop\kubuno-drive-desktop.rsproj",
                @"Z:\ws\kubuno-chat\Cargo.toml",
                @"Z:\ws\kubuno-chat\kubuno-chat.rsproj",
            };
            var manifests = new[]
            {
                @"Z:\ws\Cargo.toml",
                @"Z:\ws\kubuno-drive-desktop\Cargo.toml",
                @"Z:\ws\kubuno-chat\Cargo.toml",
            };

            var result = NonRustProjectExclusionScanner.FindDirectoriesToExclude(Root, files, manifests);

            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void DeduplicatesAndSorts_WhenMultipleMatchesShareTheSameTwoLevelDirectory()
        {
            var files = new[]
            {
                @"Z:\ws\tools\winforms-ref\parity\layout-winforms\layout-winforms.csproj",
                @"Z:\ws\tools\winforms-ref\catalog\catalog.csproj",
                @"Z:\ws\vendor\legacy\Legacy.sln",
            };

            var result = NonRustProjectExclusionScanner.FindDirectoriesToExclude(Root, files, cargoPackageManifestPaths: new string[0]);

            CollectionAssert.AreEqual(new[] { "tools/winforms-ref", "vendor/legacy" }, (List<string>)result);
        }
    }
}
