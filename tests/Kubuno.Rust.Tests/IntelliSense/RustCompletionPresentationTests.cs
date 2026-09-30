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
        public void OnlyRustAnalyzersTopRelevanceItemsAreStarred()
        {
            // Real rust-analyzer output for `Vec::`: `new` is preselected (best score); the rest are lower tiers.
            var sortTexts = new[] { "7fffffe8", "7fffffe7", "7fffffe8", "7ffffff2" };
            var preselect = new[] { false, true, false, false };

            CollectionAssert.AreEqual(new[] { 1 }, RustCompletionPresentation.StarredIndexes(sortTexts, preselect).ToArray());
            Assert.AreEqual(0, RustCompletionPresentation.StarredIndexes(sortTexts, new[] { false, false, false, false }).Count);
            // A preselected item that is not at the best score of the list is not top relevance.
            Assert.AreEqual(0, RustCompletionPresentation.StarredIndexes(sortTexts, new[] { true, false, false, false }).Count);
            Assert.AreEqual(0, RustCompletionPresentation.StarredIndexes(new[] { "x" }, new[] { true }).Count);
        }

        [TestMethod]
        public void TheListIsSortedByRelevanceTierThenConstructorsThenAlphabetically()
        {
            var keys = new[]
            {
                RustCompletionPresentation.SortKey("7fffffe8", RustCompletionCategory.Method, "swap_remove"),
                RustCompletionPresentation.SortKey("7fffffe8", RustCompletionCategory.Function, "with_capacity"),
                RustCompletionPresentation.SortKey("7fffffe7", RustCompletionCategory.Function, "new"),
                RustCompletionPresentation.SortKey("7ffffff2", RustCompletionCategory.Function, "a_lower_tier_function"),
                RustCompletionPresentation.SortKey("7fffffe8", RustCompletionCategory.Method, "remove"),
                RustCompletionPresentation.SortKey("7fffffe8", RustCompletionCategory.Function, "from_raw_parts"),
            };
            var order = keys.Select((key, i) => (key, i)).OrderBy(p => p.key, System.StringComparer.Ordinal).Select(p => p.i).ToArray();

            // new (best tier), the tier-e8 functions alphabetically, its methods alphabetically, then the lower tier.
            CollectionAssert.AreEqual(new[] { 2, 5, 1, 4, 0, 3 }, order);
        }

        [TestMethod]
        public void ConstructorsAfterATypePathAreLiftedJustBehindTheTopTier()
        {
            Assert.AreEqual("7fffffe8", RustCompletionPresentation.NextTier("7fffffe7"));
            Assert.IsNull(RustCompletionPresentation.NextTier("x"));
            Assert.IsTrue(RustCompletionPresentation.IsConstructorLike(RustCompletionCategory.Function, "const fn(usize) -> Vec<T, Global>", "Vec"));
            Assert.IsTrue(RustCompletionPresentation.IsConstructorLike(RustCompletionCategory.Function, "fn(T) -> Self", "Vec"));
            Assert.IsTrue(RustCompletionPresentation.IsConstructorLike(RustCompletionCategory.Function, "fn() -> Self", "Vec"));
            Assert.IsFalse(RustCompletionPresentation.IsConstructorLike(RustCompletionCategory.Function, "fn(&self) -> usize", "Vec"));
            Assert.IsFalse(RustCompletionPresentation.IsConstructorLike(RustCompletionCategory.Method, "fn(&self) -> Self", "Vec"));

            var new_ = RustCompletionPresentation.SortKey("7fffffe7", RustCompletionCategory.Function, "new", "7fffffe8");
            var from = RustCompletionPresentation.SortKey("7ffffffb", RustCompletionCategory.Function, "from", "7fffffe8");
            var swapRemove = RustCompletionPresentation.SortKey("7fffffe8", RustCompletionCategory.Method, "swap_remove");
            var withCapacity = RustCompletionPresentation.SortKey("7fffffe8", RustCompletionCategory.Function, "with_capacity", "7fffffe8");
            Assert.IsTrue(System.String.CompareOrdinal(withCapacity, from) < 0, "constructors of the tier itself come first");
            var pop = RustCompletionPresentation.SortKey("7ffffff2", RustCompletionCategory.Method, "pop");
            Assert.IsTrue(System.String.CompareOrdinal(new_, from) < 0);
            Assert.IsTrue(System.String.CompareOrdinal(from, swapRemove) < 0, "a lifted constructor leads the methods of its new tier");
            Assert.IsTrue(System.String.CompareOrdinal(swapRemove, pop) < 0);
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
