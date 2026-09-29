using System.Linq;
using Kubuno.VisualStudio.Core.IntelliSense;
using Kubuno.VisualStudio.Core.QuickInfo;
using Kubuno.VisualStudio.Core.SolutionExplorer;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.VisualStudio.Tests.IntelliSense
{
    [TestClass]
    public sealed class RustCompletionPresentationTests
    {
        [TestMethod]
        public void KindsGetTheSameIconsAsQuickInfoAndSolutionExplorer()
        {
            Assert.AreEqual(RustCompletionCategory.Method, RustCompletionPresentation.Categorize(2, "len()", "fn(&self) -> usize"));
            Assert.AreEqual(RustCompletionCategory.Struct, RustCompletionPresentation.Categorize(22, "HashMap<…>", "HashMap<K, V>"));
            Assert.AreEqual(RustCompletionCategory.Trait, RustCompletionPresentation.Categorize(8, "Iterator", null));
            Assert.AreEqual(RustCompletionCategory.Macro, RustCompletionPresentation.Categorize(3, "println!(…)", "macro_rules! println"));
            Assert.AreEqual(RustCompletionCategory.Local, RustCompletionPresentation.Categorize(6, "vm", "&mut dyn ViewModel"));

            Assert.AreEqual(SymbolMonikerNames.ForRust(SolutionSymbolKind.Method, SymbolVisibility.Public), RustCompletionPresentation.IconMonikerName(RustCompletionCategory.Method));
            Assert.AreEqual(SymbolMonikerNames.ForRust(SolutionSymbolKind.Struct, SymbolVisibility.Public), RustCompletionPresentation.IconMonikerName(RustCompletionCategory.Struct));
            Assert.AreEqual("LocalVariable", RustCompletionPresentation.IconMonikerName(RustCompletionCategory.Local));
            Assert.AreEqual("IntellisenseKeyword", RustCompletionPresentation.IconMonikerName(RustCompletionCategory.Keyword));
        }

        /// <summary>Every icon of the list and its filter buttons must exist in the installed image catalog.</summary>
        [TestMethod]
        public void EveryIconAndFilterMonikerExistsInTheInstalledImageCatalog()
        {
            Assert.AreEqual(RustCompletionPresentation.Filters.Count, RustCompletionPresentation.Filters.Select(f => f.AccessKey).Distinct().Count(), "access keys are unique");

            var catalog = System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles),
                @"Microsoft Visual Studio\18\Community\Common7\IDE\Microsoft.VisualStudio.ImageCatalog.dll");
            if (!System.IO.File.Exists(catalog))
            {
                Assert.Inconclusive("Visual Studio 2026 Community is not installed at the expected path: " + catalog);
            }

            var knownMonikers = System.Reflection.Assembly.LoadFrom(catalog).GetType("Microsoft.VisualStudio.Imaging.KnownMonikers", throwOnError: true)!;
            var names = System.Enum.GetValues(typeof(RustCompletionCategory)).Cast<RustCompletionCategory>().Select(RustCompletionPresentation.IconMonikerName)
                .Concat(RustCompletionPresentation.Filters.Select(f => f.MonikerName))
                .Concat(new[] { "ExpandScope", "PropertyPublic", "MethodPublic" })
                .Distinct();
            var missing = names.Where(name => knownMonikers.GetProperty(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static) == null).ToList();
            Assert.AreEqual(0, missing.Count, "Unknown KnownMonikers: " + string.Join(", ", missing));
        }

        [TestMethod]
        public void ListTextsReadLikeCSharp()
        {
            Assert.AreEqual("len", RustCompletionPresentation.DisplayText("len()", RustCompletionCategory.Method));
            Assert.AreEqual("with_capacity", RustCompletionPresentation.DisplayText("with_capacity(…)", RustCompletionCategory.Function));
            Assert.AreEqual("HashMap<>", RustCompletionPresentation.DisplayText("HashMap<…>", RustCompletionCategory.Struct));
            Assert.AreEqual("println!(…)", RustCompletionPresentation.DisplayText("println!(…)", RustCompletionCategory.Macro));
        }

        [TestMethod]
        public void APunctuationCommitInsertsTheBareName()
        {
            Assert.AreEqual("Vec", RustCompletionPresentation.BareName("Vec<…>"));
            Assert.AreEqual("push", RustCompletionPresentation.BareName("push(…)"));
            Assert.AreEqual("println!", RustCompletionPresentation.BareName("println!(…)"));
            Assert.AreEqual("vec!", RustCompletionPresentation.BareName("vec![…]"));
            Assert.AreEqual("status", RustCompletionPresentation.BareName("status"));
        }

        [TestMethod]
        public void OnlyTheClearlyMoreRelevantItemsAreStarred()
        {
            // Real rust-analyzer sortTexts: one preselected local (7fffffd1), two locals, the rest at the common score.
            var sortTexts = new[] { "7ffffff6", "7ffffff6", "7fffffd1", "7ffffff4", "7ffffff4", "7ffffff6", "7ffffff6", "7ffffff6" };

            CollectionAssert.AreEqual(new[] { 2 }, RustCompletionPresentation.StarredIndexes(sortTexts).ToArray());
            Assert.AreEqual(0, RustCompletionPresentation.StarredIndexes(new[] { "7ffffff6", "7ffffff6" }).Count);
            Assert.AreEqual(0, RustCompletionPresentation.StarredIndexes(new[] { "7ffffff6", "7ffffff6", "7ffffff6", "7ffffff6", "x" }).Count);
        }

        [TestMethod]
        public void SignaturesAreWrittenAsDeclarations()
        {
            Assert.AreEqual("fn len(&self) -> usize", RustCompletionPresentation.Signature(RustCompletionCategory.Method, "len", "fn(&self) -> usize"));
            Assert.AreEqual("const fn new() -> Vec<T, Global>", RustCompletionPresentation.Signature(RustCompletionCategory.Function, "new", "const fn() -> Vec<T, Global>"));
            Assert.AreEqual("let vm: &mut dyn ViewModel", RustCompletionPresentation.Signature(RustCompletionCategory.Local, "vm", "&mut dyn ViewModel"));
            Assert.AreEqual("status: String", RustCompletionPresentation.Signature(RustCompletionCategory.Field, "status", "String"));
            Assert.AreEqual("struct MainViewModel", RustCompletionPresentation.Signature(RustCompletionCategory.Struct, "MainViewModel", "MainViewModel"));
            Assert.AreEqual("macro_rules! println", RustCompletionPresentation.Signature(RustCompletionCategory.Macro, "println!", null));
        }

        [TestMethod]
        public void TheDescriptionHasTheIconTheColoredSignatureTheImportAndTheDocs()
        {
            var description = RustCompletionPresentation.Description(RustCompletionCategory.Struct, "HashMap<>", "HashMap<K, V>", "A **hash** map.", "std::collections::HashMap", french: false);

            var text = description.ToPlainText();
            StringAssert.Contains(text, "struct HashMap");
            StringAssert.Contains(text, "Imports std::collections::HashMap");
            StringAssert.Contains(text, "hash");
            var header = (QuickInfoContainer)((QuickInfoContainer)description).Children[0];
            Assert.IsInstanceOfType(header.Children[0], typeof(QuickInfoImage));
        }
    }
}
