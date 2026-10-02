using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Views.Tests.LanguageService
{
    /// <summary>
    /// The coloring of <c>.kbview</c>/<c>.kbcontrol</c> files (docs/INTELLISENSE.md, "Colorization"): Visual Studio's TextMate
    /// engine colors a <c>kbview</c> buffer (content type based on the language-server type, itself based on the TextMate
    /// colorization type) with <c>Grammars\Kbview\kbview.tmLanguage.json</c>, through the theme next to it. These tests
    /// pin each link of that chain, since a broken one colors nothing without any error: on 2026-10-02 every view had lost
    /// its colors because the theme named classification types that are not registered (the XML editor's "XML Name"...).
    /// </summary>
    [TestClass]
    public class KbviewColorizationTests
    {
        /// <summary>
        /// Classification types every Visual Studio session registers through MEF: the editor's predefined ones
        /// (PredefinedClassificationTypeNames, the names Visual Studio's own TextMate theme vs.tmTheme uses) and Roslyn's
        /// (always composed). A theme naming anything else colors those scopes with nothing.
        /// </summary>
        private static readonly HashSet<string> RegisteredClassifications = new(StringComparer.Ordinal)
        {
            "comment", "identifier", "keyword", "operator", "literal", "number", "string", "type", "other",
            "markup node", "markup attribute", "markup attribute value", "symbol definition", "symbol reference",
            "preprocessor keyword", "method name", "class name", "property name",
        };

        private static string RepositoryRoot()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Kubuno.VisualStudio.sln")))
            {
                dir = dir.Parent;
            }

            Assert.IsNotNull(dir, "Kubuno.VisualStudio.sln not found above the test assembly.");
            return dir!.FullName;
        }

        private static string Grammars => Path.Combine(RepositoryRoot(), "src", "Views", "Kubuno.Views", "Grammars");

        [TestMethod]
        public void Theme_NamesOnlyRegisteredClassificationTypes()
        {
            var theme = XDocument.Load(Path.Combine(Grammars, "kbview.tmLanguage.tmTheme"), LoadOptions.None);
            var names = theme.Descendants("key").Where(k => k.Value == "vsclassificationtype")
                .Select(k => ((XElement)k.NextNode!).Value).ToList();
            Assert.IsTrue(names.Count >= 6, "the theme maps the tags, attributes, values, comments, handlers and markup extensions");
            foreach (var name in names)
            {
                Assert.IsTrue(RegisteredClassifications.Contains(name), $"'{name}' is not a classification type every Visual Studio registers: its scopes would be colorless.");
            }
        }

        [TestMethod]
        public void Theme_SitsNextToTheGrammarUnderTheNameVisualStudioLooksFor()
        {
            // <grammar file name without its last extension>.tmTheme, like the Starter Kit's c.tmLanguage.json / c.tmLanguage.tmTheme.
            Assert.IsTrue(File.Exists(Path.Combine(Grammars, "kbview.tmLanguage.json")));
            Assert.IsTrue(File.Exists(Path.Combine(Grammars, "kbview.tmLanguage.tmTheme")));
            var grammar = File.ReadAllText(Path.Combine(Grammars, "kbview.tmLanguage.json"));
            StringAssert.Contains(grammar, "\"scopeName\": \"text.xml.kbview\"");
            StringAssert.Matches(grammar, new Regex("\"fileTypes\": \\[\\s*\"kbview\",\\s*\"kbcontrol\""));
            StringAssert.Contains(grammar, "(Binding|Res)", "{Binding ...} and {Res ...} are both colored as markup extensions");
        }

        [TestMethod]
        public void Grammar_HasNoBeginRuleWithoutAnEnd()
        {
            // Visual Studio's TextMate engine rejects the WHOLE grammar for one begin rule without an end (VS Code
            // tolerates it): the vendored XML grammar had two, in its comments, and colored nothing until 2026-10-02.
            using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(Grammars, "kbview.tmLanguage.json")));
            var broken = new List<string>();
            void Walk(System.Text.Json.JsonElement e, string path)
            {
                if (e.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    if (e.TryGetProperty("begin", out _) && !e.TryGetProperty("end", out _) && !e.TryGetProperty("while", out _))
                    {
                        broken.Add(path);
                    }

                    foreach (var p in e.EnumerateObject())
                    {
                        Walk(p.Value, path + "." + p.Name);
                    }
                }
                else if (e.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    var i = 0;
                    foreach (var item in e.EnumerateArray())
                    {
                        Walk(item, $"{path}[{i++}]");
                    }
                }
            }

            Walk(json.RootElement, "$");
            Assert.AreEqual(0, broken.Count, "begin rules without an end: " + string.Join(", ", broken));
        }

        [TestMethod]
        public void Grammar_HandlerRuleStartsWhereTheGenericAttributeRuleDoes()
        {
            // TextMate keeps the earliest match: a handler rule starting after the whitespace (a lookbehind) loses to
            // tagStuff's generic attribute rule, which starts on the whitespace, and handlers were never colored.
            var grammar = File.ReadAllText(Path.Combine(Grammars, "kbview.tmLanguage.json"));
            StringAssert.Contains(grammar, "\"match\": \"(?:^|\\\\s+)(On[A-Z][\\\\w]*)");
        }

        [TestMethod]
        public void Pkgdef_RegistersTheGrammarFolderAndThePackageShipsIt()
        {
            var pkgdef = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Views", "Kubuno.Views", "kbview-languages.pkgdef"));
            StringAssert.Matches(pkgdef, new Regex(@"\[\$RootKey\$\\TextMate\\Repositories\]\s*\r?\n""KubunoViews""=""\$PackageFolder\$\\Grammars\\Kbview"""));
            var vsix = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Kubuno.VisualStudio", "Kubuno.VisualStudio.csproj"));
            StringAssert.Contains(vsix, @"<Content Include=""..\Views\Kubuno.Views\Grammars\*.*"">");
            StringAssert.Contains(vsix, @"<Link>Grammars\Kbview\%(Filename)%(Extension)</Link>");
            StringAssert.Contains(vsix, @"<Content Include=""..\Views\Kubuno.Views\kbview-languages.pkgdef"">");
        }

        [TestMethod]
        public void ContentType_DerivesFromTheLanguageServerType_WhichTheTextMateColorizerServes()
        {
            // code-languageserver-preview (CodeRemoteContentDefinition.CodeRemoteContentTypeName) is itself based on
            // code-languageserver-textmate-color: the TextMate tagger colors every buffer of a type derived from it.
            var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Views", "Kubuno.Views", "LanguageService", "ContentDefinition.cs"));
            StringAssert.Contains(source, "[BaseDefinition(CodeRemoteContentDefinition.CodeRemoteContentTypeName)]");
            StringAssert.Contains(source, "[FileExtension(KbviewConstants.FileExtension)]");
            StringAssert.Contains(source, "[FileExtension(KbviewConstants.ControlFileExtension)]");
        }
    }
}
