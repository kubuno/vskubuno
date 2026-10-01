using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using Kubuno.Desktop.Logic.Resources;
using Kubuno.Desktop.Resources.Editor;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Desktop.Tests.Resources
{
    /// <summary>The pure parts of the resource editor: grid rows, details, blank files, the ICO container.</summary>
    [TestClass]
    public class ResourceEditorLogicTests
    {
        private sealed class MemoryFs : IResourceFileSystem
        {
            public Dictionary<string, byte[]> Files { get; } = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

            public bool FileExists(string path) => Files.ContainsKey(path);

            public byte[] ReadAllBytes(string path) => Files[path];

            public void WriteAllBytes(string path, byte[] bytes) => Files[path] = bytes;

            public void CopyFile(string from, string to) => Files[to] = Files[from];
        }

        private static ResourceSetModel NewModel(MemoryFs? fs = null)
        {
            var model = new ResourceSetModel(@"C:\proj\Strings.kbres", new KbresFile().ToText(), new (string, string)[0], fs ?? new MemoryFs());
            model.AddString("title", "Title", "The title");
            model.AddString("ok", "OK");
            model.AddText(ResourceKind.Color, "accent", "#3366FF");
            model.AddCulture("fr");
            return model;
        }

        [TestMethod]
        public void Grid_ListsOnlyTheCategory_AndReadsCells()
        {
            var model = NewModel();
            model.SetValue("title", "fr", "Titre");
            var grid = new ResourceGridModel(model, ResourceCategory.Strings);

            Assert.AreEqual(2, grid.Rows.Count);
            Assert.AreEqual("Title", grid.Rows[0].Value);
            Assert.AreEqual("The title", grid.Rows[0].Comment);
            Assert.AreEqual("Titre", grid.Rows[0]["fr"]);
            Assert.AreEqual(string.Empty, grid.Rows[1]["fr"]);
            Assert.IsFalse(grid.Rows[0].IsMissing("fr"));
            Assert.IsTrue(grid.Rows[1].IsMissing("fr"));
            Assert.AreEqual(1, grid.MissingCount("fr"));
            Assert.AreEqual(1, new ResourceGridModel(model, ResourceCategory.Other).Rows.Count);
        }

        [TestMethod]
        public void Grid_CellWrites_GoToTheModel_AsOneUndoStepEach()
        {
            var model = NewModel();
            var grid = new ResourceGridModel(model, ResourceCategory.Strings);

            grid.Rows[0]["fr"] = "Titre";
            grid.Rows[0].Value = "New title";
            grid.Rows[0].Comment = "changed";
            grid.Rows[0].Value = "New title"; // unchanged: no history entry

            Assert.AreEqual("New title", model.Get("title")!.Text);
            Assert.AreEqual("Titre", model.Translation("title", "fr")!.Text);
            Assert.AreEqual("changed", model.Get("title")!.Comment);
            model.Undo();
            Assert.AreEqual("The title", model.Get("title")!.Comment);
            model.Undo();
            Assert.AreEqual("Title", model.Get("title")!.Text);
            model.Undo();
            Assert.IsNull(model.Translation("title", "fr"));
        }

        [TestMethod]
        public void Grid_EmptyTranslation_RemovesIt()
        {
            var model = NewModel();
            var grid = new ResourceGridModel(model, ResourceCategory.Strings);
            grid.Rows[0]["fr"] = "Titre";
            grid.Rows[0]["fr"] = string.Empty;
            Assert.IsNull(model.Translation("title", "fr"));
        }

        [TestMethod]
        public void Grid_RefusedWrites_RaiseErrorAndKeepTheValue()
        {
            var model = NewModel();
            var grid = new ResourceGridModel(model, ResourceCategory.Strings);
            var errors = new List<string>();
            grid.Error += (_, message) => errors.Add(message);

            grid.Rows[0].Name = "ok"; // taken
            grid.Rows[0].Name = "not valid"; // invalid
            Assert.AreEqual(2, errors.Count);
            Assert.AreEqual("title", grid.Rows[0].Name);
            Assert.IsNotNull(model.Get("title"));

            var other = new ResourceGridModel(model, ResourceCategory.Other);
            other.Error += (_, message) => errors.Add(message);
            other.Rows[0].Value = "not a colour";
            Assert.AreEqual(3, errors.Count);
            Assert.AreEqual("#3366FF", model.Get("accent")!.Text);
        }

        [TestMethod]
        public void Grid_Rename_ThenRefresh_KeepsTheRowAndRebuildsOnStructuralChange()
        {
            var model = NewModel();
            var grid = new ResourceGridModel(model, ResourceCategory.Strings);
            var row = grid.Rows[0];

            row.Name = "heading";
            Assert.AreEqual("heading", row.Name);
            Assert.AreEqual("Title", row.Value);
            grid.Refresh();
            Assert.AreSame(row, grid.Rows[0]); // same names: rows updated in place

            model.AddString("extra", "x");
            grid.Refresh();
            Assert.AreEqual(3, grid.Rows.Count);
            model.Undo();
            grid.Refresh();
            Assert.AreEqual(2, grid.Rows.Count);
        }

        [TestMethod]
        public void Grid_RowRaisesPropertyChanged_OnRefresh()
        {
            var model = NewModel();
            var grid = new ResourceGridModel(model, ResourceCategory.Strings);
            var raised = 0;
            grid.Rows[0].PropertyChanged += (_, _) => raised++;
            grid.Refresh();
            Assert.AreEqual(1, raised);
        }

        [TestMethod]
        public void Details_DescribeLinkedAndEmbeddedEntries()
        {
            var fs = new MemoryFs();
            fs.Files[@"C:\proj\Resources\logo.png"] = new byte[2048];
            var model = NewModel(fs);
            var linked = model.AddFile(@"C:\proj\Resources\logo.png");
            var embedded = model.AddFile(@"C:\proj\Resources\logo.png", ResourcePersistence.Embedded, "logo2");

            var a = ResourceDetails.Of(model, linked);
            Assert.AreEqual(2048, a.Size);
            Assert.IsFalse(a.Embedded);
            Assert.AreEqual("Resources/logo.png", a.Path);
            Assert.AreEqual("png", a.Format);

            var b = ResourceDetails.Of(model, embedded);
            Assert.IsTrue(b.Embedded);
            Assert.AreEqual(string.Empty, b.Path);

            fs.Files.Remove(@"C:\proj\Resources\logo.png");
            Assert.IsNull(ResourceDetails.Of(model, linked).Size);
            Assert.AreEqual("512 B", ResourceDetails.FormatSize(512));
            Assert.AreEqual("2 KB", ResourceDetails.FormatSize(2048));
        }

        [TestMethod]
        public void IcoWriter_WritesAValidContainer()
        {
            var png16 = new byte[] { 1, 2, 3 };
            var png256 = new byte[] { 4, 5, 6, 7 };
            var ico = IcoWriter.Write(new[] { (16, png16), (256, png256) });

            Assert.AreEqual(6 + 32 + 3 + 4, ico.Length);
            Assert.AreEqual(0, BitConverter.ToUInt16(ico, 0));
            Assert.AreEqual(1, BitConverter.ToUInt16(ico, 2));
            Assert.AreEqual(2, BitConverter.ToUInt16(ico, 4));
            Assert.AreEqual(16, ico[6]);
            Assert.AreEqual(3u, BitConverter.ToUInt32(ico, 6 + 8));
            Assert.AreEqual(38u, BitConverter.ToUInt32(ico, 6 + 12));
            Assert.AreEqual(0, ico[6 + 16]); // 256 is stored as 0
            Assert.AreEqual(41u, BitConverter.ToUInt32(ico, 6 + 16 + 12));
            CollectionAssert.AreEqual(png256, ico.Skip(41).ToArray());
            Assert.ThrowsExactly<ArgumentException>(() => IcoWriter.Write(new List<(int, byte[])>()));
        }

        [TestMethod]
        public void BlankResources_AreDecodable()
        {
            foreach (var format in BlankResources.ImageFormats)
            {
                using var stream = new MemoryStream(BlankResources.Image(format));
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                Assert.AreEqual(32, decoder.Frames[0].PixelWidth, format);
            }

            using var ico = new MemoryStream(BlankResources.Icon());
            var frames = BitmapDecoder.Create(ico, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames;
            CollectionAssert.AreEquivalent(BlankResources.IconSizes, frames.Select(f => f.PixelWidth).ToArray());
        }

        [TestMethod]
        public void BlankResources_FreeFile_SkipsExistingNames()
        {
            var dir = Path.Combine(Path.GetTempPath(), "kbres-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                Assert.AreEqual(Path.Combine(dir, "image.png"), BlankResources.FreeFile(dir, "image", "png"));
                File.WriteAllBytes(Path.Combine(dir, "image.png"), new byte[1]);
                Assert.AreEqual(Path.Combine(dir, "image2.png"), BlankResources.FreeFile(dir, "image", "png"));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
