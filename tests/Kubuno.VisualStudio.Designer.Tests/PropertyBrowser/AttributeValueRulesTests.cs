using System;
using Kubuno.VisualStudio.Designer.PropertyBrowser;
using Kubuno.VisualStudio.Designer.Registry;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.VisualStudio.Designer.Tests.PropertyBrowser
{
    [TestClass]
    public class AttributeValueRulesTests
    {
        [TestInitialize]
        public void ForceEnglish() => DesignerText.ForceFrench = false;

        [TestCleanup]
        public void ResetLanguage() => DesignerText.ForceFrench = null;

        [TestMethod]
        public void Bool_IsNormalizedToLowerCase()
        {
            Assert.AreEqual("true", AttributeValueRules.Normalize(PropKind.Bool, " True "));
            Assert.AreEqual("false", AttributeValueRules.Normalize(PropKind.Bool, "FALSE"));
        }

        [TestMethod]
        public void Bool_RejectsAnythingElse()
        {
            var ex = Assert.ThrowsExactly<ArgumentException>(() => AttributeValueRules.Normalize(PropKind.Bool, "yes"));
            StringAssert.Contains(ex.Message, "true, false");
        }

        [TestMethod]
        public void Number_AcceptsInvariantDecimals_AndRejectsCommas()
        {
            Assert.AreEqual("12.5", AttributeValueRules.Normalize(PropKind.F32, "12.5"));
            Assert.AreEqual("-3", AttributeValueRules.Normalize(PropKind.F32, " -3 "));
            Assert.ThrowsExactly<ArgumentException>(() => AttributeValueRules.Normalize(PropKind.F32, "12,5"));
        }

        [TestMethod]
        public void Enum_IsMatchedCaseInsensitively_ToItsCanonicalVariant()
        {
            var kind = PropKind.CreateEnum(new[] { "Primary", "Secondary" });
            Assert.AreEqual("Secondary", AttributeValueRules.Normalize(kind, "secondary"));
            Assert.ThrowsExactly<ArgumentException>(() => AttributeValueRules.Normalize(kind, "Tertiary"));
        }

        [TestMethod]
        public void BindingExpression_IsAcceptedAsIs_ForEveryKind()
        {
            const string binding = "{Binding Status, Mode=TwoWay}";
            Assert.AreEqual(binding, AttributeValueRules.Normalize(PropKind.Bool, binding));
            Assert.AreEqual(binding, AttributeValueRules.Normalize(PropKind.F32, binding));
            Assert.AreEqual(binding, AttributeValueRules.Normalize(PropKind.CreateEnum(new[] { "A" }), binding));
        }

        [TestMethod]
        public void String_IsKeptVerbatim()
        {
            Assert.AreEqual(" Say hello ", AttributeValueRules.Normalize(PropKind.String, " Say hello "));
        }

        [TestMethod]
        public void Name_MustBeAnIdentifier()
        {
            Assert.AreEqual("save_btn", AttributeValueRules.NormalizeName(" save_btn "));
            Assert.ThrowsExactly<ArgumentException>(() => AttributeValueRules.NormalizeName("1abc"));
            Assert.ThrowsExactly<ArgumentException>(() => AttributeValueRules.NormalizeName("a b"));
        }

        [TestMethod]
        public void HandlerName_MustBeARustIdentifier()
        {
            Assert.AreEqual("on_hello_click", AttributeValueRules.NormalizeHandlerName("on_hello_click"));
            Assert.ThrowsExactly<ArgumentException>(() => AttributeValueRules.NormalizeHandlerName("on-hello"));
            Assert.ThrowsExactly<ArgumentException>(() => AttributeValueRules.NormalizeHandlerName("é"));
        }

        // Same cases as kubuno-views-ls/src/handler_insert.rs's own tests: the grid must propose exactly
        // the name the server generates.
        [TestMethod]
        public void DefaultHandlerName_MatchesTheLanguageServer()
        {
            Assert.AreEqual("on_save_btn_click", AttributeValueRules.DefaultHandlerName("saveBtn", "Button", "OnClick"));
            Assert.AreEqual("on_button_click", AttributeValueRules.DefaultHandlerName(null, "Button", "OnClick"));
            Assert.AreEqual("on_switch_toggled", AttributeValueRules.DefaultHandlerName(string.Empty, "Switch", "OnToggled"));
        }

        [TestMethod]
        public void ToSnakeCase_MatchesTheLanguageServer()
        {
            Assert.AreEqual("save_button", AttributeValueRules.ToSnakeCase("SaveButton"));
            Assert.AreEqual("save_btn_1", AttributeValueRules.ToSnakeCase("save-btn 1"));
            Assert.AreEqual(string.Empty, AttributeValueRules.ToSnakeCase(string.Empty));
        }

        [TestMethod]
        public void Categories_FollowWinFormsConventions()
        {
            Assert.AreEqual(PropertyCategoryMap.Category.Design, PropertyCategoryMap.For("x:Name", null));
            Assert.AreEqual(PropertyCategoryMap.Category.Appearance, PropertyCategoryMap.For("Text", PropKind.String));
            Assert.AreEqual(PropertyCategoryMap.Category.Layout, PropertyCategoryMap.For("Width", PropKind.F32));
            Assert.AreEqual(PropertyCategoryMap.Category.Data, PropertyCategoryMap.For("ItemsSource", PropKind.String));
            Assert.AreEqual(PropertyCategoryMap.Category.Behavior, PropertyCategoryMap.For("Loading", PropKind.Bool));
            Assert.AreEqual(PropertyCategoryMap.Category.Misc, PropertyCategoryMap.For("Mystery", PropKind.String));
        }

        [TestMethod]
        public void CategoryAndTabNames_AreLocalized()
        {
            DesignerText.ForceFrench = true;
            Assert.AreEqual("Disposition", PropertyCategoryMap.DisplayName(PropertyCategoryMap.Category.Layout));
            Assert.AreEqual("Affichage", DesignerText.ToolboxTabName("display"));
            Assert.AreEqual("Contrôles communs", DesignerText.ToolboxTabName("core"));
            Assert.AreEqual(" [Conception]", DesignerText.DesignCaptionSuffix);

            DesignerText.ForceFrench = false;
            Assert.AreEqual("Layout", PropertyCategoryMap.DisplayName(PropertyCategoryMap.Category.Layout));
            Assert.AreEqual("Containers", DesignerText.ToolboxTabName("containers"));
            Assert.AreEqual("Other", DesignerText.ToolboxTabName("other"));
            Assert.AreEqual(" [Design]", DesignerText.DesignCaptionSuffix);
        }
    }
}
