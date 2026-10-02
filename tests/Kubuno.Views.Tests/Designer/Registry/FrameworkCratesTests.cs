using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Kubuno.Views.Logic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Views.Tests.Designer.Registry
{
    /// <summary>The framework's crates: an explicit list (never a <c>kubuno</c> prefix), the same in C# and in Rust.</summary>
    [TestClass]
    public class FrameworkCratesTests
    {
        private const string RustListPath = @"desktop\windows\src\crates\kubuno-views-meta\src\framework.rs";

        [TestMethod]
        public void The_framework_is_a_list_not_a_prefix()
        {
            foreach (var name in new[] { "kubuno", "kubuno-views", "kubuno_views", "kubuno_ui", "kubuno-print", "\"kubuno-data\"", "kubuno_app_storage_components" })
            {
                Assert.IsTrue(FrameworkCrates.Contains(name), name);
            }

            foreach (var name in new[] { "kubuno-shell-controls", "kubuno_shell_controls", "kubuno-acme-widgets", "kubuno-sync", "kubuno-app-storage", "kubunoish", "shell-controls", "", null })
            {
                Assert.IsFalse(FrameworkCrates.Contains(name), name ?? "null");
            }
        }

        [TestMethod]
        public void The_list_is_the_one_of_kubuno_views_meta()
        {
            var file = FindRustList();
            if (file is null)
            {
                Assert.Inconclusive("the desktop sources (" + RustListPath + ") are not next to vskubuno");
                return;
            }

            var text = File.ReadAllText(file);
            var block = Regex.Match(text, @"pub const FRAMEWORK_CRATES: &\[&str\] = &\[(?<names>[^\]]*)\];");
            Assert.IsTrue(block.Success, "FRAMEWORK_CRATES not found in " + file);
            var rust = Regex.Matches(block.Groups["names"].Value, "\"(?<n>[^\"]+)\"").Cast<Match>().Select(m => m.Groups["n"].Value).ToArray();
            CollectionAssert.AreEqual(rust, FrameworkCrates.Names.ToArray(), "FrameworkCrates.Names and " + file + " differ: keep them identical, in the same order");
        }

        /// <summary>The Rust list in the desktop checkout next to vskubuno (found from this source file's folder upwards).</summary>
        private static string? FindRustList([CallerFilePath] string source = "")
        {
            var dir = string.IsNullOrEmpty(source) ? null : Path.GetDirectoryName(source);
            while (!string.IsNullOrEmpty(dir))
            {
                var candidate = Path.Combine(dir, RustListPath);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                dir = Path.GetDirectoryName(dir);
            }

            return null;
        }
    }
}
