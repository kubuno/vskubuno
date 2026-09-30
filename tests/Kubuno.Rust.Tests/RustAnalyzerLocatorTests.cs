using System.Collections.Generic;
using System.IO;
using Kubuno.Rust.Logic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Rust.Tests
{
    [TestClass]
    public class RustAnalyzerLocatorTests
    {
        private sealed class FakeEnvironment : IRustAnalyzerEnvironment
        {
            private readonly HashSet<string> _existingFiles = new();

            public string? UserProfileDirectory { get; set; }

            public IEnumerable<string> PathDirectories { get; set; } = new List<string>();

            public string? RustupWhichResult { get; set; }

            public int RustupWhichCallCount { get; private set; }

            public void AddExistingFile(string path) => _existingFiles.Add(path);

            public bool FileExists(string path) => _existingFiles.Contains(path);

            public string? RunRustupWhich()
            {
                RustupWhichCallCount++;
                return RustupWhichResult;
            }
        }

        [TestMethod]
        public void Locate_PrefersOptionOverride_WhenFileExists()
        {
            var env = new FakeEnvironment { RustupWhichResult = @"C:\rustup\rust-analyzer.exe" };
            env.AddExistingFile(@"C:\rustup\rust-analyzer.exe");
            env.AddExistingFile(@"C:\custom\rust-analyzer.exe");

            var result = RustAnalyzerLocator.Locate(@"C:\custom\rust-analyzer.exe", env);

            Assert.IsTrue(result.IsFound);
            Assert.AreEqual(RustAnalyzerSource.OptionOverride, result.Source);
            Assert.AreEqual(@"C:\custom\rust-analyzer.exe", result.Path);
            Assert.AreEqual(0, env.RustupWhichCallCount, "Should short-circuit before calling rustup.");
        }

        [TestMethod]
        public void Locate_FallsThrough_WhenOptionOverridePointsAtMissingFile()
        {
            var env = new FakeEnvironment { RustupWhichResult = @"C:\rustup\rust-analyzer.exe" };
            env.AddExistingFile(@"C:\rustup\rust-analyzer.exe");

            var result = RustAnalyzerLocator.Locate(@"C:\custom\rust-analyzer.exe", env);

            Assert.AreEqual(RustAnalyzerSource.RustupWhich, result.Source);
            Assert.AreEqual(@"C:\rustup\rust-analyzer.exe", result.Path);
        }

        [TestMethod]
        public void Locate_UsesRustupWhich_WhenNoOverrideAndPathExists()
        {
            var env = new FakeEnvironment { RustupWhichResult = @"C:\rustup\bin\rust-analyzer.exe" };
            env.AddExistingFile(@"C:\rustup\bin\rust-analyzer.exe");

            var result = RustAnalyzerLocator.Locate(null, env);

            Assert.AreEqual(RustAnalyzerSource.RustupWhich, result.Source);
            Assert.AreEqual(@"C:\rustup\bin\rust-analyzer.exe", result.Path);
        }

        [TestMethod]
        public void Locate_IgnoresRustupResult_WhenReportedPathDoesNotExist()
        {
            // rustup can print a path for a toolchain component that was never actually installed;
            // the locator must verify the file is really there before trusting it.
            var env = new FakeEnvironment
            {
                RustupWhichResult = @"C:\rustup\bin\rust-analyzer.exe",
                UserProfileDirectory = @"C:\Users\dev",
            };
            env.AddExistingFile(Path.Combine(@"C:\Users\dev", ".cargo", "bin", "rust-analyzer.exe"));

            var result = RustAnalyzerLocator.Locate(null, env);

            Assert.AreEqual(RustAnalyzerSource.CargoBinDefault, result.Source);
        }

        [TestMethod]
        public void Locate_FallsBackToCargoBinDefault_WhenRustupWhichUnavailable()
        {
            var env = new FakeEnvironment { UserProfileDirectory = @"C:\Users\dev" };
            var expected = Path.Combine(@"C:\Users\dev", ".cargo", "bin", "rust-analyzer.exe");
            env.AddExistingFile(expected);

            var result = RustAnalyzerLocator.Locate(null, env);

            Assert.AreEqual(RustAnalyzerSource.CargoBinDefault, result.Source);
            Assert.AreEqual(expected, result.Path);
        }

        [TestMethod]
        public void Locate_FallsBackToPath_WhenNothingElseMatches()
        {
            var env = new FakeEnvironment
            {
                UserProfileDirectory = @"C:\Users\dev",
                PathDirectories = new[] { @"C:\tools\other", @"C:\tools\rust" },
            };
            env.AddExistingFile(@"C:\tools\rust\rust-analyzer.exe");

            var result = RustAnalyzerLocator.Locate(null, env);

            Assert.AreEqual(RustAnalyzerSource.Path, result.Source);
            Assert.AreEqual(@"C:\tools\rust\rust-analyzer.exe", result.Path);
        }

        [TestMethod]
        public void Locate_StopsAtFirstMatchingPathDirectory()
        {
            var env = new FakeEnvironment
            {
                PathDirectories = new[] { @"C:\first", @"C:\second" },
            };
            env.AddExistingFile(@"C:\first\rust-analyzer.exe");
            env.AddExistingFile(@"C:\second\rust-analyzer.exe");

            var result = RustAnalyzerLocator.Locate(null, env);

            Assert.AreEqual(@"C:\first\rust-analyzer.exe", result.Path);
        }

        [TestMethod]
        public void Locate_ReturnsNotFound_WhenNothingMatchesAnywhere()
        {
            var env = new FakeEnvironment
            {
                UserProfileDirectory = @"C:\Users\dev",
                PathDirectories = new[] { @"C:\tools" },
            };

            var result = RustAnalyzerLocator.Locate(null, env);

            Assert.IsFalse(result.IsFound);
            Assert.AreEqual(RustAnalyzerSource.NotFound, result.Source);
            Assert.IsNull(result.Path);
        }

        [TestMethod]
        public void Locate_SkipsMalformedPathEntry_AndKeepsSearching()
        {
            var env = new FakeEnvironment
            {
                PathDirectories = new[] { "C:\\tools\\bad\"quote", @"C:\tools\good" },
            };
            env.AddExistingFile(@"C:\tools\good\rust-analyzer.exe");

            var result = RustAnalyzerLocator.Locate(null, env);

            Assert.AreEqual(RustAnalyzerSource.Path, result.Source);
            Assert.AreEqual(@"C:\tools\good\rust-analyzer.exe", result.Path);
        }

        [TestMethod]
        public void Locate_TreatsBlankOverride_AsNoOverride()
        {
            var env = new FakeEnvironment { UserProfileDirectory = @"C:\Users\dev" };
            var expected = Path.Combine(@"C:\Users\dev", ".cargo", "bin", "rust-analyzer.exe");
            env.AddExistingFile(expected);

            var result = RustAnalyzerLocator.Locate("   ", env);

            Assert.AreEqual(RustAnalyzerSource.CargoBinDefault, result.Source);
        }
    }
}
