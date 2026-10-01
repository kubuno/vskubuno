using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kubuno.Desktop.Logic.Resources;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Desktop.Tests.Resources
{
    /// <summary>The .kbres format (same canonical text as the Rust writer), names and cultures, and the resource editor model.</summary>
    [TestClass]
    public class ResourceModelTests
    {
        /// <summary>The sample of kubuno-resources-model's own round-trip test (format.rs): already canonical.</summary>
        private const string Sample =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
            "<Resources Version=\"1\">\n" +
            "  <String Name=\"welcome_text\" Comment=\"The greeting\">Welcome to &lt;Kubuno&gt; &amp; co</String>\n" +
            "  <String Name=\"empty\"/>\n" +
            "  <String Name=\"multi\">line 1\nline 2</String>\n" +
            "  <Image Name=\"logo\" File=\"images/logo.png\" Comment=\"Header logo\"/>\n" +
            "  <Icon Name=\"app\" File=\"app.ico\"/>\n" +
            "  <Image Name=\"dot\" Format=\"png\">AAECAwQF</Image>\n" +
            "  <Audio Name=\"ding\" File=\"sounds/ding.wav\"/>\n" +
            "  <File Name=\"license\" File=\"LICENSE.txt\" Text=\"true\"/>\n" +
            "  <Color Name=\"accent\" Value=\"#3366FF\"/>\n" +
            "  <Font Name=\"heading\" Value=\"Segoe UI, 14pt, style=Bold\"/>\n" +
            "</Resources>\n";

        [TestMethod]
        public void Format_ReadsEveryKind_AndRoundTripsLikeTheRustWriter()
        {
            var file = KbresFile.Read(Sample, out var diagnostics);
            Assert.AreEqual(0, diagnostics.Count, string.Join("; ", diagnostics));
            Assert.AreEqual(10, file.Entries.Count);
            Assert.AreEqual("Welcome to <Kubuno> & co", file.Get("welcome_text")!.Text);
            Assert.AreEqual("The greeting", file.Get("welcome_text")!.Comment);
            Assert.AreEqual("line 1\nline 2", file.Get("multi")!.Text);
            Assert.AreEqual(ResourcePersistence.Linked, file.Get("logo")!.Persistence);
            Assert.AreEqual("images/logo.png", file.Get("logo")!.Path);
            CollectionAssert.AreEqual(new byte[] { 0, 1, 2, 3, 4, 5 }, file.Get("dot")!.Bytes);
            Assert.AreEqual(ResourceKind.Icon, file.Get("app")!.Kind);
            Assert.IsTrue(file.Get("license")!.TextFile);
            Assert.AreEqual(7, file.Get("logo")!.Line);
            Assert.AreEqual(Sample, file.ToText());
        }

        [TestMethod]
        public void Format_WrapsLongEmbeddedContent_AndKeepsComments()
        {
            var file = new KbresFile();
            file.Entries.Add(ResourceEntry.Embedded(ResourceKind.Image, "big", "PNG", Enumerable.Range(0, 1000).Select(i => (byte)i).ToArray(), "x\ny \"q\""));
            var text = file.ToText();
            Assert.IsTrue(text.Split('\n').All(l => l.Length <= 90));
            var back = KbresFile.Parse(text);
            CollectionAssert.AreEqual(file.Entries[0].Bytes, back.Entries[0].Bytes);
            Assert.AreEqual("png", back.Entries[0].Format);
            Assert.AreEqual("x\ny \"q\"", back.Entries[0].Comment);
            Assert.AreEqual(text, back.ToText());
            Assert.AreEqual("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<Resources Version=\"1\"/>\n", new KbresFile().ToText());
        }

        [TestMethod]
        public void Format_ReportsProblems()
        {
            KbresFile.Read("<Resources><String Name=\"a\">x</String><String Name=\"a\">y</String><Blob Name=\"b\"/><Image Name=\"c\"/><String Name=\"9x\"/></Resources>", out var d);
            Assert.AreEqual(4, d.Count, string.Join("; ", d));
            Assert.IsTrue(d.Any(x => x.Message.Contains("duplicate resource `a`")));
            KbresFile.Read("<Resources><String", out d);
            Assert.IsTrue(d.Single().IsError);
        }

        [TestMethod]
        public void Names_MatchTheRustRules()
        {
            Assert.AreEqual("welcome_text", ResourceNames.RustName("WelcomeText"));
            Assert.AreEqual("ok_button_text", ResourceNames.RustName("okButton.Text"));
            Assert.AreEqual("http_server2", ResourceNames.RustName("HTTPServer2"));
            Assert.AreEqual("medium_plus_plus", ResourceNames.RustName("Medium++"));
            Assert.AreEqual("type_", ResourceNames.RustName("type"));
            Assert.AreEqual("MainView", ResourceNames.TypeName("main_view"));
            Assert.IsTrue(KbresFile.IsValidName("okButton.Text"));
            Assert.IsFalse(KbresFile.IsValidName("a b"));
            Assert.AreEqual(("resources", (string?)"de-DE"), ResourceNames.SplitFileName("resources.de-de.kbres")!.Value);
            Assert.AreEqual(("app.icons", (string?)null), ResourceNames.SplitFileName("app.icons.kbres")!.Value);
            Assert.IsNull(ResourceNames.SplitFileName("a.resx"));
            Assert.AreEqual(@"c:\p\resources.kbres", ResourceNames.NeutralFileOf(@"c:\p\resources.fr.kbres"));
            var sats = ResourceNames.Satellites(@"c:\p\resources.kbres", _ => new[] { @"c:\p\resources.kbres", @"c:\p\resources.fr.kbres", @"c:\p\resources.de-DE.kbres", @"c:\p\other.fr.kbres" });
            CollectionAssert.AreEqual(new[] { "de-DE", "fr" }, sats.Select(s => s.Culture).ToArray());
        }

        [TestMethod]
        public void References_ParseAndFormat()
        {
            Assert.AreEqual("{Res logo}", ResourceNames.Reference("logo"));
            Assert.AreEqual("{Res logo, Source=app}", ResourceNames.Reference("logo", "app"));
            Assert.AreEqual(("logo", (string?)null), ResourceNames.ParseReference("{Res logo}")!.Value);
            Assert.AreEqual(("logo", (string?)"app"), ResourceNames.ParseReference(" {Res logo, Source=app.kbres} ")!.Value);
            Assert.AreEqual(("ok.Text", (string?)null), ResourceNames.ParseReference("{Res Key=ok.Text}")!.Value);
            Assert.IsNull(ResourceNames.ParseReference("{Binding logo}"));
            Assert.IsNull(ResourceNames.ParseReference("{Resx logo}"));
            Assert.IsNull(ResourceNames.ParseReference("images/logo.png"));
        }

        private sealed class FakeFs : IResourceFileSystem
        {
            public readonly Dictionary<string, byte[]> Files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

            public bool FileExists(string path) => Files.ContainsKey(Path.GetFullPath(path));

            public byte[] ReadAllBytes(string path) => Files[Path.GetFullPath(path)];

            public void WriteAllBytes(string path, byte[] bytes) => Files[Path.GetFullPath(path)] = bytes;

            public void CopyFile(string from, string to) => Files[Path.GetFullPath(to)] = Files[Path.GetFullPath(from)];
        }

        private static ResourceSetModel NewModel(FakeFs fs, string? fr = null) =>
            new ResourceSetModel(@"c:\proj\src\resources.kbres", "<Resources><String Name=\"hello\">Hello</String><String Name=\"bye\">Bye</String></Resources>", fr is null ? Array.Empty<(string, string)>() : new[] { ("fr", fr) }, fs);

        [TestMethod]
        public void Model_EditsAllCultures_AsSingleUndoSteps()
        {
            var fs = new FakeFs();
            var model = NewModel(fs, "<Resources><String Name=\"hello\">Bonjour</String></Resources>");
            var changes = 0;
            model.Changed += (_, _) => changes++;

            model.Rename("hello", "greeting");
            Assert.IsNotNull(model.Translation("greeting", "fr"));
            Assert.AreEqual("Bonjour", model.Translation("greeting", "fr")!.Text);

            model.SetValue("bye", "fr", "Au revoir");
            model.SetValue("bye", "de-DE", "Tschüss");
            CollectionAssert.AreEqual(new[] { "de-DE", "fr" }, model.Cultures.ToArray());
            Assert.IsTrue(model.Texts()[@"c:\proj\src\resources.fr.kbres"].Contains("<String Name=\"greeting\">Bonjour</String>\n  <String Name=\"bye\">Au revoir</String>"), "translations keep the neutral order");
            CollectionAssert.AreEqual(new[] { "greeting" }, model.MissingTranslations("de-DE").ToArray());

            model.SetValue("bye", "fr", string.Empty);
            Assert.IsNull(model.Translation("bye", "fr"), "an empty translation falls back to the neutral value");

            var added = model.AddString();
            Assert.AreEqual("String1", added.Name);
            Assert.AreEqual("String2", model.AddString().Name);
            model.SetComment("String1", "a note");
            model.Remove("greeting");
            Assert.IsNull(model.Translation("greeting", "fr"));

            Assert.ThrowsExactly<ArgumentException>(() => model.Rename("bye", "String1"));
            Assert.ThrowsExactly<ArgumentException>(() => model.Rename("bye", "a b"));

            var before = changes;
            model.Undo(); // remove
            Assert.IsNotNull(model.Get("greeting"));
            Assert.AreEqual("Bonjour", model.Translation("greeting", "fr")!.Text);
            model.Undo(); // comment
            Assert.IsNull(model.Get("String1")!.Comment);
            model.Redo();
            Assert.AreEqual("a note", model.Get("String1")!.Comment);
            Assert.IsTrue(changes > before);
            while (model.CanUndo)
            {
                model.Undo();
            }

            Assert.IsNotNull(model.Get("hello"));
            CollectionAssert.AreEqual(new[] { "fr" }, model.Cultures.ToArray());
            Assert.IsFalse(model.CanUndo);
            Assert.IsTrue(model.CanRedo);
        }

        [TestMethod]
        public void Model_AddsFiles_LinkedOrEmbedded_AndSwitchesPersistence()
        {
            var fs = new FakeFs();
            fs.Files[@"c:\elsewhere\Logo.png"] = new byte[] { 1, 2, 3 };
            fs.Files[@"c:\proj\src\img\local.ico"] = new byte[] { 0, 0, 1, 0 };
            var model = NewModel(fs);

            var logo = model.AddFile(@"c:\elsewhere\Logo.png");
            Assert.AreEqual(ResourceKind.Image, logo.Kind);
            Assert.AreEqual("Resources/Logo.png", logo.Path, "an outside file is copied into the set's Resources folder");
            Assert.IsTrue(fs.FileExists(@"c:\proj\src\Resources\Logo.png"));
            var icon = model.AddFile(@"c:\proj\src\img\local.ico");
            Assert.AreEqual(ResourceKind.Icon, icon.Kind);
            Assert.AreEqual("img/local.ico", icon.Path, "a file of the project stays where it is");
            Assert.AreEqual("Logo2", model.AddFile(@"c:\elsewhere\Logo.png", name: null).Name, "a free name");

            model.SetPersistence("Logo", ResourcePersistence.Embedded);
            Assert.AreEqual(ResourcePersistence.Embedded, model.Get("Logo")!.Persistence);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, model.BytesOf("Logo"));
            model.SetPersistence("Logo", ResourcePersistence.Linked);
            Assert.AreEqual("Resources/Logo3.png", model.Get("Logo")!.Path, "linking writes a fresh file");

            model.AddCulture("fr-fr");
            CollectionAssert.Contains(model.Cultures.ToArray(), "fr-FR");
            fs.Files[@"c:\elsewhere\Logo.fr.png"] = new byte[] { 9 };
            model.SetFileTranslation("Logo", "fr-FR", @"c:\elsewhere\Logo.fr.png");
            CollectionAssert.AreEqual(new byte[] { 9 }, model.BytesOf("Logo", "fr-FR"));
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, model.BytesOf("Logo", null));
            Assert.ThrowsExactly<ArgumentException>(() => model.AddCulture("french"));
            Assert.AreEqual(ResourceCategory.Icons, ResourceSetModel.CategoryOf(ResourceKind.Icon));
            Assert.AreEqual(1, model.InCategory(ResourceCategory.Icons).Count());
        }
    }
}
