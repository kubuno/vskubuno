using System;
using System.IO;
using System.Linq;
using Kubuno.VisualStudio.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.VisualStudio.Tests.Debugging
{
    [TestClass]
    public sealed class RustDebuggerFilesTests
    {
        private string _root = string.Empty;
        private string _source = string.Empty;
        private string _destination = string.Empty;

        [TestInitialize]
        public void Setup()
        {
            _root = Path.Combine(Path.GetTempPath(), "kubuno-dbgfiles-" + Guid.NewGuid().ToString("N"));
            _source = Path.Combine(_root, "src");
            _destination = Path.Combine(_root, "Visual Studio 18", "Visualizers");
            Directory.CreateDirectory(_source);
            foreach (var name in RustDebuggerFiles.RustFiles.Concat(RustDebuggerFiles.FrameworkFiles))
            {
                File.WriteAllText(Path.Combine(_source, name), "<content of " + name + "/>");
            }
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        [TestMethod]
        public void Installs_the_rust_and_framework_files_and_is_idempotent()
        {
            RustDebuggerFiles.EnsureInstalled(_source, new[] { _destination }, frameworkIsExternalCode: true);
            var logged = 0;
            RustDebuggerFiles.EnsureInstalled(_source, new[] { _destination }, frameworkIsExternalCode: true, _ => logged++);

            foreach (var name in RustDebuggerFiles.RustFiles.Concat(RustDebuggerFiles.FrameworkFiles))
            {
                Assert.AreEqual("<content of " + name + "/>", File.ReadAllText(Path.Combine(_destination, name)));
            }

            Assert.AreEqual(0, logged, "an up-to-date copy is left alone");
        }

        [TestMethod]
        public void Turning_the_framework_option_off_removes_only_the_framework_files()
        {
            RustDebuggerFiles.EnsureInstalled(_source, new[] { _destination }, frameworkIsExternalCode: true);
            File.WriteAllText(Path.Combine(_destination, "mine.natstepfilter"), "user file");

            RustDebuggerFiles.EnsureInstalled(_source, new[] { _destination }, frameworkIsExternalCode: false);

            Assert.IsTrue(RustDebuggerFiles.RustFiles.All(n => File.Exists(Path.Combine(_destination, n))));
            Assert.IsFalse(RustDebuggerFiles.FrameworkFiles.Any(n => File.Exists(Path.Combine(_destination, n))));
            Assert.IsTrue(File.Exists(Path.Combine(_destination, "mine.natstepfilter")), "other files are never touched");
        }

        [TestMethod]
        public void A_changed_shipped_file_is_recopied()
        {
            RustDebuggerFiles.EnsureInstalled(_source, new[] { _destination }, frameworkIsExternalCode: false);
            File.WriteAllText(Path.Combine(_source, "Kubuno.Rust.natjmc"), "<v2/>");

            RustDebuggerFiles.EnsureInstalled(_source, new[] { _destination }, frameworkIsExternalCode: false);

            Assert.AreEqual("<v2/>", File.ReadAllText(Path.Combine(_destination, "Kubuno.Rust.natjmc")));
        }

        [TestMethod]
        public void Legacy_toolchain_natvis_copies_are_removed_but_not_foreign_files_of_the_same_name()
        {
            Directory.CreateDirectory(_destination);
            var rust = Path.Combine(_destination, "liballoc.natvis");
            File.WriteAllText(rust, "<AutoVisualizer xmlns=\"http://schemas.microsoft.com/vstudio/debugger/natvis/2010\"><Type Name=\"alloc::vec::Vec&lt;*&gt;\"/></AutoVisualizer>");
            var foreign = Path.Combine(_destination, "libcore.natvis");
            File.WriteAllText(foreign, "<AutoVisualizer xmlns=\"http://schemas.microsoft.com/vstudio/debugger/natvis/2010\"><Type Name=\"MyLib::Thing\"/></AutoVisualizer>");

            RustDebuggerFiles.EnsureInstalled(_source, new[] { _destination }, frameworkIsExternalCode: true);

            Assert.IsFalse(File.Exists(rust));
            Assert.IsTrue(File.Exists(foreign));
        }

        [TestMethod]
        public void A_missing_shipped_file_is_logged_not_thrown()
        {
            File.Delete(Path.Combine(_source, "Kubuno.Rust.natjmc"));
            string? log = null;

            RustDebuggerFiles.EnsureInstalled(_source, new[] { _destination }, frameworkIsExternalCode: false, line => log ??= line);

            StringAssert.Contains(log, "Kubuno.Rust.natjmc");
        }
    }
}
