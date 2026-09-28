using System.Collections.Generic;
using Kubuno.VisualStudio.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.VisualStudio.Tests
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
