using Kubuno.Desktop.Designer.Editing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Desktop.Tests.Designer.Editing
{
    [TestClass]
    public class LspPositionMapperTests
    {
        [TestMethod]
        public void ToOffset_Line0Character0_IsZero()
        {
            Assert.AreEqual(0, LspPositionMapper.ToOffset("hello", new LspPosition(0, 0)));
        }

        [TestMethod]
        public void ToOffset_MidFirstLine()
        {
            Assert.AreEqual(3, LspPositionMapper.ToOffset("hello world", new LspPosition(0, 3)));
        }

        [TestMethod]
        public void ToOffset_SecondLine_Lf()
        {
            var text = "line0\nline1\nline2";
            Assert.AreEqual(6, LspPositionMapper.ToOffset(text, new LspPosition(1, 0)));
            Assert.AreEqual(9, LspPositionMapper.ToOffset(text, new LspPosition(1, 3)));
        }

        [TestMethod]
        public void ToOffset_SecondLine_CrLf()
        {
            var text = "line0\r\nline1\r\nline2";
            Assert.AreEqual(7, LspPositionMapper.ToOffset(text, new LspPosition(1, 0)));
        }

        [TestMethod]
        public void ToOffset_LoneCr_CountsAsLineTerminator()
        {
            var text = "line0\rline1";
            Assert.AreEqual(6, LspPositionMapper.ToOffset(text, new LspPosition(1, 0)));
        }

        [TestMethod]
        public void ToOffset_CharacterPastEndOfLine_ClampsToLineEnd()
        {
            var text = "ab\ncd";
            Assert.AreEqual(2, LspPositionMapper.ToOffset(text, new LspPosition(0, 99)));
        }

        [TestMethod]
        public void ToOffset_LinePastEndOfDocument_ClampsToTextLength()
        {
            var text = "only-one-line";
            Assert.AreEqual(text.Length, LspPositionMapper.ToOffset(text, new LspPosition(5, 0)));
        }

        [TestMethod]
        public void ToOffset_EndOfDocument_ForEmptyText()
        {
            Assert.AreEqual(0, LspPositionMapper.ToOffset("", new LspPosition(0, 0)));
        }

        [TestMethod]
        public void ToOffsetRange_ReturnsBothEndpoints()
        {
            var text = "abcdef";
            var (start, end) = LspPositionMapper.ToOffsetRange(text, new LspRange(new LspPosition(0, 1), new LspPosition(0, 4)));

            Assert.AreEqual(1, start);
            Assert.AreEqual(4, end);
        }

        [TestMethod]
        public void ToOffsetRange_EndBeforeStart_Throws()
        {
            var text = "line0\nline1";
            Assert.ThrowsExactly<System.ArgumentException>(
                () => LspPositionMapper.ToOffsetRange(text, new LspRange(new LspPosition(1, 0), new LspPosition(0, 0))));
        }
    }
}
