using Kubuno.VisualStudio.Designer.Editing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.VisualStudio.Designer.Tests.Editing
{
    [TestClass]
    public class InsertChildFormatterTests
    {
        [TestMethod]
        public void BeforeTheParentsEndTag_TakesThePreviousChildsIndentation_OnItsOwnLine()
        {
            // "  </Stack>" with the caret after the indentation; the last child is indented by 4.
            var text = InsertChildFormatter.Format("  ", "</Stack>", "    <Button x:Name=\"hello\"/>", "<Button/>", "\r\n");

            Assert.AreEqual("  <Button/>\r\n  ", text);
            Assert.AreEqual("    <Button/>\r\n  </Stack>", "  " + text + "</Stack>");
        }

        [TestMethod]
        public void IntoAnEmptyParent_IndentsOneLevelDeeper()
        {
            var text = InsertChildFormatter.Format("  ", "</Stack>", "  <Stack Gap=\"16\">", "<Button/>", "\n");
            Assert.AreEqual("  <Button/>\n  ", text);
        }

        [TestMethod]
        public void TabIndentation_IsKept()
        {
            var text = InsertChildFormatter.Format("\t", "</Stack>", "\t<Stack>", "<Button/>", "\n");
            Assert.AreEqual("\t<Button/>\n\t", text);
        }

        [TestMethod]
        public void BeforeASibling_TakesTheSiblingsIndentation()
        {
            var text = InsertChildFormatter.Format("    ", "<Button x:Name=\"hello\"/>", "    <TextField/>", "<Switch/>", "\n");
            Assert.AreEqual("<Switch/>\n    ", text);
        }

        [TestMethod]
        public void AnInsertionInsideALine_IsLeftUntouched()
        {
            Assert.AreEqual("<Button/>", InsertChildFormatter.Format("  <Stack>", "</Stack>", null, "<Button/>", "\n"));
            Assert.AreEqual("<A>\n</A>", InsertChildFormatter.Format("  ", "</Stack>", null, "<A>\n</A>", "\n"));
        }
    }
}
