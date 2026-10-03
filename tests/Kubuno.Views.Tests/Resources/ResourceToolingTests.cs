using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Kubuno.Views.Designer.DesignSurface;
using Kubuno.Views.Logic.Resources;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Views.Tests.Resources
{
    /// <summary>F12 from the generated accessors, the designer's setResources message, and the project resource scan.</summary>
    [TestClass]
    public class ResourceToolingTests
    {
        [TestMethod]
        public void Navigation_ParsesMacroCalls()
        {
            var call = ResourceNavigation.ParseCall("kubuno_desktop::resources!(\"resources.kbres\");")!;
            Assert.AreEqual("resources.kbres", call.Path);
            Assert.AreEqual("Resources", call.TypeName);
            call = ResourceNavigation.ParseCall("    kubuno_desktop::resources!(pub(crate) Strings, \"i18n/strings.kbres\");")!;
            Assert.AreEqual("Strings", call.TypeName);
            Assert.AreEqual("i18n/strings.kbres", call.Path);
            Assert.AreEqual("MainView", ResourceNavigation.ParseCall("resources!(\"main_view.kbres\")")!.TypeName);
            Assert.IsNull(ResourceNavigation.ParseCall("data_source!(\"shop.kbdata\")"));
        }

        [TestMethod]
        public void Navigation_FindsTheEntryOfAnAccessor()
        {
            const string code = "let t = Resources::welcome_text();\nlet l = crate::Resources::logo();";
            var at = code.IndexOf("welcome_text", StringComparison.Ordinal) + 3;
            Assert.AreEqual(("welcome_text", (string?)"Resources"), ResourceNavigation.IdentifierAt(code, at));
            Assert.AreEqual(("Resources", (string?)"crate"), ResourceNavigation.IdentifierAt(code, code.LastIndexOf("Resources", StringComparison.Ordinal) + 1));

            const string kbres = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<Resources Version=\"1\">\n  <String Name=\"WelcomeText\">Hi</String>\n  <Image Name=\"logo\" File=\"logo.png\"/>\n</Resources>\n";
            var call = new ResourceNavigation.MacroCall("resources.kbres", "Resources");
            Assert.AreEqual((2, 16), ResourceNavigation.Target(call, kbres, "welcome_text", "Resources"));
            Assert.AreEqual((3, 15), ResourceNavigation.Target(call, kbres, "logo", "Self"));
            Assert.AreEqual((0, 0), ResourceNavigation.Target(call, kbres, "Resources", "crate"));
            Assert.IsNull(ResourceNavigation.Target(call, kbres, "logo", "Other"));
            Assert.IsNull(ResourceNavigation.Target(call, kbres, "nothing", "Resources"));
        }

        [TestMethod]
        public void Navigation_ResolvesTheFileLikeTheMacro()
        {
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\p\src\res\resources.kbres" };
            Assert.AreEqual(@"C:\p\src\res\resources.kbres", ResourceNavigation.ResolveFile(@"C:\p\src\res\mod.rs", "resources.kbres", files.Contains));
            Assert.IsNull(ResourceNavigation.ResolveFile(@"C:\p\src\main.rs", "nope.kbres", files.Contains));
        }

        [TestMethod]
        public void SetResources_CarriesEverySetAndItsCultures()
        {
            var texts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [@"C:\p\src\resources.kbres"] = "<Resources><String Name=\"t\">Title</String></Resources>",
                [@"C:\p\src\resources.fr.kbres"] = "<Resources><String Name=\"t\">Titre</String></Resources>",
            };
            var json = DesignSurfaceResourcesProtocol.EncodeSetResources("fr", new[] { @"C:\p\src\resources.kbres" }, p => texts.TryGetValue(p, out var t) ? t : null, _ => texts.Keys);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            Assert.AreEqual("setResources", root.GetProperty("type").GetString());
            Assert.AreEqual("fr", root.GetProperty("culture").GetString());
            var set = root.GetProperty("sets")[0];
            Assert.AreEqual("resources", set.GetProperty("name").GetString());
            Assert.AreEqual(@"C:\p\src", set.GetProperty("baseDir").GetString());
            Assert.AreEqual("fr", set.GetProperty("satellites")[0].GetProperty("culture").GetString());
            StringAssert.Contains(set.GetProperty("satellites")[0].GetProperty("text").GetString(), "Titre");
        }

        [TestMethod]
        public void ProjectResources_ListsTheSetsOfTheProject()
        {
            var root = Path.Combine(Path.GetTempPath(), "kbres-project-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "src", "views"));
                Directory.CreateDirectory(Path.Combine(root, "target", "debug"));
                File.WriteAllText(Path.Combine(root, "Cargo.toml"), "[package]\nname = \"p\"\n");
                File.WriteAllText(Path.Combine(root, "src", "resources.kbres"), "<Resources><Image Name=\"logo\" File=\"logo.png\"/><String Name=\"t\">x</String></Resources>");
                File.WriteAllText(Path.Combine(root, "src", "resources.fr.kbres"), "<Resources/>");
                File.WriteAllText(Path.Combine(root, "src", "views", "main_view.kbres"), "<Resources><Image Name=\"logo\" Format=\"png\">AAEC</Image></Resources>");
                File.WriteAllText(Path.Combine(root, "target", "debug", "copy.kbres"), "<Resources/>");
                File.WriteAllBytes(Path.Combine(root, "src", "logo.png"), new byte[] { 1, 2 });
                var view = Path.Combine(root, "src", "views", "main_view.kbview");

                CollectionAssert.AreEqual(new[] { Path.Combine(root, "src", "resources.kbres"), Path.Combine(root, "src", "views", "main_view.kbres") }, ProjectResources.NeutralFiles(ProjectResources.ProjectRoot(view)).ToArray());
                var items = ProjectResources.Items(view);
                Assert.AreEqual(3, items.Count);
                var linked = ProjectResources.Find(items, "{Res logo, Source=resources}")!;
                CollectionAssert.AreEqual(new byte[] { 1, 2 }, linked.ReadBytes());
                Assert.AreEqual("{Res logo, Source=resources}", linked.Reference(qualified: true));
                CollectionAssert.AreEqual(new byte[] { 0, 1, 2 }, ProjectResources.Find(items, "{Res logo, Source=main_view}")!.ReadBytes());
                Assert.IsNull(ProjectResources.Find(items, "{Res none}"));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
