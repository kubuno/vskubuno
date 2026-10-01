using System;
using System.ComponentModel;
using System.Linq;
using Kubuno.Desktop.Designer.PropertyBrowser;
using Kubuno.Desktop.Designer.Registry;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Kubuno.Desktop.Designer;

namespace Kubuno.Desktop.Tests.Designer.PropertyBrowser
{
    /// <summary>docs/EVENTS.md, "WinForms-rich property sets": the Properties window's rows for the control hierarchy's properties.</summary>
    [TestClass]
    public class RichPropertiesTests
    {
        private const string View =
            "<Panel Title=\"Demo\" Opacity=\"80\">\n" +
            "  <Button x:Name=\"ok\" Text=\"OK\" X=\"24\" Y=\"16\" Width=\"100\" Margin=\"4\"/>\n" +
            "  <Button x:Name=\"cancel\" Text=\"Cancel\" X=\"140\" Y=\"16\"/>\n" +
            "  <Slider Max=\"10\"/>\n" +
            "  <ToolTip x:Name=\"toolTip1\"/>\n" +
            "  <ContextMenu x:Name=\"menu\"/>\n" +
            "  <Stack Padding=\"8\"/>\n" +
            "</Panel>";

        [TestInitialize]
        public void ForceEnglish() => DesignerText.ForceFrench = false;

        [TestCleanup]
        public void ResetLanguage() => DesignerText.ForceFrench = null;

        private static (KbviewElementObject Element, RichHost Host) Create(string id, string tag, string text = View)
        {
            var host = new RichHost(text);
            return (new KbviewElementObject(host, id, host.Registry.Find(tag)!), host);
        }

        private static PropertyDescriptor Row(KbviewElementObject element, string name) => element.GetProperties().Find(name, false) ?? throw new AssertFailedException("no row " + name);

        [TestMethod]
        public void TheNewExportKeys_AreRead()
        {
            var button = RichRegistry.Registry.Find("Button")!;
            var back = button.Properties.Single(p => p.Name == "BackColor");
            Assert.AreEqual("Control", back.InheritedFrom);
            Assert.AreEqual("color", back.Editor);
            Assert.IsTrue(button.Properties.Single(p => p.Name == "Title").RootOnly);
            Assert.IsTrue(button.Properties.Single(p => p.Name == "Locked").DesignTime);
            var max = RichRegistry.Registry.Find("Slider")!.Properties.Single(p => p.Name == "Maximum");
            CollectionAssert.AreEqual(new[] { "Max" }, max.Aliases);
            Assert.IsTrue(max.Matches("Max") && max.Matches("Maximum") && !max.Matches("Min"));
            Assert.IsNull(button.Properties.Single(p => p.Name == "Text").InheritedFrom);
        }

        [TestMethod]
        public void InheritedProperties_AreListedInTheirWinFormsCategories_InBothLanguages()
        {
            var (button, _) = Create("0", "Button");
            Assert.AreEqual(new CategoryAttribute("Accessibility").Category, Row(button, "AccessibleName").Category);
            Assert.AreEqual(new CategoryAttribute("Focus").Category, Row(button, "CausesValidation").Category);
            Assert.AreEqual(new CategoryAttribute("Misc").Category, Row(button, "ToolTip").Category);
            Assert.AreEqual(new CategoryAttribute("Design").Category, Row(button, "Locked").Category);
            Assert.AreEqual("The BackColor of the element.", Row(button, "BackColor").Description);

            DesignerText.ForceFrench = true;
            var (french, _) = Create("0", "Button");
            Assert.AreEqual("Accessibilité", Row(french, "AccessibleName").Category);
            Assert.AreEqual("Comportement", Row(french, "Enabled").Category);
            Assert.AreEqual("Disposition", Row(french, KbviewElementObject.LocationRow).Category);
            Assert.AreEqual("Données", Row(french, "Tag").Category);
            Assert.AreEqual("Divers", Row(french, "ToolTip").Category);
            Assert.AreEqual("Le BackColor de l'élément.", Row(french, "BackColor").Description);
            var (root, _) = Create(string.Empty, "Panel");
            Assert.AreEqual("Style de fenêtre", Row(root, "Opacity").Category);
            Assert.AreEqual("Style de fenêtre", PropertyCategoryMap.DisplayName("Window Style"));
        }

        [TestMethod]
        public void TheViewsOwnProperties_AreOnTheRootOnly()
        {
            var (button, _) = Create("0", "Button");
            Assert.IsNull(button.GetProperties().Find("Title", false));
            Assert.IsNull(button.GetProperties().Find("Opacity", false));
            var (root, _) = Create(string.Empty, "Panel");
            Assert.AreEqual("Demo", Row(root, "Title").GetValue(root));
            Assert.IsNotNull(root.GetProperties().Find("StartPosition", false));
        }

        [TestMethod]
        public void LocationAndSize_AreExpandableComposites_OverXYWidthHeight()
        {
            var (button, host) = Create("0", "Button");
            var rows = button.GetProperties().Cast<PropertyDescriptor>().Where(p => p.IsBrowsable).Select(p => p.Name).ToList();
            CollectionAssert.DoesNotContain(rows, "X", "X is shown inside Location");
            CollectionAssert.DoesNotContain(rows, "Width");

            var location = Row(button, KbviewElementObject.LocationRow);
            Assert.AreEqual("Location", location.DisplayName);
            var value = location.GetValue(button)!;
            Assert.AreEqual("24, 16", location.Converter.ConvertToString(value));
            Assert.IsTrue(location.ShouldSerializeValue(button));
            var parts = location.Converter.GetProperties(value)!;
            CollectionAssert.AreEqual(new[] { "X", "Y" }, parts.Cast<PropertyDescriptor>().Select(p => p.Name).ToArray());
            Assert.AreEqual("24", parts["X"]!.GetValue(value));

            // A button has its own Size (small, medium, large): the composite is shown as "Dimensions".
            var size = Row(button, KbviewElementObject.SizeRow);
            Assert.AreEqual("Dimensions", size.DisplayName);
            Assert.AreEqual("100, auto", size.Converter.ConvertToString(size.GetValue(button)));
            Assert.AreEqual("Md", Row(button, "Size").GetValue(button), "the own Size row stays");

            // Typing the whole value writes both attributes; a part writes one; "auto" removes the size.
            location.SetValue(button, location.Converter.ConvertFromString("30, 16"));
            parts["Y"]!.SetValue(location.GetValue(button), "20");
            var sizeValue = size.GetValue(button)!;
            size.Converter.GetProperties(sizeValue)!["Width"]!.SetValue(sizeValue, "auto");
            CollectionAssert.AreEqual(new[] { "set 0 X=30", "set 0 Y=20", "remove 0 Width" }, host.Calls);
            Assert.ThrowsExactly<ArgumentException>(() => location.SetValue(button, location.Converter.ConvertFromString("30")));
        }

        [TestMethod]
        public void CompositeParts_AreBoldWhenSet_AndReset()
        {
            var (cancel, host) = Create("1", "Button");
            var location = Row(cancel, KbviewElementObject.LocationRow);
            var value = location.GetValue(cancel)!;
            var parts = location.Converter.GetProperties(value)!;
            Assert.IsTrue(parts["X"]!.ShouldSerializeValue(value));
            Assert.IsTrue(parts["X"]!.CanResetValue(value));
            parts["X"]!.ResetValue(value);
            location.ResetValue(cancel);
            CollectionAssert.AreEqual(new[] { "remove 1 X", "remove 1 Y" }, host.Calls, "the row's Reset removes what is still written");
        }

        [TestMethod]
        public void Padding_ExpandsToAllLeftTopRightBottom_AndTheDefaultRemovesIt()
        {
            var (button, host) = Create("0", "Button");
            var margin = Row(button, "Margin");
            var value = margin.GetValue(button)!;
            Assert.AreEqual("4, 4, 4, 4", margin.Converter.ConvertToString(value));
            var parts = margin.Converter.GetProperties(value)!;
            CollectionAssert.AreEqual(new[] { "All", "Left", "Top", "Right", "Bottom" }, parts.Cast<PropertyDescriptor>().Select(p => p.Name).ToArray());
            Assert.AreEqual("4", parts["All"]!.GetValue(value));

            parts["Left"]!.SetValue(value, "10");
            Assert.AreEqual(string.Empty, parts["All"]!.GetValue(margin.GetValue(button)), "the sides differ now");
            margin.SetValue(button, margin.Converter.ConvertFromString("0"));
            CollectionAssert.AreEqual(new[] { "set 0 Margin=10, 4, 4, 4", "remove 0 Margin" }, host.Calls);

            var (stack, _) = Create("5", "Stack");
            var own = Row(stack, "Padding");
            Assert.AreEqual("8, 8, 8, 8", own.Converter.ConvertToString(own.GetValue(stack)), "a single number is the four sides");
            var min = Row(stack, "MinimumSize");
            Assert.IsFalse(min.ShouldSerializeValue(stack));
            CollectionAssert.AreEqual(new[] { "Width", "Height" }, min.Converter.GetProperties(min.GetValue(stack))!.Cast<PropertyDescriptor>().Select(p => p.Name).ToArray());
        }

        [TestMethod]
        public void Opacity_IsShownAsAPercentage()
        {
            var (root, host) = Create(string.Empty, "Panel");
            var opacity = Row(root, "Opacity");
            Assert.AreEqual("80 %", opacity.Converter.ConvertToString(opacity.GetValue(root)));
            opacity.SetValue(root, opacity.Converter.ConvertFromString("50 %"));
            opacity.SetValue(root, opacity.Converter.ConvertFromString("0.25"));
            CollectionAssert.AreEqual(new[] { "set  Opacity=50", "set  Opacity=25" }, host.Calls);
            Assert.ThrowsExactly<ArgumentException>(() => opacity.Converter.ConvertFromString("half"));
            Assert.AreEqual(100f, CompositeText.ParseOpacity("150"));
        }

        [TestMethod]
        public void AnAlias_IsReadAndEditedWhereTheFileWritesIt()
        {
            var (slider, host) = Create("2", "Slider");
            var max = Row(slider, "Maximum");
            Assert.AreEqual("10", max.GetValue(slider));
            Assert.IsTrue(max.ShouldSerializeValue(slider));
            max.SetValue(slider, "20");
            Row(slider, "Minimum").SetValue(slider, "1");
            max.ResetValue(slider);
            CollectionAssert.AreEqual(new[] { "set 2 Max=20", "set 2 Minimum=1", "remove 2 Max" }, host.Calls);
            Assert.IsNull(slider.GetProperties().Find("Max", false), "the alias is not a second row");
        }

        [TestMethod]
        public void ToolTip_IsAnExtenderRow_NamedAfterTheViewsToolTipComponent()
        {
            var (button, _) = Create("0", "Button");
            Assert.AreEqual("ToolTip on toolTip1", Row(button, "ToolTip").DisplayName);
            var (alone, _) = Create("0", "Button", "<Panel><Button/></Panel>");
            Assert.AreEqual("ToolTip", Row(alone, "ToolTip").DisplayName);
            DesignerText.ForceFrench = true;
            Assert.AreEqual("ToolTip sur toolTip1", Row(Create("0", "Button").Element, "ToolTip").DisplayName);
        }

        [TestMethod]
        public void References_ListTheMatchingNames()
        {
            var (button, _) = Create("0", "Button");
            var menu = Row(button, "ContextMenu").Converter;
            var context = new Context(button);
            Assert.IsTrue(menu.GetStandardValuesSupported(context));
            Assert.IsFalse(menu.GetStandardValuesExclusive(context));
            CollectionAssert.AreEqual(new[] { "menu" }, menu.GetStandardValues(context)!.Cast<string>().ToArray());

            var (root, _) = Create(string.Empty, "Panel");
            CollectionAssert.AreEqual(new[] { "ok", "cancel" }, Row(root, "AcceptButton").Converter.GetStandardValues(new Context(root))!.Cast<string>().ToArray());
        }

        [TestMethod]
        public void Editors_FollowTheRegistry()
        {
            var (button, _) = Create("0", "Button");
            Assert.IsInstanceOfType<KbviewColorEditor>(((Kubuno.Desktop.Designer.Bindings.KbviewBindableEditor)Row(button, "BackColor").GetEditor(typeof(System.Drawing.Design.UITypeEditor))!).Inner);
            Assert.IsInstanceOfType<KbviewFontEditor>(((Kubuno.Desktop.Designer.Bindings.KbviewBindableEditor)Row(button, "Font").GetEditor(typeof(System.Drawing.Design.UITypeEditor))!).Inner);
            Assert.IsInstanceOfType<KbviewImageEditor>(((Kubuno.Desktop.Designer.Bindings.KbviewBindableEditor)Row(button, "BackgroundImage").GetEditor(typeof(System.Drawing.Design.UITypeEditor))!).Inner);
            Assert.IsInstanceOfType<KbviewCursorEditor>(((Kubuno.Desktop.Designer.Bindings.KbviewBindableEditor)Row(button, "Cursor").GetEditor(typeof(System.Drawing.Design.UITypeEditor))!).Inner);
            Assert.IsTrue(new KbviewColorEditor().GetPaintValueSupported(null));
            Assert.AreEqual(FontText.AmbientDefault, Row(button, "Font").GetValue(button), "the ambient font, greyed");
            Assert.IsFalse(Row(button, "Font").ShouldSerializeValue(button));
        }

        [TestMethod]
        public void BackColor_OnAButton_AlsoTurnsOffTheVisualStyleFace_InTheSameBatch()
        {
            var (button, host) = Create("0", "Button");
            Row(button, "BackColor").SetValue(button, "primary");
            Row(button, "ForeColor").SetValue(button, "#abc");
            Assert.ThrowsExactly<ArgumentException>(() => Row(button, "BackColor").SetValue(button, "not a colour"));
            Row(button, "Font").SetValue(button, "Segoe UI, 12pt, style=bold");
            host.RunScheduled();
            Assert.AreEqual(1, host.Batches.Count);
            CollectionAssert.AreEqual(
                new[] { "set 0 BackColor=Primary", "set 0 UseVisualStyleBackColor=false", "set 0 ForeColor=#AABBCC", "set 0 Font=Segoe UI, 12pt, style=Bold" },
                host.Batches[0].Select(e => e.ToString()).ToArray());
        }

        [TestMethod]
        public void MultiSelection_SetsAnExpandableRowOnEveryElement_InOneBatch()
        {
            var host = new RichHost(View);
            var ok = new KbviewElementObject(host, "0", host.Registry.Find("Button")!);
            var cancel = new KbviewElementObject(host, "1", host.Registry.Find("Button")!);
            var location = TypeDescriptor.GetProperties(ok).Find(KbviewElementObject.LocationRow, false)!;
            Assert.AreNotEqual(location.GetValue(ok), location.GetValue(cancel), "differing values are blanked");
            var typed = location.Converter.ConvertFromString(new Context(new object[] { ok, cancel }), "8, 8");
            location.SetValue(ok, typed);
            location.SetValue(cancel, typed);
            host.RunScheduled();
            Assert.AreEqual(1, host.Batches.Count);
            CollectionAssert.AreEqual(new[] { "set 0 X=8", "set 0 Y=8", "set 1 X=8", "set 1 Y=8" }, host.Batches[0].Select(e => e.ToString()).ToArray());
            Assert.AreEqual(2, RichEditors.Elements(new Context(new object[] { ok, cancel })).Count);
        }

        [TestMethod]
        public void DataBindings_ListTheBindableProperties_AndWriteBindings()
        {
            var (button, host) = Create("0", "Button");
            var bindings = Row(button, KbviewBindingsPropertyDescriptor.RowName);
            Assert.AreEqual(new CategoryAttribute("Data").Category, bindings.Category);
            var value = bindings.GetValue(button)!;
            var parts = bindings.Converter.GetProperties(value)!.Cast<PropertyDescriptor>().ToList();
            CollectionAssert.IsSubsetOf(new[] { "Text", "Enabled", "Visible", "Tag", "(Advanced)" }, parts.Select(p => p.Name).ToArray());
            var text = parts.Single(p => p.Name == "Text");
            Assert.AreEqual(string.Empty, text.GetValue(value), "a plain value is not a binding");
            text.SetValue(value, "Status");
            parts.Single(p => p.Name == "Enabled").SetValue(value, "{Binding CanSave, Mode=TwoWay}");
            CollectionAssert.AreEqual(new[] { "set 0 Text={Binding Status}", "set 0 Enabled={Binding CanSave, Mode=TwoWay}" }, host.Calls);
            Assert.IsInstanceOfType<Kubuno.Desktop.Designer.Bindings.KbviewBindableEditor>(text.GetEditor(typeof(System.Drawing.Design.UITypeEditor)));
        }

        [TestMethod]
        public void BindingText_BuildsAndParses()
        {
            Assert.AreEqual("{Binding Status}", BindingText.Build("Status", "OneWay"));
            Assert.AreEqual("{Binding Status, Mode=TwoWay}", BindingText.Build(" Status ", "twoway"));
            Assert.AreEqual(string.Empty, BindingText.Build(" ", "TwoWay"));
            Assert.AreEqual(("Name", "OneTime"), BindingText.Parse("{Binding Name, Mode=OneTime}"));
            Assert.AreEqual(((string?)null, "OneWay"), BindingText.Parse("plain"));
            Assert.AreEqual("{Binding A}", BindingText.FromTyped("A"));
            Assert.ThrowsExactly<ArgumentException>(() => BindingText.FromTyped("a, b"));
            CollectionAssert.AreEqual(new[] { "Status", "Items" }, BindingText.ParsePaths("{\"paths\":[\"Status\",\"Items\",\"Status\",\"\",3]}").ToArray());
            Assert.AreEqual(0, BindingText.ParsePaths("not json").Count);
            Assert.AreEqual(0, BindingText.ParsePaths("null").Count);
        }

        [TestMethod]
        public void ChildrenRows_AreCollectionsOrStringLists()
        {
            var rows = (string tag) => new KbviewElementObject(new RichHost("<Panel/>"), "0", RichRegistry.Registry.Find(tag)!).GetProperties().OfType<KbviewChildrenPropertyDescriptor>().Select(r => (r.Name, r.ChildTag, r.StringList)).ToList();
            CollectionAssert.AreEqual(new[] { ("Items", "Option", true) }, rows("ComboBox"));
            CollectionAssert.AreEqual(new[] { ("Items", "Item", false), ("Columns", "Column", false) }, rows("ListView"));
            CollectionAssert.AreEqual(new[] { ("TabPages", "TabItem", false) }, rows("Tabs"));
            CollectionAssert.AreEqual(new[] { ("Items", "MenuItem", false) }, rows("ContextMenu"));
            Assert.AreEqual(0, rows("Button").Count);
            Assert.IsInstanceOfType<KbviewStringListEditor>(new KbviewChildrenPropertyDescriptor("Items", "Option", true, "Behavior").GetEditor(typeof(System.Drawing.Design.UITypeEditor)));
            Assert.IsInstanceOfType<KbviewCollectionEditor>(new KbviewChildrenPropertyDescriptor("Columns", "Column", false, "Behavior").GetEditor(typeof(System.Drawing.Design.UITypeEditor)));
        }

        [TestMethod]
        public void ImagePaths_AreRelativeToTheView_WithForwardSlashes()
        {
            Assert.AreEqual("resources/logo.png", ImageResources.RelativePath(@"C:\app\src\main_view.kbview", @"C:\app\src\resources\logo.png"));
            Assert.AreEqual("../assets/a b.png", ImageResources.RelativePath(@"C:\app\src\main_view.kbview", @"C:\app\assets\a b.png"));
            Assert.IsTrue(ImageResources.IsBesideView(@"C:\app\src\v.kbview", @"C:\APP\src\img\x.png"));
            Assert.IsFalse(ImageResources.IsBesideView(@"C:\app\src\v.kbview", @"C:\other\x.png"));
            Assert.AreEqual(@"C:\app\src\resources\x2.png", ImageResources.CopyTarget(@"C:\app\src\v.kbview", @"D:\x.png", p => p.EndsWith(@"\x.png", StringComparison.Ordinal)));
            Assert.IsTrue(ImageResources.IsImage("a.PNG") && !ImageResources.IsImage("a.txt"));
            var (button, host) = Create("0", "Button");
            Row(button, "BackgroundImage").SetValue(button, @"resources\logo.png");
            CollectionAssert.AreEqual(new[] { "set 0 BackgroundImage=resources/logo.png" }, host.Calls);
        }

        [TestMethod]
        public void Cursors_HaveTheirWindowsCursor()
        {
            foreach (var name in CursorNames.All)
            {
                Assert.IsNotNull(CursorNames.CursorOf(name), name);
            }
        }

        [TestMethod]
        public void LspPositions_RoundTripWithOffsets()
        {
            var text = "ab\r\ncd\nef\rgh";
            foreach (var offset in new[] { 0, 2, 4, 5, 7, 8, 10, 11, text.Length })
            {
                var position = Kubuno.Desktop.Designer.Editing.LspPositionMapper.FromOffset(text, offset);
                Assert.AreEqual(offset, Kubuno.Desktop.Designer.Editing.LspPositionMapper.ToOffset(text, position), offset.ToString());
            }
        }

        /// <summary>The grid's context for one element or a multi-selection.</summary>
        private sealed class Context : ITypeDescriptorContext
        {
            public Context(object instance) => Instance = instance;

            public IContainer? Container => null;

            public object Instance { get; }

            public PropertyDescriptor? PropertyDescriptor => null;

            public object? GetService(Type serviceType) => null;

            public void OnComponentChanged()
            {
            }

            public bool OnComponentChanging() => true;
        }
    }
}
