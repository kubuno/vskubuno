using System;
using System.IO;
using Kubuno.Rust.Logic.ProjectGeneration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Rust.Tests.ProjectGeneration
{
    [TestClass]
    public sealed class SolutionNamingTests
    {
        [TestMethod]
        public void The_web_core_the_desktop_core_and_modules_get_distinct_solution_names()
        {
            var root = Path.Combine(Path.GetTempPath(), "kubuno-naming-" + Guid.NewGuid().ToString("N"));
            try
            {
                var core = Write(Path.Combine(root, "core", "crates", "kubuno-core", "Cargo.toml"), "[package]\nname = \"kubuno-core\"\n");
                var desktop = Write(Path.Combine(root, "desktop", "windows", "src", "crates", "kubuno-desktop-ui", "Cargo.toml"), "[package]\nname = \"kubuno-desktop-ui\"\n");
                var module = Write(Path.Combine(root, "p2p-nas", "module.toml"), "[module]\nid = \"p2p-nas\"\n");
                Directory.CreateDirectory(Path.Combine(root, "other"));

                Assert.AreEqual("Kubuno.Core.slnx", SolutionNaming.DefaultSolutionFileName(Path.Combine(root, "core")));
                Assert.AreEqual("Kubuno.Desktop.slnx", SolutionNaming.DefaultSolutionFileName(Path.Combine(root, "desktop", "windows")));
                Assert.AreEqual("Kubuno.Desktop.slnx", SolutionNaming.DefaultSolutionFileName(Path.Combine(root, "desktop")), "the desktop repository root");
                Write(Path.Combine(root, "old-desktop", "windows", "src", "crates", "kubuno-ui", "Cargo.toml"), "[package]\nname = \"kubuno-ui\"\n");
                Assert.AreEqual("Kubuno.Desktop.slnx", SolutionNaming.DefaultSolutionFileName(Path.Combine(root, "old-desktop", "windows")), "a checkout older than the 2026-10 rename");
                Assert.AreEqual("Kubuno.P2pNas.slnx", SolutionNaming.DefaultSolutionFileName(Path.Combine(root, "p2p-nas")));
                Assert.AreEqual("other.slnx", SolutionNaming.DefaultSolutionFileName(Path.Combine(root, "other")));
                Assert.IsTrue(File.Exists(core) && File.Exists(desktop) && File.Exists(module));
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        private static string Write(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
            return path;
        }
    }
}
