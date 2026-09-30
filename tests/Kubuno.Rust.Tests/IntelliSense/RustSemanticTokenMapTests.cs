using System.Collections.Generic;
using Kubuno.VisualStudio.Core.IntelliSense;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.VisualStudio.Tests.IntelliSense
{
    [TestClass]
    public sealed class RustSemanticTokenMapTests
    {
        // rust-analyzer 1.98's legend (captured from a real initialize answer).
        private static readonly string[] Types =
        {
            "comment", "decorator", "enumMember", "enum", "function", "interface", "keyword", "macro", "method", "namespace", "number", "operator",
            "parameter", "property", "string", "struct", "typeParameter", "variable", "type", "angle", "arithmetic", "attributeBracket", "attribute",
            "bitwise", "boolean", "brace", "bracket", "builtinAttribute", "builtinType", "character", "colon", "comma", "comparison", "constParameter",
            "const", "deriveHelper", "derive", "dot", "escapeSequence", "formatSpecifier", "generic", "invalidEscapeSequence", "label", "lifetime",
            "logical", "macroBang", "negation", "parenthesis", "procMacro", "punctuation", "selfKeyword", "selfTypeKeyword", "semicolon", "static",
            "toolModule", "typeAlias", "union", "unresolvedReference",
        };

        private static readonly string[] Modifiers =
        {
            "async", "documentation", "declaration", "static", "defaultLibrary", "deprecated", "associated", "attribute", "callable", "constant",
            "consuming", "controlFlow", "crateRoot", "injected", "intraDocLink", "library", "macro", "mutable", "procMacro", "public", "reference",
            "trait", "unsafe",
        };

        private static readonly RustSemanticTokenMap Map = new RustSemanticTokenMap(Types, Modifiers);

        private static string TypeOf(string type, params string[] modifiers) => Map.Legend[Map.Map(System.Array.IndexOf(Types, type), Bits(modifiers)).Type];

        private static List<string> ModifiersOf(string type, params string[] modifiers)
        {
            var bits = Map.Map(System.Array.IndexOf(Types, type), Bits(modifiers)).Modifiers;
            var names = new List<string>();
            for (int i = 0; i < RustSemanticTokenMap.Modifiers.Count; i++)
            {
                if ((bits & (1 << i)) != 0)
                {
                    names.Add(RustSemanticTokenMap.Modifiers[i]);
                }
            }

            return names;
        }

        private static int Bits(string[] modifiers)
        {
            int bits = 0;
            foreach (var modifier in modifiers)
            {
                bits |= 1 << System.Array.IndexOf(Modifiers, modifier);
            }

            return bits;
        }

        [TestMethod]
        public void TypesAreColoredLikeCSharp()
        {
            Assert.AreEqual("struct name", TypeOf("struct"));
            Assert.AreEqual("enum name", TypeOf("enum"));
            Assert.AreEqual("interface name", TypeOf("interface"));
            Assert.AreEqual("type parameter name", TypeOf("typeParameter"));
            Assert.AreEqual("keyword", TypeOf("builtinType"));
            Assert.AreEqual("enum member name", TypeOf("enumMember"));
        }

        [TestMethod]
        public void ValuesAndMembersAreColoredLikeCSharp()
        {
            Assert.AreEqual("local name", TypeOf("variable"));
            Assert.AreEqual("parameter name", TypeOf("parameter"));
            Assert.AreEqual("field name", TypeOf("property"));
            Assert.AreEqual("method name", TypeOf("function"));
            Assert.AreEqual("method name", TypeOf("method"));
            Assert.AreEqual("constant name", TypeOf("const"));
            Assert.AreEqual("namespace name", TypeOf("namespace"));
        }

        [TestMethod]
        public void ControlFlowKeywordsAndDocCommentsGetTheirCSharpClassification()
        {
            Assert.AreEqual("keyword - control", TypeOf("keyword", "controlFlow"));
            Assert.AreEqual("keyword", TypeOf("keyword"));
            Assert.AreEqual("xml doc comment - text", TypeOf("comment", "documentation"));
            Assert.AreEqual("comment", TypeOf("comment"));
        }

        [TestMethod]
        public void RustOnlyTraitsBecomeKubunoModifiers()
        {
            CollectionAssert.Contains(ModifiersOf("variable", "mutable"), RustSemanticTokenMap.Mutable);
            CollectionAssert.Contains(ModifiersOf("function", "unsafe"), RustSemanticTokenMap.Unsafe);
            CollectionAssert.Contains(ModifiersOf("macro"), RustSemanticTokenMap.Macro);
            CollectionAssert.Contains(ModifiersOf("macroBang"), RustSemanticTokenMap.Macro);
            CollectionAssert.Contains(ModifiersOf("lifetime"), RustSemanticTokenMap.Lifetime);
            CollectionAssert.Contains(ModifiersOf("function", "static"), RustSemanticTokenMap.StaticSymbol);
            Assert.AreEqual(0, ModifiersOf("variable", "declaration", "public").Count);
        }

        [TestMethod]
        public void RemapRewritesTypeAndModifiersOfEveryTokenOnly()
        {
            var data = new List<int>
            {
                0, 4, 3, System.Array.IndexOf(Types, "struct"), 0,
                1, 8, 1, System.Array.IndexOf(Types, "variable"), Bits(new[] { "mutable" }),
            };

            Map.Remap(data);

            CollectionAssert.AreEqual(new[] { 0, 4, 3 }, data.GetRange(0, 3));
            Assert.AreEqual("struct name", Map.Legend[data[3]]);
            CollectionAssert.AreEqual(new[] { 1, 8, 1 }, data.GetRange(5, 3));
            Assert.AreEqual("local name", Map.Legend[data[8]]);
            Assert.AreEqual(1 << RustSemanticTokenMapTestsHelper.IndexOfModifier(RustSemanticTokenMap.Mutable), data[9]);
        }

        [TestMethod]
        public void AnUnknownTokenTypeIsPlainText()
        {
            Assert.AreEqual("text", Map.Legend[Map.Map(999, 0).Type]);
        }
    }

    internal static class RustSemanticTokenMapTestsHelper
    {
        public static int IndexOfModifier(string name)
        {
            for (int i = 0; i < RustSemanticTokenMap.Modifiers.Count; i++)
            {
                if (RustSemanticTokenMap.Modifiers[i] == name)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
