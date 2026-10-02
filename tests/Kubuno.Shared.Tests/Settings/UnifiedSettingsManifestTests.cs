using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Shared.Tests.Settings
{
    /// <summary>The unified settings manifest (Tools &gt; Options &gt; Kubuno) agrees with the options pages and the resources.</summary>
    [TestClass]
    public sealed class UnifiedSettingsManifestTests
    {
        private static string ProjectDirectory()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Kubuno.VisualStudio.sln")))
            {
                dir = dir.Parent;
            }

            Assert.IsNotNull(dir, "repository root not found");
            return Path.Combine(dir!.FullName, "src", "Kubuno.VisualStudio");
        }

        private static JsonObject Manifest() =>
            (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(ProjectDirectory(), "UnifiedSettings", "kubuno.registration.json")))!;

        private static string[] ResourceNames(string file) =>
            XDocument.Load(Path.Combine(ProjectDirectory(), "Resources", file)).Descendants("data").Select(d => (string)d.Attribute("name")!).ToArray();

        private static string ResourceOf(string reference)
        {
            Assert.IsTrue(reference.StartsWith("@", StringComparison.Ordinal) && reference.EndsWith(";..\\Kubuno.VisualStudio.dll", StringComparison.Ordinal), reference);
            return reference.Substring(1, reference.IndexOf(';') - 1);
        }

        [TestMethod]
        public void EveryLabelExistsInEnglishAndFrench()
        {
            var manifest = Manifest();
            var english = ResourceNames("Settings.resx");
            var french = ResourceNames("Settings.fr.resx");
            CollectionAssert.AreEquivalent(english, french);

            var references = manifest["properties"]!.AsObject()
                .SelectMany(p => new[] { p.Value!["title"], p.Value["description"] }.Concat(p.Value["enumItemLabels"]?.AsArray() ?? new JsonArray()))
                .Concat(manifest["categories"]!.AsObject().SelectMany(c => new[] { c.Value!["title"], c.Value["description"] }));
            foreach (var reference in references)
            {
                Assert.IsTrue(english.Contains(ResourceOf((string)reference!)), (string)reference!);
            }
        }

        [TestMethod]
        public void EveryPropertyHasItsCategoriesAndEnumLabels()
        {
            var manifest = Manifest();
            var categories = manifest["categories"]!.AsObject().Select(c => c.Key).ToHashSet();
            foreach (var entry in manifest["properties"]!.AsObject())
            {
                string moniker = entry.Key;
                var property = entry.Value;
                var parent = moniker.Substring(0, moniker.LastIndexOf('.'));
                Assert.IsTrue(categories.Contains(parent), $"{moniker}: no category {parent}");
                if (property!["enum"] is JsonArray values)
                {
                    Assert.AreEqual(values.Count, property["enumItemLabels"]!.AsArray().Count, moniker);
                    Assert.IsTrue(values.Any(v => (string)v! == (string)property["default"]!), $"{moniker}: default not in enum");
                }
            }

            foreach (var category in categories.Where(c => c != "kubuno"))
            {
                Assert.IsTrue(categories.Contains(category.Substring(0, category.LastIndexOf('.'))), category);
            }
        }

        [TestMethod]
        public void EveryOptionsPagePropertyIsInTheManifest()
        {
            var monikers = Manifest()["properties"]!.AsObject().Select(p => p.Key).ToArray();
            var srcDirectory = Directory.GetParent(ProjectDirectory())!.FullName;
            var used = Directory.GetFiles(srcDirectory, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains("\\obj\\") && !f.Contains("\\bin\\"))
                .SelectMany(f => Regex.Matches(File.ReadAllText(f), "\\[UnifiedSetting\\(\"([^\"]+)\"\\)\\]").Cast<Match>().Select(m => m.Groups[1].Value))
                .ToArray();

            CollectionAssert.AreEquivalent(monikers, used);
        }
    }
}
