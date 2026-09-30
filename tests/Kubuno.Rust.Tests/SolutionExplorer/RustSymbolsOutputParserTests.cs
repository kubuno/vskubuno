using System.Collections.Generic;
using System.Linq;
using System.Text;
using Kubuno.VisualStudio.Core.SolutionExplorer;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.VisualStudio.Tests.SolutionExplorer
{
    [TestClass]
    public class RustSymbolsOutputParserTests
    {
        // A real `rust-analyzer symbols < sym.rs` run (rust-analyzer 1.98.1) over exactly this text.
        private const string Source =
            "/// Doc.\n#[derive(Debug)]\npub struct Foo {\n    pub a: i32,\n    b: u8,\n}\n\nimpl Foo {\n" +
            "    pub(crate) fn new(x: i32) -> Self { todo!() }\n    fn secret(&self) {}\n}\n\n" +
            "impl std::fmt::Display for Foo {\n    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result { Ok(()) }\n}\n\n" +
            "pub(super) enum E { A, B(u8) }\ntrait T { fn x(&self); }\npub const C: i32 = 1;\nstatic S: &str = \"é\";\n" +
            "pub mod m { pub fn f() {} }\n#[macro_export]\nmacro_rules! mac { () => {} }\ntype Alias = u8;\nfn main() {}\n";

        private const string Output =
            "StructureNode { parent: None, label: \"Foo\", navigation_range: 37..40, node_range: 0..71, kind: SymbolKind(Struct), detail: None, deprecated: false }\n" +
            "StructureNode { parent: Some(0), label: \"a\", navigation_range: 51..52, node_range: 47..57, kind: SymbolKind(Field), detail: Some(\"i32\"), deprecated: false }\n" +
            "StructureNode { parent: Some(0), label: \"b\", navigation_range: 63..64, node_range: 63..68, kind: SymbolKind(Field), detail: Some(\"u8\"), deprecated: false }\n" +
            "StructureNode { parent: None, label: \"impl Foo\", navigation_range: 78..81, node_range: 73..159, kind: SymbolKind(Impl), detail: None, deprecated: false }\n" +
            "StructureNode { parent: Some(3), label: \"new\", navigation_range: 102..105, node_range: 88..133, kind: SymbolKind(Function), detail: Some(\"fn(x: i32) -> Self\"), deprecated: false }\n" +
            "StructureNode { parent: Some(3), label: \"secret\", navigation_range: 141..147, node_range: 138..157, kind: SymbolKind(Method), detail: Some(\"fn(&self)\"), deprecated: false }\n" +
            "StructureNode { parent: None, label: \"impl std::fmt::Display for Foo\", navigation_range: 188..191, node_range: 161..277, kind: SymbolKind(Impl), detail: None, deprecated: false }\n" +
            "StructureNode { parent: Some(6), label: \"fmt\", navigation_range: 201..204, node_range: 198..275, kind: SymbolKind(Method), detail: Some(\"fn(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result\"), deprecated: false }\n" +
            "StructureNode { parent: None, label: \"E\", navigation_range: 295..296, node_range: 279..309, kind: SymbolKind(Enum), detail: None, deprecated: false }\n" +
            "StructureNode { parent: Some(8), label: \"A\", navigation_range: 299..300, node_range: 299..300, kind: SymbolKind(Variant), detail: None, deprecated: false }\n" +
            "StructureNode { parent: Some(8), label: \"B\", navigation_range: 302..303, node_range: 302..307, kind: SymbolKind(Variant), detail: None, deprecated: false }\n" +
            "StructureNode { parent: None, label: \"T\", navigation_range: 316..317, node_range: 310..334, kind: SymbolKind(Trait), detail: None, deprecated: false }\n" +
            "StructureNode { parent: Some(11), label: \"x\", navigation_range: 323..324, node_range: 320..332, kind: SymbolKind(Method), detail: Some(\"fn(&self)\"), deprecated: false }\n" +
            "StructureNode { parent: None, label: \"C\", navigation_range: 345..346, node_range: 335..356, kind: SymbolKind(Const), detail: Some(\"i32\"), deprecated: false }\n" +
            "StructureNode { parent: None, label: \"S\", navigation_range: 364..365, node_range: 357..379, kind: SymbolKind(Static), detail: Some(\"&str\"), deprecated: false }\n" +
            "StructureNode { parent: None, label: \"m\", navigation_range: 388..389, node_range: 380..407, kind: SymbolKind(Module), detail: None, deprecated: false }\n" +
            "StructureNode { parent: Some(15), label: \"f\", navigation_range: 399..400, node_range: 392..405, kind: SymbolKind(Function), detail: Some(\"fn()\"), deprecated: false }\n" +
            "StructureNode { parent: None, label: \"mac\", navigation_range: 437..440, node_range: 408..453, kind: SymbolKind(Macro), detail: None, deprecated: false }\n" +
            "StructureNode { parent: None, label: \"Alias\", navigation_range: 459..464, node_range: 454..470, kind: SymbolKind(TypeAlias), detail: Some(\"u8\"), deprecated: false }\n" +
            "StructureNode { parent: None, label: \"main\", navigation_range: 474..478, node_range: 471..483, kind: SymbolKind(Function), detail: Some(\"fn()\"), deprecated: false }\n";

        private static IReadOnlyList<SolutionSymbol> ParseSample() =>
            RustSymbolsOutputParser.Parse(Output, Encoding.UTF8.GetBytes(Source));

        private static SolutionSymbol Root(string name) => ParseSample().Single(s => s.Name == name);

        [TestMethod]
        public void TopLevelItemsAreRootsInSourceOrder()
        {
            CollectionAssert.AreEqual(
                new[] { "Foo", "impl Foo", "impl std::fmt::Display for Foo", "E", "T", "C", "S", "m", "mac", "Alias", "main" },
                ParseSample().Select(s => s.Name).ToArray());
        }

        [TestMethod]
        public void ChildrenAreNestedUnderTheirParent()
        {
            CollectionAssert.AreEqual(new[] { "a", "b" }, Root("Foo").Children.Select(s => s.Name).ToArray());
            CollectionAssert.AreEqual(new[] { "new", "secret" }, Root("impl Foo").Children.Select(s => s.Name).ToArray());
            CollectionAssert.AreEqual(new[] { "f" }, Root("m").Children.Select(s => s.Name).ToArray());
        }

        [TestMethod]
        public void VisibilityFollowsTheWrittenModifier()
        {
            Assert.AreEqual(SymbolVisibility.Public, Root("Foo").Visibility, "pub, after a doc comment and an attribute");
            Assert.AreEqual(SymbolVisibility.Public, Root("Foo").Children[0].Visibility, "pub field");
            Assert.AreEqual(SymbolVisibility.Private, Root("Foo").Children[1].Visibility, "private field");
            Assert.AreEqual(SymbolVisibility.Internal, Root("impl Foo").Children[0].Visibility, "pub(crate)");
            Assert.AreEqual(SymbolVisibility.Private, Root("impl Foo").Children[1].Visibility);
            Assert.AreEqual(SymbolVisibility.Protected, Root("E").Visibility, "pub(super)");
            Assert.AreEqual(SymbolVisibility.Private, Root("T").Visibility);
            Assert.AreEqual(SymbolVisibility.Private, Root("S").Visibility);
            Assert.AreEqual(SymbolVisibility.Private, Root("main").Visibility);
        }

        [TestMethod]
        public void TraitMembersVariantsAndExportedMacrosArePublic()
        {
            Assert.AreEqual(SymbolVisibility.Public, Root("T").Children[0].Visibility);
            Assert.AreEqual(SymbolVisibility.Public, Root("impl std::fmt::Display for Foo").Children[0].Visibility);
            Assert.AreEqual(SymbolVisibility.Public, Root("E").Children[0].Visibility);
            Assert.AreEqual(SymbolVisibility.Public, Root("mac").Visibility);
        }

        [TestMethod]
        public void KindsAreMapped()
        {
            Assert.AreEqual(SolutionSymbolKind.Struct, Root("Foo").Kind);
            Assert.AreEqual(SolutionSymbolKind.Impl, Root("impl Foo").Kind);
            Assert.AreEqual(SolutionSymbolKind.TraitImpl, Root("impl std::fmt::Display for Foo").Kind);
            Assert.AreEqual(SolutionSymbolKind.Variant, Root("E").Children[1].Kind);
            Assert.AreEqual(SolutionSymbolKind.Static, Root("S").Kind);
            Assert.AreEqual(SolutionSymbolKind.Macro, Root("mac").Kind);
            Assert.AreEqual(SolutionSymbolKind.TypeAlias, Root("Alias").Kind);
        }

        [TestMethod]
        public void PositionsAreZeroBasedLinesAndUtf16ColumnsOfTheName()
        {
            var foo = Root("Foo");
            Assert.AreEqual(2, foo.Line);
            Assert.AreEqual(11, foo.Column);

            // After the two-byte "é" on the line above: byte offsets must not leak into lines/columns.
            var m = Root("m");
            Assert.AreEqual(20, m.Line);
            Assert.AreEqual(8, m.Column);
        }

        [TestMethod]
        public void DisplayTextShowsSignaturesAndTypes()
        {
            Assert.AreEqual("new(x: i32) -> Self", Root("impl Foo").Children[0].DisplayText);
            Assert.AreEqual("a: i32", Root("Foo").Children[0].DisplayText);
            Assert.AreEqual("main()", Root("main").DisplayText);
        }

        [TestMethod]
        public void UnparsableLinesAreSkippedWithoutShiftingParentIndices()
        {
            var output =
                "StructureNode { parent: None, label: \"A\", navigation_range: 7..8, node_range: 0..11, kind: SymbolKind(Struct), detail: None, deprecated: false }\n" +
                "StructureNode { something new entirely }\n" +
                "StructureNode { parent: Some(1), label: \"orphan\", navigation_range: 0..1, node_range: 0..1, kind: SymbolKind(Field), detail: None, deprecated: false }\n" +
                "StructureNode { parent: Some(0), label: \"x\", navigation_range: 9..10, node_range: 9..10, kind: SomethingElse, detail: None, deprecated: false }\n";
            var roots = RustSymbolsOutputParser.Parse(output, Encoding.UTF8.GetBytes("struct A{x}"));

            Assert.AreEqual(1, roots.Count);
            Assert.AreEqual("x", roots[0].Children.Single().Name);
            Assert.AreEqual(SolutionSymbolKind.Other, roots[0].Children[0].Kind);
        }

        [TestMethod]
        public void LocalsInsideFunctionsAreNotListed()
        {
            // Real output for "fn main() {\n    let greeting = hello_rust::greet(\"Kubuno\");\n...".
            var output =
                "StructureNode { parent: None, label: \"main\", navigation_range: 3..7, node_range: 0..94, kind: SymbolKind(Function), detail: Some(\"fn()\"), deprecated: false }\n" +
                "StructureNode { parent: Some(0), label: \"greeting\", navigation_range: 21..29, node_range: 17..60, kind: SymbolKind(Local), detail: None, deprecated: false }\n";
            var roots = RustSymbolsOutputParser.Parse(output, Encoding.UTF8.GetBytes(new string(' ', 94)));

            Assert.AreEqual("main", roots.Single().Name);
            Assert.AreEqual(0, roots[0].Children.Count);
        }

        [TestMethod]
        public void DebugEscapesInLabelsAreDecoded()
        {
            Assert.AreEqual("a\"b\\cé", RustSymbolsOutputParser.Unescape("a\\\"b\\\\c\\u{e9}"));
        }

        [TestMethod]
        [DataRow("pub ", SymbolVisibility.Public)]
        [DataRow("pub(crate) ", SymbolVisibility.Internal)]
        [DataRow("pub( crate ) ", SymbolVisibility.Internal)]
        [DataRow("pub(in crate::a) ", SymbolVisibility.Internal)]
        [DataRow("pub(super) ", SymbolVisibility.Protected)]
        [DataRow("pub(self) ", SymbolVisibility.Private)]
        [DataRow("fn ", SymbolVisibility.Private)]
        [DataRow("/// pub docs\n// pub comment\n#[cfg(feature = \"pub\")]\nstruct ", SymbolVisibility.Private)]
        [DataRow("/* pub */ #[attr(x = \"]\")] pub unsafe fn ", SymbolVisibility.Public)]
        [DataRow("public_thing ", SymbolVisibility.Private)]
        public void VisibilityReaderHandlesModifiersCommentsAndAttributes(string prefix, SymbolVisibility expected)
        {
            Assert.AreEqual(expected, RustVisibilityReader.Read(prefix));
        }
    }
}
