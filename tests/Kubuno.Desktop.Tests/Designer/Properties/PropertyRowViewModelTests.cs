using System.Collections.Generic;
using Kubuno.Desktop.Designer.Properties;
using Kubuno.Desktop.Designer.Registry;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Desktop.Tests.Designer.Properties
{
    [TestClass]
    public class PropertyRowViewModelTests
    {
        private static PropertyMeta CreateBoolMeta(string? @default = "false") =>
            new PropertyMeta { Name = "Enabled", Kind = PropKind.Bool, Default = @default, Doc = "doc" };

        [TestMethod]
        public void NoRawValue_IsDefault_AndEffectiveValueFallsBackToDefault()
        {
            var row = new PropertyRowViewModel(CreateBoolMeta("false"), currentRawValue: null);

            Assert.IsTrue(row.IsDefault);
            Assert.AreEqual("false", row.EffectiveValue);
        }

        [TestMethod]
        public void ExplicitRawValue_DifferentFromDefault_IsNotDefault()
        {
            var row = new PropertyRowViewModel(CreateBoolMeta("false"), currentRawValue: "true");

            Assert.IsFalse(row.IsDefault);
            Assert.AreEqual("true", row.EffectiveValue);
        }

        [TestMethod]
        public void ExplicitRawValue_EqualToDefaultText_IsStillConsideredDefault()
        {
            var row = new PropertyRowViewModel(CreateBoolMeta("false"), currentRawValue: "false");

            Assert.IsTrue(row.IsDefault);
        }

        [TestMethod]
        public void ResetToDefault_ClearsRawValue_FallsBackToDefault()
        {
            var row = new PropertyRowViewModel(CreateBoolMeta("false"), currentRawValue: "true");

            row.ResetToDefault();

            Assert.IsNull(row.RawValue);
            Assert.IsTrue(row.IsDefault);
            Assert.AreEqual("false", row.EffectiveValue);
        }

        [TestMethod]
        public void SettingRawValue_RaisesValueCommitted()
        {
            var row = new PropertyRowViewModel(CreateBoolMeta("false"), currentRawValue: null);
            string? committed = "unset";
            row.ValueCommitted += (_, value) => committed = value;

            row.RawValue = "true";

            Assert.AreEqual("true", committed);
        }

        [TestMethod]
        public void SettingSameRawValue_DoesNotRaisePropertyChanged()
        {
            var row = new PropertyRowViewModel(CreateBoolMeta("false"), currentRawValue: "true");
            var raised = new List<string?>();
            row.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            row.RawValue = "true";

            Assert.AreEqual(0, raised.Count);
        }

        [TestMethod]
        public void BindingValue_IsDetectedAsBinding_AndExposesParsedExpression()
        {
            var meta = new PropertyMeta { Name = "Text", Kind = PropKind.String, Default = "" };
            var row = new PropertyRowViewModel(meta, "{Binding UserName, Mode=TwoWay}");

            Assert.IsTrue(row.IsBinding);
            Assert.AreEqual("UserName", row.Binding!.Path);
            Assert.AreEqual("TwoWay", row.Binding.Mode);
        }

        [TestMethod]
        public void LiteralValue_IsNotDetectedAsBinding()
        {
            var meta = new PropertyMeta { Name = "Text", Kind = PropKind.String, Default = "" };
            var row = new PropertyRowViewModel(meta, "hello");

            Assert.IsFalse(row.IsBinding);
            Assert.IsNull(row.Binding);
        }
    }
}
