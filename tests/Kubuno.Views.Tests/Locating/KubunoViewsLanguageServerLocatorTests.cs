using System.Collections.Generic;
using System.IO;
using Kubuno.Desktop.Views.Locating;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Desktop.Tests.Views
{
    [TestClass]
    public class KubunoViewsLanguageServerLocatorTests
    {
        private sealed class FakeEnvironment : IKubunoViewsLanguageServerEnvironment
        {
            private readonly HashSet<string> _existingFiles = new();

            public IEnumerable<string> PathDirectories { get; set; } = new List<string>();

            public IEnumerable<string> DevBuildDirectories { get; set; } = new List<string>();

            public void AddExistingFile(string path) => _existingFiles.Add(path);

            public bool FileExists(string path) => _existingFiles.Contains(path);
        }

        private const string ExeName = "kubuno-views-ls.exe";

        [TestMethod]
        public void Locate_PrefersOptionOverride_WhenFileExists()
        {
            var env = new FakeEnvironment();
            env.AddExistingFile(@"C:\custom\kubuno-views-ls.exe");
            env.AddExistingFile(Path.Combine(@"C:\extension", "tools", ExeName));

            var result = KubunoViewsLanguageServerLocator.Locate(@"C:\custom\kubuno-views-ls.exe", @"C:\extension", env);

            Assert.IsTrue(result.IsFound);
            Assert.AreEqual(KubunoViewsLanguageServerSource.OptionOverride, result.Source);
            Assert.AreEqual(@"C:\custom\kubuno-views-ls.exe", result.Path);
        }

        [TestMethod]
        public void Locate_FallsThrough_WhenOptionOverridePointsAtMissingFile()
        {
            var env = new FakeEnvironment();
            var toolsCandidate = Path.Combine(@"C:\extension", "tools", ExeName);
            env.AddExistingFile(toolsCandidate);

            var result = KubunoViewsLanguageServerLocator.Locate(@"C:\custom\kubuno-views-ls.exe", @"C:\extension", env);

            Assert.AreEqual(KubunoViewsLanguageServerSource.ExtensionToolsFolder, result.Source);
            Assert.AreEqual(toolsCandidate, result.Path);
        }

        [TestMethod]
        public void Locate_UsesExtensionToolsFolder_WhenNoOverrideAndFileExists()
        {
            var env = new FakeEnvironment();
            var toolsCandidate = Path.Combine(@"C:\Program Files\Kubuno for Visual Studio", "tools", ExeName);
            env.AddExistingFile(toolsCandidate);

            var result = KubunoViewsLanguageServerLocator.Locate(null, @"C:\Program Files\Kubuno for Visual Studio", env);

            Assert.AreEqual(KubunoViewsLanguageServerSource.ExtensionToolsFolder, result.Source);
            Assert.AreEqual(toolsCandidate, result.Path);
        }

        [TestMethod]
        public void Locate_SkipsToolsFolderCandidate_WhenExtensionDirectoryUnknown()
        {
            var env = new FakeEnvironment { PathDirectories = new[] { @"C:\tools\bin" } };
            env.AddExistingFile(Path.Combine(@"C:\tools\bin", ExeName));

            var result = KubunoViewsLanguageServerLocator.Locate(null, null, env);

            Assert.AreEqual(KubunoViewsLanguageServerSource.Path, result.Source);
        }

        [TestMethod]
        public void Locate_FallsBackToPath_WhenExtensionToolsFolderHasNoBinary()
        {
            var env = new FakeEnvironment { PathDirectories = new[] { @"C:\tools\other", @"C:\tools\bin" } };
            env.AddExistingFile(Path.Combine(@"C:\tools\bin", ExeName));

            var result = KubunoViewsLanguageServerLocator.Locate(null, @"C:\extension", env);

            Assert.AreEqual(KubunoViewsLanguageServerSource.Path, result.Source);
            Assert.AreEqual(Path.Combine(@"C:\tools\bin", ExeName), result.Path);
        }

        [TestMethod]
        public void Locate_StopsAtFirstMatchingPathDirectory()
        {
            var env = new FakeEnvironment { PathDirectories = new[] { @"C:\first", @"C:\second" } };
            env.AddExistingFile(Path.Combine(@"C:\first", ExeName));
            env.AddExistingFile(Path.Combine(@"C:\second", ExeName));

            var result = KubunoViewsLanguageServerLocator.Locate(null, null, env);

            Assert.AreEqual(Path.Combine(@"C:\first", ExeName), result.Path);
        }

        [TestMethod]
        public void Locate_FallsBackToDevBuildFolder_WhenNothingElseMatches()
        {
            var env = new FakeEnvironment
            {
                PathDirectories = new[] { @"C:\tools\other" },
                DevBuildDirectories = new[] { @"C:\kubuno-build\desktop-target\debug", @"C:\kubuno-build\agent-views-ls\debug" },
            };
            env.AddExistingFile(Path.Combine(@"C:\kubuno-build\agent-views-ls\debug", ExeName));

            var result = KubunoViewsLanguageServerLocator.Locate(null, @"C:\extension", env);

            Assert.AreEqual(KubunoViewsLanguageServerSource.DevBuildFolder, result.Source);
            Assert.AreEqual(Path.Combine(@"C:\kubuno-build\agent-views-ls\debug", ExeName), result.Path);
        }

        [TestMethod]
        public void Locate_PrefersFirstDevBuildFolder_OverLater()
        {
            var env = new FakeEnvironment
            {
                DevBuildDirectories = new[] { @"C:\kubuno-build\desktop-target\debug", @"C:\kubuno-build\agent-views-ls\debug" },
            };
            env.AddExistingFile(Path.Combine(@"C:\kubuno-build\desktop-target\debug", ExeName));
            env.AddExistingFile(Path.Combine(@"C:\kubuno-build\agent-views-ls\debug", ExeName));

            var result = KubunoViewsLanguageServerLocator.Locate(null, null, env);

            Assert.AreEqual(Path.Combine(@"C:\kubuno-build\desktop-target\debug", ExeName), result.Path);
        }

        [TestMethod]
        public void Locate_ReturnsNotFound_WhenNothingMatchesAnywhere()
        {
            var env = new FakeEnvironment
            {
                PathDirectories = new[] { @"C:\tools" },
                DevBuildDirectories = new[] { @"C:\kubuno-build\desktop-target\debug" },
            };

            var result = KubunoViewsLanguageServerLocator.Locate(null, @"C:\extension", env);

            Assert.IsFalse(result.IsFound);
            Assert.AreEqual(KubunoViewsLanguageServerSource.NotFound, result.Source);
            Assert.IsNull(result.Path);
        }

        [TestMethod]
        public void Locate_SkipsMalformedPathEntry_AndKeepsSearching()
        {
            var env = new FakeEnvironment { PathDirectories = new[] { "C:\\tools\\bad\"quote", @"C:\tools\good" } };
            env.AddExistingFile(Path.Combine(@"C:\tools\good", ExeName));

            var result = KubunoViewsLanguageServerLocator.Locate(null, null, env);

            Assert.AreEqual(KubunoViewsLanguageServerSource.Path, result.Source);
            Assert.AreEqual(Path.Combine(@"C:\tools\good", ExeName), result.Path);
        }

        [TestMethod]
        public void Locate_TreatsBlankOverride_AsNoOverride()
        {
            var env = new FakeEnvironment();
            var toolsCandidate = Path.Combine(@"C:\extension", "tools", ExeName);
            env.AddExistingFile(toolsCandidate);

            var result = KubunoViewsLanguageServerLocator.Locate("   ", @"C:\extension", env);

            Assert.AreEqual(KubunoViewsLanguageServerSource.ExtensionToolsFolder, result.Source);
        }

        [TestMethod]
        public void Locate_ThrowsArgumentNullException_WhenEnvironmentIsNull()
        {
            Assert.ThrowsExactly<System.ArgumentNullException>(() => KubunoViewsLanguageServerLocator.Locate(null, null, null!));
        }
    }
}
