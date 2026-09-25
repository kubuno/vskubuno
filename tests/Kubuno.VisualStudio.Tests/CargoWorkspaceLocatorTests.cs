using System.Collections.Generic;
using System.IO;
using Kubuno.VisualStudio.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.VisualStudio.Tests
{
    [TestClass]
    public class CargoWorkspaceLocatorTests
    {
        private static (System.Func<string, bool> isDirectory, System.Func<string, bool> fileExists) BuildFakeFileSystem(
            IEnumerable<string> directories, IEnumerable<string> files)
        {
            var directorySet = new HashSet<string>(directories);
            var fileSet = new HashSet<string>(files);
            return (d => directorySet.Contains(d), f => fileSet.Contains(f));
        }

        [TestMethod]
        public void FindWorkspaceRoot_ReturnsNull_ForNullOrEmptyStart()
        {
            var (isDirectory, fileExists) = BuildFakeFileSystem(new string[0], new string[0]);

            Assert.IsNull(CargoWorkspaceLocator.FindWorkspaceRoot(null, isDirectory, fileExists));
            Assert.IsNull(CargoWorkspaceLocator.FindWorkspaceRoot(string.Empty, isDirectory, fileExists));
            Assert.IsNull(CargoWorkspaceLocator.FindWorkspaceRoot("   ", isDirectory, fileExists));
        }

        [TestMethod]
        public void FindWorkspaceRoot_FindsManifest_InSameDirectoryAsFile()
        {
            var directories = new[] { @"C:\repo\crate\src" };
            var files = new[] { @"C:\repo\crate\src\main.rs", @"C:\repo\crate\Cargo.toml" };
            var (isDirectory, fileExists) = BuildFakeFileSystem(directories, files);

            var root = CargoWorkspaceLocator.FindWorkspaceRoot(@"C:\repo\crate\src\main.rs", isDirectory, fileExists);

            Assert.AreEqual(@"C:\repo\crate", root);
        }

        [TestMethod]
        public void FindWorkspaceRoot_WalksUpMultipleLevels_ToNearestManifest()
        {
            var files = new[]
            {
                @"C:\repo\crate\src\nested\deep.rs",
                @"C:\repo\crate\Cargo.toml",
                @"C:\repo\Cargo.toml", // an outer workspace manifest further up - nearest wins, not this one
            };
            var (isDirectory, fileExists) = BuildFakeFileSystem(new string[0], files);

            var root = CargoWorkspaceLocator.FindWorkspaceRoot(@"C:\repo\crate\src\nested\deep.rs", isDirectory, fileExists);

            Assert.AreEqual(@"C:\repo\crate", root);
        }

        [TestMethod]
        public void FindWorkspaceRoot_StartsSearchAtDirectoryItself_WhenStartIsADirectory()
        {
            var directories = new[] { @"C:\repo\crate" };
            var files = new[] { @"C:\repo\crate\Cargo.toml" };
            var (isDirectory, fileExists) = BuildFakeFileSystem(directories, files);

            var root = CargoWorkspaceLocator.FindWorkspaceRoot(@"C:\repo\crate", isDirectory, fileExists);

            Assert.AreEqual(@"C:\repo\crate", root);
        }

        [TestMethod]
        public void FindWorkspaceRoot_FallsBackToStartingDirectory_WhenNoManifestFound()
        {
            var (isDirectory, fileExists) = BuildFakeFileSystem(new string[0], new string[0]);

            var root = CargoWorkspaceLocator.FindWorkspaceRoot(@"C:\opened-folder\notes\file.rs", isDirectory, fileExists);

            Assert.AreEqual(@"C:\opened-folder\notes", root);
        }

        [TestMethod]
        public void FindWorkspaceRoot_FallsBackToItself_WhenNoManifestFound_AndStartIsADirectory()
        {
            var directories = new[] { @"C:\opened-folder" };
            var (isDirectory, fileExists) = BuildFakeFileSystem(directories, new string[0]);

            var root = CargoWorkspaceLocator.FindWorkspaceRoot(@"C:\opened-folder", isDirectory, fileExists);

            Assert.AreEqual(@"C:\opened-folder", root);
        }

        [TestMethod]
        public void FindWorkspaceRoot_IgnoresTrailingSlash_OnDirectoryInput()
        {
            var directories = new[] { @"C:\repo\crate" };
            var files = new[] { @"C:\repo\crate\Cargo.toml" };
            var (isDirectory, fileExists) = BuildFakeFileSystem(directories, files);

            var root = CargoWorkspaceLocator.FindWorkspaceRoot(@"C:\repo\crate\", isDirectory, fileExists);

            Assert.AreEqual(@"C:\repo\crate", root);
        }
    }
}
