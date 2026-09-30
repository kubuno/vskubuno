using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kubuno.Core.Logic.QuickInfo;
using Kubuno.Rust.Logic.QuickInfo;
using Kubuno.Rust.Logic.SolutionExplorer;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Rust.Tests.QuickInfo
{
    /// <summary>Real rust-analyzer hovers (Fixtures/Hover) turned into Visual Studio-style QuickInfo elements.</summary>
    [TestClass]
    public class RustHoverTests
    {
        internal static string Fixture(string name) =>
            File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fixtures", "Hover", name + ".md"));

        private static RustHover Hover(string fixture) => RustHover.Parse(Fixture(fixture)) ?? throw new AssertFailedException("no hover parsed");

        internal static QuickInfoTextKind KindOf(IEnumerable<QuickInfoRun> runs, string text)
        {
            var run = runs.FirstOrDefault(r => r.Text == text) ?? throw new AssertFailedException($"no run '{text}' in: {string.Join("|", runs.Select(r => r.Text))}");
            return run.Kind;
        }

        /// <summary>The header row: Wrapped[image, text or Stacked[text, path]].</summary>
        internal static (QuickInfoImage Icon, QuickInfoText Signature, string? Path) Header(QuickInfoElement tooltip)
        {
            var root = (QuickInfoContainer)tooltip;
            Assert.AreEqual(QuickInfoContainerStyle.Stacked | QuickInfoContainerStyle.VerticalPadding, root.Style);
            var header = (QuickInfoContainer)root.Children[0];
            Assert.AreEqual(QuickInfoContainerStyle.Wrapped, header.Style);
            var icon = (QuickInfoImage)header.Children[0];
            if (header.Children[1] is QuickInfoText signature)
            {
                return (icon, signature, null);
            }

            var stacked = (QuickInfoContainer)header.Children[1];
            var path = (QuickInfoText)stacked.Children[1];
            Assert.IsTrue(path.Runs.All(r => r.Kind == QuickInfoTextKind.Muted), "the path is grey");
            return (icon, (QuickInfoText)stacked.Children[0], path.ToPlainText());
        }

        [TestMethod]
        public void AnAssociatedFunctionShowsAMethodIconItsSignatureOnOneLineAndItsTypeAsPath()
        {
            var hover = Hover("fn-new");
            Assert.AreEqual("hoverprobe::shapes::Point", hover.Container);
            Assert.AreEqual("pub fn new(x: f64, y: f64) -> Self", hover.Signature);
            Assert.AreEqual(RustItemKind.Method, hover.Kind);

            var (icon, signature, path) = Header(hover.ToQuickInfo());
            Assert.AreEqual("MethodPublic", icon.MonikerName);
            Assert.AreEqual("hoverprobe::shapes::Point", path);
            Assert.AreEqual(QuickInfoTextKind.Keyword, KindOf(signature.Runs, "pub"));
            Assert.AreEqual(QuickInfoTextKind.Method, KindOf(signature.Runs, "new"));
            Assert.AreEqual(QuickInfoTextKind.Parameter, KindOf(signature.Runs, "x"));
            Assert.AreEqual(QuickInfoTextKind.Keyword, KindOf(signature.Runs, "f64"));
            Assert.AreEqual(QuickInfoTextKind.Operator, KindOf(signature.Runs, "->"));
            Assert.AreEqual(QuickInfoTextKind.Keyword, KindOf(signature.Runs, "Self"));
        }

        [TestMethod]
        public void TheDocSummaryIsKeptAndPanicsIsShownLikeRoslynsExceptionsSection()
        {
            var tooltip = (QuickInfoContainer)Hover("fn-new").ToQuickInfo();
            var docs = tooltip.Children[1].ToPlainText();
            StringAssert.StartsWith(docs, "Creates a point at (x, y).");
            StringAssert.Contains(docs, "Panics");
            StringAssert.Contains(docs, "Never.");

            // Inline code is in the code font, the heading bold.
            var runs = AllRuns(tooltip.Children[1]).ToList();
            Assert.IsTrue(runs.Single(r => r.Text == "x").Style.HasFlag(QuickInfoRunStyle.Code));
            Assert.IsTrue(runs.Single(r => r.Text == "Panics").Style.HasFlag(QuickInfoRunStyle.Bold));
        }

        [TestMethod]
        public void AStructDropsItsBodyAndKeepsLinksListsAndClassifiedExamples()
        {
            var hover = Hover("struct-point");
            Assert.AreEqual("pub struct Point", hover.Signature);
            Assert.AreEqual(RustItemKind.Struct, hover.Kind);
            Assert.AreEqual("hoverprobe::shapes", hover.Container);

            var tooltip = (QuickInfoContainer)hover.ToQuickInfo();
            var (icon, signature, _) = Header(tooltip);
            Assert.AreEqual("StructurePublic", icon.MonikerName);
            Assert.AreEqual(QuickInfoTextKind.Struct, KindOf(signature.Runs, "Point"));

            var runs = AllRuns(tooltip.Children[1]).ToList();
            var link = runs.Single(r => r.Text == "Point::new");
            Assert.AreEqual("https://docs.rs/hoverprobe/0.1.0/hoverprobe/shapes/struct.Point.html#method.new", link.Url);
            Assert.IsTrue(link.Style.HasFlag(QuickInfoRunStyle.Code));

            // "- **x** grows to the right": a bullet row with a bold run.
            Assert.IsTrue(runs.Any(r => r.Text.Trim() == "•"));
            Assert.IsTrue(runs.Single(r => r.Text == "x" && r.Style.HasFlag(QuickInfoRunStyle.Bold)) != null);
            Assert.IsTrue(runs.Single(r => r.Text == "y" && r.Style.HasFlag(QuickInfoRunStyle.Italic)) != null);

            // The code example: classified, in the code font.
            var assert = runs.First(r => r.Text == "assert_eq");
            Assert.AreEqual(QuickInfoTextKind.Macro, assert.Kind);
            Assert.IsTrue(assert.Style.HasFlag(QuickInfoRunStyle.Code));
            Assert.AreEqual(QuickInfoTextKind.Keyword, runs.First(r => r.Text == "let").Kind);
        }

        [TestMethod]
        public void AStdMethodDropsTheImplLineAndInheritedBoundsAndShowsTheSubstitutionsAsNotes()
        {
            var hover = Hover("std-insert");
            Assert.AreEqual("pub fn insert(&mut self, k: K, v: V) -> Option<V>", hover.Signature);
            Assert.AreEqual(RustItemKind.Method, hover.Kind);
            Assert.AreEqual("std::collections::hash::map::HashMap", hover.Container);
            CollectionAssert.AreEqual(new[] { "K = String, V = Vec<u8>, S = RandomState, A = Global" }, hover.Notes.ToArray());

            var (_, signature, _) = Header(hover.ToQuickInfo());
            Assert.AreEqual(QuickInfoTextKind.Class, KindOf(signature.Runs, "Option"));
            Assert.AreEqual(QuickInfoTextKind.Keyword, KindOf(signature.Runs, "self"));

            // A method's own where clause stays, its bounds colored as traits.
            var own = RustSyntax.ToOneLine("pub fn get<Q>(&self, k: &Q) -> Option<&V>\nwhere\n    K: Borrow<Q>,\n    Q: Hash + Eq + ?Sized,", "std::collections::HashMap", out _);
            Assert.AreEqual("pub fn get<Q>(&self, k: &Q) -> Option<&V> where K: Borrow<Q>, Q: Hash + Eq + ?Sized", own);
            var runs = RustSyntax.ClassifySignature(own, RustItemKind.Method);
            Assert.AreEqual(QuickInfoTextKind.Trait, KindOf(runs, "Borrow"));
            Assert.AreEqual(QuickInfoTextKind.Trait, KindOf(runs, "Hash"));
            Assert.AreEqual(QuickInfoTextKind.Keyword, KindOf(runs, "where"));
        }

        [TestMethod]
        public void GenericsLifetimesAndWhereClausesAreColored()
        {
            var hover = Hover("fn-generic");
            Assert.AreEqual("fn generic_fn<T, const N: usize>(items: [T; {const}]) -> Vec<T> where T: Clone + fmt::Debug, T: Send", hover.Signature);
            Assert.AreEqual(RustItemKind.Function, hover.Kind);
            Assert.AreEqual(SymbolVisibility.Private, hover.Visibility);
            Assert.AreEqual("MethodPrivate", hover.IconMonikerName);

            var runs = RustSyntax.ClassifySignature(hover.Signature, hover.Kind);
            Assert.IsTrue(runs.Where(r => r.Text == "T").All(r => r.Kind == QuickInfoTextKind.TypeParameter));
            Assert.AreEqual(QuickInfoTextKind.TypeParameter, KindOf(runs, "N"));
            Assert.AreEqual(QuickInfoTextKind.Namespace, KindOf(runs, "fmt"));
            Assert.AreEqual(QuickInfoTextKind.Trait, KindOf(runs, "Debug"));
            Assert.AreEqual(QuickInfoTextKind.Class, KindOf(runs, "Vec"));

            var lifetimes = RustSyntax.ClassifySignature(Hover("fn-distance-to").Signature, RustItemKind.Method);
            Assert.AreEqual(QuickInfoTextKind.TypeParameter, KindOf(lifetimes, "'a"));
            Assert.AreEqual(QuickInfoTextKind.Parameter, KindOf(lifetimes, "other"));
            Assert.AreEqual(QuickInfoTextKind.Class, KindOf(lifetimes, "Point"));
        }

        [TestMethod]
        [DataRow("field-x", "pub x: f64", RustItemKind.Field, "FieldPublic")]
        [DataRow("field-y", "pub(crate) y: f64", RustItemKind.Field, "FieldInternal")]
        [DataRow("const-origin", "pub(crate) const ORIGIN: Point = Point { x: 0.0, y: 0.0 }", RustItemKind.Const, "ConstantInternal")]
        [DataRow("macro-double", "macro_rules! double", RustItemKind.Macro, "MacroPrivate")]
        [DataRow("std-println", "macro_rules! println", RustItemKind.Macro, "MacroPrivate")]
        [DataRow("trait-shape", "pub trait Shape", RustItemKind.Trait, "InterfacePublic")]
        [DataRow("trait-method-area", "pub fn area(&self) -> f64", RustItemKind.Method, "MethodPublic")]
        [DataRow("kv-method-text", "pub fn text(&self) -> String", RustItemKind.Method, "MethodPublic")]
        [DataRow("enum-kind", "pub enum Kind", RustItemKind.Enum, "EnumerationPublic")]
        [DataRow("type-named", "pub type Named<'a> = std::collections::HashMap<&'a str, Point>", RustItemKind.TypeAlias, "TypeDefinitionPublic")]
        [DataRow("std-hashmap", "pub struct HashMap<K, V, S = RandomState, A = Global>", RustItemKind.Struct, "StructurePublic")]
        [DataRow("std-string", "pub struct String", RustItemKind.Struct, "StructurePublic")]
        [DataRow("local-d", "let d: f64", RustItemKind.Local, "LocalVariable")]
        [DataRow("param-other", "other: &'a Point", RustItemKind.Parameter, "Parameter")]
        [DataRow("type-param-t", "T: Clone + Debug + Send", RustItemKind.TypeParameter, "Type")]
        [DataRow("keyword-fn", "fn", RustItemKind.Keyword, "IntellisenseKeyword")]
        [DataRow("variant-circle", "Circle(f64)", RustItemKind.Variant, "EnumerationItemPublic")]
        [DataRow("module-shapes", "pub mod shapes", RustItemKind.Module, "ModulePublic")]
        public void EveryKindOfItemGetsItsSignatureAndRoslynIcon(string fixture, string signature, RustItemKind kind, string moniker)
        {
            var hover = Hover(fixture);
            Assert.AreEqual(signature, hover.Signature);
            Assert.AreEqual(kind, hover.Kind);
            Assert.AreEqual(moniker, hover.IconMonikerName);
        }

        [TestMethod]
        public void TheDeclaredNameIsColoredByWhatItIs()
        {
            Assert.AreEqual(QuickInfoTextKind.Field, KindOf(RustSyntax.ClassifySignature("pub x: f64", RustItemKind.Field), "x"));
            Assert.AreEqual(QuickInfoTextKind.Local, KindOf(RustSyntax.ClassifySignature("let d: f64", RustItemKind.Local), "d"));
            Assert.AreEqual(QuickInfoTextKind.EnumMember, KindOf(RustSyntax.ClassifySignature("Circle(f64)", RustItemKind.Variant), "Circle"));
            Assert.AreEqual(QuickInfoTextKind.Constant, KindOf(RustSyntax.ClassifySignature("pub(crate) const ORIGIN: Point = Point { x: 0.0, y: 0.0 }", RustItemKind.Const), "ORIGIN"));
            Assert.AreEqual(QuickInfoTextKind.Trait, KindOf(RustSyntax.ClassifySignature("pub trait Shape", RustItemKind.Trait), "Shape"));
            Assert.AreEqual(QuickInfoTextKind.Enum, KindOf(RustSyntax.ClassifySignature("pub enum Kind", RustItemKind.Enum), "Kind"));
            Assert.AreEqual(QuickInfoTextKind.Macro, KindOf(RustSyntax.ClassifySignature("macro_rules! double", RustItemKind.Macro), "double"));
            Assert.AreEqual(QuickInfoTextKind.Namespace, KindOf(RustSyntax.ClassifySignature("pub mod shapes", RustItemKind.Module), "shapes"));
        }

        [TestMethod]
        public void LayoutAndVarianceFactsBecomeGreyNotesNotDocumentation()
        {
            var kind = Hover("enum-kind");
            CollectionAssert.AreEqual(new[] { "size = 16 (0x10), align = 0x8, no Drop" }, kind.Notes.ToArray());
            Assert.AreEqual("Kinds of shapes.", kind.Documentation);

            var shape = Hover("trait-shape");
            CollectionAssert.AreEqual(new[] { "Is dyn-compatible" }, shape.Notes.ToArray());
            Assert.AreEqual("Something with an area.", shape.Documentation);

            Assert.AreEqual(string.Empty, Hover("field-y").Documentation);
            var tooltip = (QuickInfoContainer)Hover("field-y").ToQuickInfo();
            Assert.IsTrue(AllRuns(tooltip.Children.Last()).All(r => r.Kind == QuickInfoTextKind.Muted));
        }

        [TestMethod]
        public void LongStdDocumentationIsCutAfterTheSummaryWithAnEllipsis()
        {
            var tooltip = (QuickInfoContainer)Hover("std-string").ToQuickInfo();
            var docs = tooltip.Children[1];
            var text = docs.ToPlainText();
            StringAssert.StartsWith(text, "A UTF-8");
            Assert.IsFalse(text.Contains("# Examples"));
            Assert.IsTrue(text.Length <= DocumentationRenderer.DefaultBudget + 200, text.Length.ToString());

            // The "Examples" section and later ones are dropped: the summary alone remains.
            Assert.IsFalse(text.Contains("sparkle_heart"));

            var println = (QuickInfoContainer)Hover("std-println").ToQuickInfo();
            var printlnDocs = println.Children[1].ToPlainText();
            StringAssert.Contains(printlnDocs, "Prints to the standard output, with a newline.");
            StringAssert.Contains(printlnDocs, "Panics");
            Assert.IsFalse(printlnDocs.Contains("prints just a newline"), "the Examples section is not shown");

            // Reference-style link definitions ([`std::fmt`]: https://...) resolve and are not shown as text.
            var links = AllRuns(println.Children[1]).Where(r => r.Url != null).ToList();
            Assert.IsTrue(links.Any(r => r.Text == "std::fmt" && r.Url!.StartsWith("https://doc.rust-lang.org/", StringComparison.Ordinal)));
            Assert.IsFalse(printlnDocs.Contains("]: https://"));
        }

        [TestMethod]
        public void AnUnresolvedIntraDocLinkShowsItsCodeWithoutBrackets()
        {
            var runs = Markdown.ParseInline("the primitive [`str`] and [plain]");
            Assert.AreEqual("the primitive str and [plain]", string.Concat(runs.Select(r => r.Text)));
            Assert.IsTrue(runs.Single(r => r.Text == "str").Style.HasFlag(QuickInfoRunStyle.Code));
            Assert.IsNull(runs.Single(r => r.Text == "str").Url);
        }

        [TestMethod]
        public void KeywordDocsKeepTheirSummaryAndClassifiedExample()
        {
            var tooltip = (QuickInfoContainer)Hover("keyword-fn").ToQuickInfo();
            var docs = (QuickInfoContainer)tooltip.Children[1];
            StringAssert.StartsWith(docs.ToPlainText(), "A function or function pointer.");
            var code = docs.Children.OfType<QuickInfoContainer>().First(c => c.Style == QuickInfoContainerStyle.Stacked && c.Children.OfType<QuickInfoText>().Any());
            Assert.IsTrue(code.Children.Count <= DocumentationRenderer.MaxCodeLines + 1);
            var runs = AllRuns(code).ToList();
            Assert.AreEqual(QuickInfoTextKind.Method, runs.First(r => r.Text == "standalone_function").Kind);
            Assert.AreEqual(QuickInfoTextKind.Comment, runs.First(r => r.Text.StartsWith("//", StringComparison.Ordinal)).Kind);
            Assert.IsTrue(runs.All(r => r.Style.HasFlag(QuickInfoRunStyle.Code)));
        }

        [TestMethod]
        public void KubunoViewsHoversKeepTheirPathAndDocs()
        {
            var sender = Hover("kv-struct-sender");
            Assert.AreEqual("pub struct Sender<'a, C>", sender.Signature);
            Assert.AreEqual("kubuno_views::events::sender", sender.Container);
            var runs = RustSyntax.ClassifySignature(sender.Signature, sender.Kind);
            Assert.AreEqual(QuickInfoTextKind.TypeParameter, KindOf(runs, "C"));
            Assert.AreEqual(QuickInfoTextKind.Struct, KindOf(runs, "Sender"));

            var clicks = Hover("kv-field-clicks");
            Assert.AreEqual("pub clicks: u8", clicks.Signature);
            Assert.AreEqual("kubuno_views::events::args::MouseEventArgs", clicks.Container);
            Assert.AreEqual("FieldPublic", clicks.IconMonikerName);

            var text = Hover("kv-method-text");
            Assert.AreEqual("MethodPublic", text.IconMonikerName);
            StringAssert.Contains(text.ToQuickInfo().ToPlainText(), "((Button)sender).Text");

            Assert.AreEqual("InterfacePublic", Hover("kv-trait-viewmodel").IconMonikerName);
            Assert.AreEqual("ModulePublic", Hover("kv-module-prelude").IconMonikerName);
        }

        [TestMethod]
        public void OnlyTheFirstOfSeveralJoinedHoverResultsIsShown()
        {
            var hover = Hover("kv-struct-mouseeventargs-multi");
            Assert.AreEqual("pub struct MouseEventArgs", hover.Signature);
            StringAssert.StartsWith(hover.Documentation, "Mouse event args");
            Assert.IsFalse(hover.Documentation.Contains("core"), hover.Documentation);
        }

        [TestMethod]
        public void MultiLineSignaturesAreJoined()
        {
            var code = "pub fn frame_typed<V: ViewModel>(\n    &mut self,\n    canvas: &mut Canvas,\n    body: Rect,\n) -> Vec<Event>\nwhere\n    V: 'static,";
            Assert.AreEqual("pub fn frame_typed<V: ViewModel>(&mut self, canvas: &mut Canvas, body: Rect) -> Vec<Event> where V: 'static",
                RustSyntax.ToOneLine(code, "kubuno_views::runtime::Runtime", out var kind));
            Assert.AreEqual(RustItemKind.Method, kind);
        }

        [TestMethod]
        public void ANonRustAnalyzerShapeIsShownAsDocumentation()
        {
            var hover = RustHover.Parse("Just some **text**.")!;
            Assert.AreEqual(string.Empty, hover.Signature);
            var tooltip = (QuickInfoContainer)hover.ToQuickInfo();
            Assert.AreEqual("Just some text.", tooltip.ToPlainText());
            Assert.IsNull(RustHover.Parse("  "));
        }

        internal static IEnumerable<QuickInfoRun> AllRuns(QuickInfoElement element)
        {
            switch (element)
            {
                case QuickInfoText text:
                    return text.Runs;
                case QuickInfoContainer container:
                    return container.Children.SelectMany(AllRuns);
                default:
                    return Enumerable.Empty<QuickInfoRun>();
            }
        }
    }
}
