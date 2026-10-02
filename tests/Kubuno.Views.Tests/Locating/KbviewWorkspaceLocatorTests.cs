using System.Collections.Generic;
using Kubuno.Views.Locating;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Views.Tests.Locating
{
    [TestClass]
    public class KbviewWorkspaceLocatorTests
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

            Assert.IsNull(KbviewWorkspaceLocator.FindWorkspaceRoot(null, isDirectory, fileExists));
            Assert.IsNull(KbviewWorkspaceLocator.FindWorkspaceRoot(string.Empty, isDirectory, fileExists));
            Assert.IsNull(KbviewWorkspaceLocator.FindWorkspaceRoot("   ", isDirectory, fileExists));
        }

        [TestMethod]
        public void FindWorkspaceRoot_FindsManifest_InSameDirectoryAsKbviewFile()
        {
            var directories = new[] { @"C:\repo\shell\src" };
            var files = new[] { @"C:\repo\shell\src\settings_view.kbview", @"C:\repo\shell\Cargo.toml" };
            var (isDirectory, fileExists) = BuildFakeFileSystem(directories, files);

            var root = KbviewWorkspaceLocator.FindWorkspaceRoot(@"C:\repo\shell\src\settings_view.kbview", isDirectory, fileExists);

            Assert.AreEqual(@"C:\repo\shell", root);
        }

        [TestMethod]
        public void FindWorkspaceRoot_WalksUpMultipleLevels_ToNearestManifest()
        {
            var files = new[]
            {
                @"C:\repo\shell\src\admin\users_view.kbview",
                @"C:\repo\shell\Cargo.toml",
                @"C:\repo\Cargo.toml", // an outer workspace manifest further up - nearest wins, not this one
            };
            var (isDirectory, fileExists) = BuildFakeFileSystem(new string[0], files);

            var root = KbviewWorkspaceLocator.FindWorkspaceRoot(@"C:\repo\shell\src\admin\users_view.kbview", isDirectory, fileExists);

            Assert.AreEqual(@"C:\repo\shell", root);
        }

        [TestMethod]
        public void FindWorkspaceRoot_StartsSearchAtDirectoryItself_WhenStartIsADirectory()
        {
            var directories = new[] { @"C:\repo\shell" };
            var files = new[] { @"C:\repo\shell\Cargo.toml" };
            var (isDirectory, fileExists) = BuildFakeFileSystem(directories, files);

            var root = KbviewWorkspaceLocator.FindWorkspaceRoot(@"C:\repo\shell", isDirectory, fileExists);

            Assert.AreEqual(@"C:\repo\shell", root);
        }

        [TestMethod]
        public void FindWorkspaceRoot_FallsBackToStartingDirectory_WhenNoManifestFound()
        {
            var (isDirectory, fileExists) = BuildFakeFileSystem(new string[0], new string[0]);

            var root = KbviewWorkspaceLocator.FindWorkspaceRoot(@"C:\opened-folder\notes\file.kbview", isDirectory, fileExists);

            Assert.AreEqual(@"C:\opened-folder\notes", root);
        }

        [TestMethod]
        public void FindWorkspaceRoot_IgnoresTrailingSlash_OnDirectoryInput()
        {
            var directories = new[] { @"C:\repo\shell" };
            var files = new[] { @"C:\repo\shell\Cargo.toml" };
            var (isDirectory, fileExists) = BuildFakeFileSystem(directories, files);

            var root = KbviewWorkspaceLocator.FindWorkspaceRoot(@"C:\repo\shell\", isDirectory, fileExists);

            Assert.AreEqual(@"C:\repo\shell", root);
        }
    }
}
