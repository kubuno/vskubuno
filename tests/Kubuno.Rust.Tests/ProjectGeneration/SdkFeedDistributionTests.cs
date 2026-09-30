using System;
using System.Collections.Generic;
using System.IO;
using Kubuno.Rust.Logic.ProjectGeneration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Rust.Tests.ProjectGeneration
{
    /// <summary>
    /// docs/RSPROJ.md, "SDK feed": a <c>.rsproj</c> must resolve <c>Sdk="Kubuno.Rust.Sdk/…"</c> without any Kubuno package having
    /// loaded, so the solution carries its own feed and the user's NuGet.Config is filled in by the template wizard too.
    /// </summary>
    [TestClass]
    public class SdkFeedDistributionTests
    {
        private string _root = null!;
        private string _extension = null!;
        private readonly List<string> _log = new List<string>();

        [TestInitialize]
        public void Setup()
        {
            _root = Path.Combine(Path.GetTempPath(), "kubuno-sdkfeed-" + Guid.NewGuid().ToString("N"));
            _extension = Path.Combine(_root, "extension");
            Directory.CreateDirectory(Path.Combine(_extension, "tools", "SdkFeed"));
            File.WriteAllBytes(Path.Combine(_extension, "tools", "SdkFeed", "Kubuno.Rust.Sdk.1.0.0.nupkg"), new byte[] { 1, 2, 3, 4 });
        }

        [TestCleanup]
        public void Cleanup()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
            }
        }

        [TestMethod]
        public void ASolutionGetsItsOwnCopyOfTheSdkAndARelativeSource()
        {
            var solution = Path.Combine(_root, "solution");
            Directory.CreateDirectory(solution);

            Assert.IsTrue(SdkFeedDistribution.EnsureSolutionLocal(solution, _extension, _log.Add));

            CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(Path.Combine(solution, ".kubuno", "sdk-feed", "Kubuno.Rust.Sdk.1.0.0.nupkg")));
            var config = File.ReadAllText(Path.Combine(solution, "NuGet.Config"));
            StringAssert.Contains(config, "key=\"Kubuno.Rust.Sdk (solution)\"");
            StringAssert.Contains(config, "value=\".kubuno/sdk-feed\"");
        }

        [TestMethod]
        public void AnExistingNuGetConfigIsMergedIntoUnderItsOwnSpellingAndTwiceChangesNothing()
        {
            var solution = Path.Combine(_root, "solution");
            Directory.CreateDirectory(solution);
            var existing = Path.Combine(solution, "nuget.config");
            File.WriteAllText(existing, "<configuration><packageSources><add key=\"mine\" value=\"https://example.test/v3/index.json\" /></packageSources></configuration>");

            Assert.IsTrue(SdkFeedDistribution.EnsureSolutionLocal(solution, _extension, _log.Add));
            var first = File.ReadAllText(existing);
            Assert.IsTrue(SdkFeedDistribution.EnsureSolutionLocal(solution, _extension, _log.Add));

            StringAssert.Contains(first, "key=\"mine\"");
            StringAssert.Contains(first, ".kubuno/sdk-feed");
            Assert.AreEqual(first, File.ReadAllText(existing));
            Assert.AreEqual(1, Directory.GetFiles(solution, "*", SearchOption.TopDirectoryOnly).Length, "no second NuGet.Config next to the existing one");
        }

        [TestMethod]
        public void NothingIsWrittenWhenTheExtensionShipsNoFeed()
        {
            var solution = Path.Combine(_root, "solution");
            Directory.CreateDirectory(solution);

            Assert.IsFalse(SdkFeedDistribution.EnsureSolutionLocal(solution, Path.Combine(_root, "nowhere"), _log.Add));
            Assert.IsFalse(SdkFeedDistribution.EnsureSolutionLocal(solution, null, _log.Add));
            Assert.AreEqual(0, Directory.GetFileSystemEntries(solution).Length);
        }

        [TestMethod]
        public void TheUserNuGetConfigGetsTheExtensionFeedOnceAndIsRepointedWhenItMoves()
        {
            var config = Path.Combine(_root, "roaming", "NuGet", "NuGet.Config");
            var stamp = Path.Combine(_root, "local", "sdk-feed.stamp");

            Assert.IsTrue(SdkFeedDistribution.EnsureUserRegistered(_extension, _log.Add, config, stamp));
            StringAssert.Contains(File.ReadAllText(config), Path.Combine(_extension, "tools", "SdkFeed"));
            var written = File.GetLastWriteTimeUtc(config);
            Assert.IsTrue(SdkFeedDistribution.EnsureUserRegistered(_extension, _log.Add, config, stamp));
            Assert.AreEqual(written, File.GetLastWriteTimeUtc(config), "an unchanged registration leaves the file alone");

            var moved = Path.Combine(_root, "extension2");
            Directory.CreateDirectory(Path.Combine(moved, "tools", "SdkFeed"));
            Assert.IsTrue(SdkFeedDistribution.EnsureUserRegistered(moved, _log.Add, config, stamp));
            StringAssert.Contains(File.ReadAllText(config), Path.Combine(moved, "tools", "SdkFeed"));
        }

        [TestMethod]
        public void AnExperimentalInstanceDoesNotRepointTheRegularInstallationsSource()
        {
            var config = Path.Combine(_root, "roaming", "NuGet", "NuGet.Config");
            var stamp = Path.Combine(_root, "local", "sdk-feed.stamp");
            var regular = Path.Combine(_root, "18.0_dc9e2338", "Extensions", "abc");
            var experimental = Path.Combine(_root, "18.0_dc9e2338KubunoDesk", "Extensions", "def");
            foreach (var extension in new[] { regular, experimental })
            {
                Directory.CreateDirectory(Path.Combine(extension, "tools", "SdkFeed"));
                File.WriteAllBytes(Path.Combine(extension, "tools", "SdkFeed", "Kubuno.Rust.Sdk.1.1.0.nupkg"), new byte[] { 1 });
            }

            Assert.IsTrue(SdkFeedDistribution.EnsureUserRegistered(regular, _log.Add, config, stamp));
            Assert.IsTrue(SdkFeedDistribution.EnsureUserRegistered(experimental, _log.Add, config, stamp));
            StringAssert.Contains(File.ReadAllText(config), Path.Combine(regular, "tools", "SdkFeed"));
            Assert.IsFalse(File.ReadAllText(config).Contains(experimental));

            // The regular installation's feed is gone (uninstalled): the experimental instance may fill the gap...
            Directory.Delete(Path.Combine(regular, "tools", "SdkFeed"), recursive: true);
            File.Delete(stamp);
            Assert.IsTrue(SdkFeedDistribution.EnsureUserRegistered(experimental, _log.Add, config, stamp));
            StringAssert.Contains(File.ReadAllText(config), Path.Combine(experimental, "tools", "SdkFeed"));

            // ...and the regular installation always takes its source back.
            Directory.CreateDirectory(Path.Combine(regular, "tools", "SdkFeed"));
            Assert.IsTrue(SdkFeedDistribution.EnsureUserRegistered(regular, _log.Add, config, stamp));
            StringAssert.Contains(File.ReadAllText(config), Path.Combine(regular, "tools", "SdkFeed"));
        }

        [TestMethod]
        [DataRow(@"C:\Users\u\AppData\Local\Microsoft\VisualStudio\18.0_dc9e2338\Extensions\x", false)]
        [DataRow(@"C:\Users\u\AppData\Local\Microsoft\VisualStudio\18.0_dc9e2338Exp\Extensions\x", true)]
        [DataRow(@"C:\Users\u\AppData\Local\Microsoft\VisualStudio\18.0_dc9e2338KubunoDesk\Extensions\Kubuno\1.0", true)]
        [DataRow(@"C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\Extensions\x", false)]
        [DataRow(null, false)]
        public void ExperimentalHivesAreRecognizedByTheirRootSuffix(string? directory, bool expected) =>
            Assert.AreEqual(expected, SdkFeedDistribution.IsExperimentalHiveDirectory(directory));
    }
}
