using Kubuno.Desktop.Designer.Selection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Desktop.Tests.Designer.Selection
{
    [TestClass]
    public class ElementAttributeReaderTests
    {
        [TestMethod]
        public void Read_RootElement_ReturnsItsTagNameAndAttributes()
        {
            var attributes = ElementAttributeReader.Read(@"<Button Text=""Ok"" Dock=""Left""/>", "");

            Assert.IsNotNull(attributes);
            Assert.AreEqual("Button", attributes!.TagName);
            Assert.AreEqual("Ok", attributes.Attributes["Text"]);
            Assert.AreEqual("Left", attributes.Attributes["Dock"]);
        }

        [TestMethod]
        public void Read_NestedChild_WalksTheOrdinalPath()
        {
            var text = @"<Stack><Button Text=""a""/><Switch x:Name=""s""/></Stack>";

            var second = ElementAttributeReader.Read(text, "1");

            Assert.IsNotNull(second);
            Assert.AreEqual("Switch", second!.TagName);
            Assert.AreEqual("s", second.Attributes["x:Name"]);
        }

        [TestMethod]
        public void Read_GrandchildViaMultiSegmentId_Resolves()
        {
            var text = @"<Card><Stack><TextField x:Name=""proxy"" Text=""hi""/></Stack></Card>";

            var textField = ElementAttributeReader.Read(text, "0.0");

            Assert.IsNotNull(textField);
            Assert.AreEqual("TextField", textField!.TagName);
            Assert.AreEqual("hi", textField.Attributes["Text"]);
        }

        [TestMethod]
        public void Read_XNamePrefixedAttribute_IsKeptAsOneLiteralAttributeName()
        {
            // The real kubuno-views grammar treats "x:Name"/"x:Class" as one flat IDENT token (colons
            // allowed mid-identifier), never a real XML namespace resolved against a declared `xmlns:x`
            // - `tests/corpus/settings_view.kbview` (kubuno-views' own corpus) declares `xmlns="kubuno/ui/2026"`
            // but never `xmlns:x`, and still uses `x:Class`/`x:Name` throughout.
            var attributes = ElementAttributeReader.Read(@"<View xmlns=""kubuno/ui/2026"" x:Class=""shell::x""/>", "");

            Assert.IsNotNull(attributes);
            Assert.AreEqual("shell::x", attributes!.Attributes["x:Class"]);
            Assert.AreEqual("kubuno/ui/2026", attributes.Attributes["xmlns"]);
        }

        [TestMethod]
        public void Read_SingleAndDoubleQuotedValues_BothStripTheirQuotes()
        {
            var attributes = ElementAttributeReader.Read(@"<Stack Direction='TopDown' Gap=""8"">", "");

            Assert.IsNotNull(attributes);
            Assert.AreEqual("TopDown", attributes!.Attributes["Direction"]);
            Assert.AreEqual("8", attributes.Attributes["Gap"]);
        }

        [TestMethod]
        public void Read_CommentsAndCdataBetweenElements_AreSkippedNotCountedAsChildren()
        {
            var text = "<Stack><!-- a leading comment -->" +
                       "<TextField Text=\"hello\"/>" +
                       "<Script><![CDATA[not really script: a < b]]></Script>" +
                       "<!-- a trailing comment --></Stack>";

            var script = ElementAttributeReader.Read(text, "1");

            Assert.IsNotNull(script);
            Assert.AreEqual("Script", script!.TagName);
        }

        [TestMethod]
        public void Read_MismatchedEndTag_ClosesTheNearestOpenElementAndSiblingParsingContinues()
        {
            // Mirrors kubuno-views' own parser tolerance: `parse_end_tag` closes whatever element is
            // currently open regardless of the closing tag's own name (never checked during parsing) -
            // here, the mismatched `</BadName>` closes <Card> (not <Stack>, its grandparent), and parsing
            // correctly resumes one level up: <Switch> is still Stack's second child.
            var text = "<Stack><Card><Button/></BadName><Switch x:Name=\"s\"/></Stack>";

            var card = ElementAttributeReader.Read(text, "0");
            var second = ElementAttributeReader.Read(text, "1");

            Assert.IsNotNull(card);
            Assert.AreEqual("Card", card!.TagName);
            Assert.IsNotNull(second);
            Assert.AreEqual("Switch", second!.TagName);
        }

        [TestMethod]
        public void Read_DuplicateAttributeName_FirstOccurrenceWins()
        {
            var attributes = ElementAttributeReader.Read(@"<Button Text=""first"" Text=""second""/>", "");

            Assert.IsNotNull(attributes);
            Assert.AreEqual("first", attributes!.Attributes["Text"]);
        }

        [TestMethod]
        public void Read_OutOfRangeIndex_ReturnsNull()
        {
            Assert.IsNull(ElementAttributeReader.Read(@"<Stack><Button/></Stack>", "5"));
        }

        [TestMethod]
        public void Read_MalformedId_ReturnsNull()
        {
            Assert.IsNull(ElementAttributeReader.Read(@"<Stack><Button/></Stack>", "not-a-number"));
        }

        [TestMethod]
        public void Read_EmptyDocument_ReturnsNull()
        {
            Assert.IsNull(ElementAttributeReader.Read(string.Empty, ""));
        }

        [TestMethod]
        public void Read_UnterminatedTag_DoesNotThrowAndDegradesToNull()
        {
            Assert.IsNull(ElementAttributeReader.Read("<Button Text=\"unterminated", "0"));
        }

        [TestMethod]
        public void Read_UnterminatedString_DoesNotThrowAndStillReadsTheTagName()
        {
            var attributes = ElementAttributeReader.Read("<Button Text=\"unterminated/>", "");

            // The lexer's own contract: an unterminated string token still spans to end-of-input, and
            // `Attribute::value()` returns None for it - mirrored here by the attribute simply not being
            // recorded rather than the whole read failing (the tag name itself is still known).
            Assert.IsNotNull(attributes);
            Assert.AreEqual("Button", attributes!.TagName);
        }
    }
}
