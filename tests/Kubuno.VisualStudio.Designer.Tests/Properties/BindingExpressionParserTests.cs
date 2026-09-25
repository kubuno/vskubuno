using Kubuno.VisualStudio.Designer.Properties;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.VisualStudio.Designer.Tests.Properties
{
    [TestClass]
    public class BindingExpressionParserTests
    {
        [TestMethod]
        public void TryParse_SimpleBinding_ParsesPath_NoMode()
        {
            var ok = BindingExpressionParser.TryParse("{Binding UserName}", out var expression);

            Assert.IsTrue(ok);
            Assert.AreEqual("UserName", expression!.Path);
            Assert.IsNull(expression.Mode);
        }

        [TestMethod]
        public void TryParse_BindingWithMode_ParsesBoth()
        {
            var ok = BindingExpressionParser.TryParse("{Binding Items.Count, Mode=TwoWay}", out var expression);

            Assert.IsTrue(ok);
            Assert.AreEqual("Items.Count", expression!.Path);
            Assert.AreEqual("TwoWay", expression.Mode);
        }

        [TestMethod]
        public void TryParse_PlainLiteral_IsNotABinding()
        {
            var ok = BindingExpressionParser.TryParse("Hello world", out var expression);

            Assert.IsFalse(ok);
            Assert.IsNull(expression);
        }

        [TestMethod]
        [DataRow((string?)null)]
        [DataRow("")]
        public void TryParse_NullOrEmpty_IsNotABinding(string? rawValue)
        {
            Assert.IsFalse(BindingExpressionParser.TryParse(rawValue, out _));
        }

        [TestMethod]
        public void Format_RoundTripsWithoutMode()
        {
            var ok = BindingExpressionParser.TryParse("{Binding Path}", out var expression);
            Assert.IsTrue(ok);

            Assert.AreEqual("{Binding Path}", BindingExpressionParser.Format(expression!));
        }

        [TestMethod]
        public void Format_RoundTripsWithMode()
        {
            var ok = BindingExpressionParser.TryParse("{Binding Path, Mode=OneWay}", out var expression);
            Assert.IsTrue(ok);

            Assert.AreEqual("{Binding Path, Mode=OneWay}", BindingExpressionParser.Format(expression!));
        }

        [TestMethod]
        public void IsBindingExpression_MatchesTryParse()
        {
            Assert.IsTrue(BindingExpressionParser.IsBindingExpression("{Binding X}"));
            Assert.IsFalse(BindingExpressionParser.IsBindingExpression("X"));
        }
    }
}
