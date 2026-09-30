using System;
using System.IO;
using System.Linq;
using System.Text;
using Kubuno.Desktop.Logic.DesignSurface;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Desktop.Tests.DesignSurface
{
    /// <summary>docs/DESIGNER.md, "One file name per kubuno_ui build": which kubuno_ui DLL a program imports.</summary>
    [TestClass]
    public class KubunoUiLibraryTests
    {
        [TestMethod]
        public void Recognises_the_hashed_and_the_plain_names()
        {
            Assert.IsTrue(KubunoUiLibrary.IsLibraryFileName("kubuno_ui-0123456789abcdef.dll"));
            Assert.IsTrue(KubunoUiLibrary.IsLibraryFileName("KUBUNO_UI-0123456789ABCDEF.DLL"));
            Assert.IsTrue(KubunoUiLibrary.IsLibraryFileName("kubuno_ui.dll"));
            Assert.IsTrue(KubunoUiLibrary.IsHashedFileName("kubuno_ui-0123456789abcdef.dll"));
            Assert.IsFalse(KubunoUiLibrary.IsHashedFileName("kubuno_ui.dll"));
            Assert.IsFalse(KubunoUiLibrary.IsLibraryFileName("kubuno_ui-0123.dll"));
            Assert.IsFalse(KubunoUiLibrary.IsLibraryFileName("kubuno_ui-0123456789abcdeg.dll"));
            Assert.IsFalse(KubunoUiLibrary.IsLibraryFileName("kubuno_ui.dll.lib"));
            Assert.IsFalse(KubunoUiLibrary.IsLibraryFileName(@"..\kubuno_ui.dll"));
            Assert.IsFalse(KubunoUiLibrary.IsLibraryFileName(null));
        }

        [TestMethod]
        public void Reads_the_import_table_of_a_pe32_plus_image()
        {
            var image = Image("std-44a584f44bc3dd65.dll", "kubuno_ui-0123456789abcdef.dll", "KERNEL32.dll");

            CollectionAssert.AreEqual(
                new[] { "std-44a584f44bc3dd65.dll", "kubuno_ui-0123456789abcdef.dll", "KERNEL32.dll" },
                KubunoUiLibrary.ReadImports(image).ToArray());
        }

        [TestMethod]
        public void The_imported_library_is_read_from_the_file()
        {
            var path = Path.Combine(Path.GetTempPath(), "kubuno-ui-imports-" + Guid.NewGuid().ToString("N") + ".exe");
            try
            {
                File.WriteAllBytes(path, Image("std-1.dll", "kubuno_ui-00000000deadbeef.dll"));
                Assert.AreEqual("kubuno_ui-00000000deadbeef.dll", KubunoUiLibrary.ImportedBy(path));

                File.WriteAllBytes(path, Image("kubuno_ui.dll"));
                Assert.AreEqual("kubuno_ui.dll", KubunoUiLibrary.ImportedBy(path), "a build older than per-build names");

                File.WriteAllBytes(path, Image("KERNEL32.dll"));
                Assert.IsNull(KubunoUiLibrary.ImportedBy(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void Garbage_and_missing_files_have_no_imports()
        {
            Assert.AreEqual(0, KubunoUiLibrary.ReadImports(new byte[0]).Count);
            Assert.AreEqual(0, KubunoUiLibrary.ReadImports(Encoding.ASCII.GetBytes("MZ not really a PE file, just text")).Count);
            var truncated = Image("kubuno_ui.dll").Take(0x150).ToArray();
            Assert.AreEqual(0, KubunoUiLibrary.ReadImports(truncated).Count);
            Assert.IsNull(KubunoUiLibrary.ImportedBy(Path.Combine(Path.GetTempPath(), "no-such-" + Guid.NewGuid().ToString("N") + ".exe")));
        }

        [TestMethod]
        public void A_real_system_program_lists_kernel32()
        {
            var notepad = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "notepad.exe");
            if (!File.Exists(notepad))
            {
                Assert.Inconclusive("no notepad.exe on this machine");
            }

            var imports = KubunoUiLibrary.ReadImportedDllNames(notepad);
            Assert.IsTrue(imports.Any(n => n.StartsWith("KERNEL32", StringComparison.OrdinalIgnoreCase) || n.StartsWith("api-ms-win", StringComparison.OrdinalIgnoreCase)), string.Join(", ", imports));
        }

        /// <summary>
        /// A minimal PE32+ image: headers at 0, one section (RVA 0x1000, file offset 0x200) holding the import
        /// descriptors and, from RVA 0x1100 (file 0x300), the DLL names.
        /// </summary>
        private static byte[] Image(params string[] dlls)
        {
            var image = new byte[0x600];
            image[0] = (byte)'M';
            image[1] = (byte)'Z';
            WriteInt32(image, 0x3C, 0x40);
            image[0x40] = (byte)'P';
            image[0x41] = (byte)'E';
            var coff = 0x44;
            WriteUInt16(image, coff, 0x8664);
            WriteUInt16(image, coff + 2, 1);
            WriteUInt16(image, coff + 16, 0xF0);
            var optional = coff + 20;
            WriteUInt16(image, optional, 0x20b);
            WriteInt32(image, optional + 112 + 8, 0x1000); // data directory 1: imports
            WriteInt32(image, optional + 112 + 12, (dlls.Length + 1) * 20);
            var section = optional + 0xF0;
            WriteInt32(image, section + 8, 0x400);
            WriteInt32(image, section + 12, 0x1000);
            WriteInt32(image, section + 16, 0x400);
            WriteInt32(image, section + 20, 0x200);

            var nameRva = 0x1100;
            for (var i = 0; i < dlls.Length; i++)
            {
                WriteInt32(image, 0x200 + (i * 20) + 12, nameRva);
                var bytes = Encoding.ASCII.GetBytes(dlls[i]);
                Array.Copy(bytes, 0, image, 0x200 + (nameRva - 0x1000), bytes.Length);
                nameRva += bytes.Length + 1;
            }

            return image;
        }

        private static void WriteInt32(byte[] bytes, int offset, int value) => Array.Copy(BitConverter.GetBytes(value), 0, bytes, offset, 4);

        private static void WriteUInt16(byte[] bytes, int offset, int value) => Array.Copy(BitConverter.GetBytes((ushort)value), 0, bytes, offset, 2);
    }
}
