using System;
using Kubuno.VisualStudio.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.VisualStudio.Tests
{
    [TestClass]
    public class RustTestLocatorTests
    {
        [TestMethod]
        public void FindEnclosingTestName_ReturnsModuleQualifiedName_ForCaretInsideTestBody()
        {
            // Mirrors samples/hello-rust/src/lib.rs.
            var text =
                "pub fn greet(name: &str) -> String {\n" +      // 0
                "    format!(\"Hello, {name}!\")\n" +            // 1
                "}\n" +                                          // 2
                "\n" +                                            // 3
                "#[cfg(test)]\n" +                                // 4
                "mod tests {\n" +                                 // 5
                "    use super::*;\n" +                           // 6
                "\n" +                                            // 7
                "    #[test]\n" +                                 // 8
                "    fn greet_includes_the_name() {\n" +          // 9
                "        assert_eq!(greet(\"Kubuno\"), \"Hello, Kubuno!\");\n" + // 10
                "    }\n" +                                       // 11
                "}\n";                                            // 12

            var name = RustTestLocator.FindEnclosingTestName(text, caretLine: 10);

            Assert.AreEqual("tests::greet_includes_the_name", name);
        }

        [TestMethod]
        public void FindEnclosingTestName_ReturnsTopLevelName_ForIntegrationTestFile()
        {
            // Mirrors samples/hello-rust/tests/basic.rs: no enclosing module.
            var text =
                "#[test]\n" +                                                                  // 0
                "fn greet_includes_the_name() {\n" +                                            // 1
                "    assert_eq!(hello_rust::greet(\"Visual Studio\"), \"Hello, Visual Studio!\");\n" + // 2
                "}\n";                                                                          // 3

            var name = RustTestLocator.FindEnclosingTestName(text, caretLine: 2);

            Assert.AreEqual("greet_includes_the_name", name);
        }

        [TestMethod]
        public void FindEnclosingTestName_MatchesWhenCaretIsOnTheFnOrAttributeLine()
        {
            var text = "#[test]\nfn it_works() {\n    assert!(true);\n}\n";

            Assert.AreEqual("it_works", RustTestLocator.FindEnclosingTestName(text, caretLine: 0));
            Assert.AreEqual("it_works", RustTestLocator.FindEnclosingTestName(text, caretLine: 1));
        }

        [TestMethod]
        public void FindEnclosingTestName_IgnoresNonTestFunctions()
        {
            var text =
                "fn helper() {\n" +
                "    let x = 1;\n" +
                "}\n" +
                "\n" +
                "#[test]\n" +
                "fn it_works() {\n" +
                "    assert!(true);\n" +
                "}\n";

            Assert.IsNull(RustTestLocator.FindEnclosingTestName(text, caretLine: 1));
            Assert.AreEqual("it_works", RustTestLocator.FindEnclosingTestName(text, caretLine: 6));
        }

        [TestMethod]
        public void FindEnclosingTestName_ReturnsNull_WhenNoTestFunctionFollows()
        {
            var text = "#[test]\nfn it_works() {\n    assert!(true);\n}\n\nfn helper() {\n    let x = 1;\n}\n";

            Assert.IsNull(RustTestLocator.FindEnclosingTestName(text, caretLine: 6));
        }

        [TestMethod]
        public void FindEnclosingTestName_DoesNotMiscountBracesInsideStringLiterals()
        {
            // A `{` inside a format string must not be mistaken for a scope-opening brace -
            // this is exactly the case in hello-rust's own greet() (format!("Hello, {name}!")).
            var text =
                "mod tests {\n" +
                "    fn helper() -> String {\n" +
                "        format!(\"{ not a real brace }\")\n" +
                "    }\n" +
                "\n" +
                "    #[test]\n" +
                "    fn it_works() {\n" +
                "        assert!(true);\n" +
                "    }\n" +
                "}\n";

            var name = RustTestLocator.FindEnclosingTestName(text, caretLine: 7);

            Assert.AreEqual("tests::it_works", name);
        }

        [TestMethod]
        public void FindEnclosingTestName_HandlesNestedModules()
        {
            var text =
                "mod outer {\n" +
                "    mod inner {\n" +
                "        #[test]\n" +
                "        fn deeply_nested() {\n" +
                "            assert!(true);\n" +
                "        }\n" +
                "    }\n" +
                "}\n";

            var name = RustTestLocator.FindEnclosingTestName(text, caretLine: 4);

            Assert.AreEqual("outer::inner::deeply_nested", name);
        }

        [TestMethod]
        public void FindEnclosingTestName_RecognizesAsyncTestAttributes()
        {
            var text = "#[tokio::test]\nasync fn it_works() {\n    assert!(true);\n}\n";

            var name = RustTestLocator.FindEnclosingTestName(text, caretLine: 2);

            Assert.AreEqual("it_works", name);
        }

        [TestMethod]
        public void FindEnclosingTestName_DoesNotMatchTestCaseAttribute()
        {
            var text = "#[test_case(1)]\nfn parametrized() {\n    assert!(true);\n}\n";

            Assert.IsNull(RustTestLocator.FindEnclosingTestName(text, caretLine: 2));
        }

        [TestMethod]
        public void FindEnclosingTestName_ThrowsOnNullDocumentText()
        {
            Assert.ThrowsExactly<ArgumentNullException>(() => RustTestLocator.FindEnclosingTestName(null!, 0));
        }

        [TestMethod]
        public void FindEnclosingTestName_ThrowsOnNegativeCaretLine()
        {
            Assert.ThrowsExactly<ArgumentException>(() => RustTestLocator.FindEnclosingTestName("fn f() {}", -1));
        }
    }
}
