using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Kubuno.Views.Designer;
using Kubuno.Views.Designer.Icons;
using Kubuno.Views.Designer.PropertyBrowser;
using Kubuno.Views.Designer.Registry;
using Kubuno.Views.Tests.Designer.PropertyBrowser;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Views.Tests.Designer.Icons
{
    /// <summary>docs/ICONS.md: the icon kind in the Properties window - the value syntax, the catalog, the editors and a round trip into the view.</summary>
    [TestClass]
    public class IconEditorTests
    {
        private const string Catalog = @"{
            ""icons"": [
                { ""name"": ""Save"", ""set"": ""Lucide"", ""aliasOf"": null, ""keywords"": [""save""], ""viewBox"": 24,
                  ""layers"": [ { ""d"": ""M2 2L22 22"", ""stroke"": 2, ""role"": ""base"", ""opacity"": 1, ""color"": null, ""transform"": [1,0,0,1,0,0] } ] },
                { ""name"": ""FolderOpen"", ""set"": ""Lucide"", ""aliasOf"": null, ""keywords"": [""folder"", ""open"", ""folder-open""], ""viewBox"": 24,
                  ""layers"": [ { ""d"": ""M2 2H22V22Z"", ""stroke"": 2, ""role"": ""base"", ""opacity"": 1, ""color"": null, ""transform"": [1,0,0,1,0,0] } ] },
                { ""name"": ""Trash2"", ""set"": ""Lucide"", ""aliasOf"": null, ""keywords"": [""trash"", ""2""], ""viewBox"": 24,
                  ""layers"": [ { ""d"": ""M2 2H22"", ""stroke"": 2, ""role"": ""base"", ""opacity"": 1, ""color"": null, ""transform"": [1,0,0,1,0,0] } ] },
                { ""name"": ""Settings"", ""set"": ""Kubuno"", ""aliasOf"": null, ""keywords"": [""settings""], ""viewBox"": 16,
                  ""layers"": [ { ""d"": ""M0 0H16V16H0Z"", ""stroke"": null, ""role"": ""accent"", ""opacity"": 0.5, ""color"": null, ""transform"": [1,0,0,1,0,0] } ] },
                { ""name"": ""DriveLogo"", ""set"": ""Modules"", ""aliasOf"": null, ""keywords"": [""drive""], ""viewBox"": 24,
                  ""layers"": [ { ""d"": ""M0 0H24V24Z"", ""stroke"": null, ""role"": ""base"", ""opacity"": 1, ""color"": ""#ff8800"", ""transform"": [1,0,0,1,0,0] } ] }
            ],
            ""aliases"": { ""trash"": ""Trash2"" },
            ""sizes"": { ""Small"": 16, ""Large"": 24 }
        }";

        [TestInitialize]
        public void ForceEnglish() => DesignerText.ForceFrench = false;

        [TestCleanup]
        public void ResetLanguage() => DesignerText.ForceFrench = null;

        [TestMethod]
        public void TheValueSyntax_IsAGlyphAFileAResourceOrABinding()
        {
            Assert.AreEqual(IconValueKind.None, IconValue.Classify("  "));
            Assert.AreEqual(IconValueKind.Glyph, IconValue.Classify("Save"));
            Assert.AreEqual(IconValueKind.Glyph, IconValue.Classify("trash"));
            foreach (var file in new[] { "resources/save.svg", "a.PNG", "b.ico", "c.jpeg", "d.bmp", "e.gif", "f.tif", "g.tiff", "h.webp", "i.jpg" })
            {
                Assert.AreEqual(IconValueKind.File, IconValue.Classify(file), file);
            }

            Assert.AreEqual(IconValueKind.Resource, IconValue.Classify("{Res Logo}"));
            Assert.AreEqual("Logo", IconValue.ResourceKey("{Res Logo, Source=resources}"));
            Assert.IsNull(IconValue.ResourceKey("{Resource Logo}"));
            Assert.AreEqual(IconValueKind.Binding, IconValue.Classify("{Binding Icon}"));
            Assert.AreEqual("resources/save.svg", IconValue.Normalize(" resources\\save.svg "));
            Assert.AreEqual("Save", IconValue.Normalize(" Save "));
        }

        [TestMethod]
        public void TheCatalog_IsReadFoundAndSearched()
        {
            var catalog = IconCatalog.FromJson(Catalog);
            Assert.AreEqual(5, catalog.Icons.Count);
            Assert.AreEqual("Trash2", catalog.Find("trash")!.Name);
            Assert.AreEqual(2.0, catalog.Find("Save")!.Layers[0].Stroke);
            Assert.IsNull(catalog.Find("Settings")!.Layers[0].Stroke);
            Assert.AreEqual("#ff8800", catalog.Find("DriveLogo")!.Layers[0].Color);
            Assert.IsNull(catalog.Find("Nope"));
            Assert.AreEqual(24.0, catalog.Sizes["Large"]);

            // Name before keyword, the sets in their order.
            CollectionAssert.AreEqual(new[] { "FolderOpen" }, catalog.Search("folder-open").Select(g => g.Name).ToArray());
            CollectionAssert.AreEqual(new[] { "Save" }, catalog.Search("sav").Select(g => g.Name).ToArray());
            Assert.AreEqual("Trash2", catalog.Search("trash").First().Name);
            Assert.AreEqual(5, catalog.Search(string.Empty).Count);
            CollectionAssert.AreEqual(new[] { "Settings" }, catalog.Search(string.Empty, "Kubuno").Select(g => g.Name).ToArray());
            Assert.AreEqual("Save", catalog.Search(string.Empty)[1].Name, "Lucide first, by name: FolderOpen, Save, Trash2");
            Assert.IsTrue(IconCatalog.FromJson("not json").IsEmpty);
            Assert.IsTrue(IconCatalog.FromJson(null).IsEmpty);
        }

        [TestMethod]
        public void AGlyph_IsDrawnFromItsOwnPaths() => OnSta(() =>
        {
            var catalog = IconCatalog.FromJson(Catalog);
            var image = IconDrawing.ToImage(catalog.Find("Save")!, System.Windows.Media.Colors.Black, System.Windows.Media.Colors.Blue, 16);
            Assert.IsNotNull(image);
            Assert.AreEqual(16.0, image!.Width, 0.01);
            using var bitmap = IconDrawing.ToGdi(image, 16, 16)!;
            // The diagonal stroke covers the centre; a corner far from it stays clear.
            Assert.IsTrue(bitmap.GetPixel(8, 8).A > 100);
            Assert.AreEqual(0, bitmap.GetPixel(15, 0).A);
            Assert.AreEqual(System.Windows.Media.Color.FromRgb(0x12, 0x34, 0x56), IconDrawing.ParseColor("#123456"));
            Assert.IsNull(IconDrawing.ParseColor("Accent"));
        });

        /// <summary>Runs <paramref name="body"/> on an STA thread (WPF drawing), rethrowing what it throws.</summary>
        private static void OnSta(Action body)
        {
            Exception? failure = null;
            var thread = new System.Threading.Thread(() =>
            {
                try
                {
                    body();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure is not null)
            {
                throw new AssertFailedException(failure.Message, failure);
            }
        }

        [TestMethod]
        public void RecentIcons_AreMostRecentFirstKeptAndCapped()
        {
            var file = Path.Combine(Path.GetTempPath(), "kubuno-recent-" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                var recent = new RecentIcons(file);
                recent.Add("Save");
                recent.Add("resources/a.svg");
                recent.Add("Save");
                recent.Add("{Binding X}");
                recent.Add(string.Empty);
                CollectionAssert.AreEqual(new[] { "Save", "resources/a.svg" }, recent.Items.ToArray());
                CollectionAssert.AreEqual(new[] { "Save", "resources/a.svg" }, new RecentIcons(file).Items.ToArray(), "kept between sessions");
                for (var i = 0; i < 40; i++)
                {
                    recent.Add("Icon" + i);
                }

                Assert.AreEqual(RecentIcons.Capacity, recent.Items.Count);
                Assert.AreEqual("Icon39", recent.Items[0]);
            }
            finally
            {
                File.Delete(file);
            }
        }

        [TestMethod]
        public void IconRows_GetTheIconEditorTheIconCategoryAndTheirOptions()
        {
            var icon = new PropertyMeta { Name = "Icon", Kind = PropKind.String, Editor = "icon", Category = "Icon" };
            Assert.IsInstanceOfType<KbviewIconEditor>(RichEditors.EditorFor(icon));
            Assert.AreEqual("resources/x.svg", RichEditors.Normalize(icon, PropKind.String, "resources\\x.svg"));
            var align = new PropertyMeta { Name = "ImageAlign", Kind = PropKind.CreateEnum(KbviewContentAlignmentEditor.Values.ToArray()) };
            Assert.IsInstanceOfType<KbviewContentAlignmentEditor>(RichEditors.EditorFor(align));
            var notAlign = new PropertyMeta { Name = "Dock", Kind = PropKind.CreateEnum(new[] { "None", "Top" }) };
            Assert.IsNull(RichEditors.EditorFor(notAlign));
            var size = new PropertyMeta { Name = "IconSize", Kind = PropKind.String, TypeConverter = "IconSize" };
            var converter = RichEditors.ConverterFor(size)!;
            Assert.IsFalse(converter.GetStandardValuesExclusive(null));
            CollectionAssert.AreEqual(new[] { "Small", "Medium", "Large", "XLarge" }, converter.GetStandardValues(null)!.Cast<string>().ToArray());
            Assert.AreEqual("Icon", PropertyCategoryMap.DisplayName("Icon"));
            DesignerText.ForceFrench = true;
            Assert.AreEqual("Icône", PropertyCategoryMap.DisplayName("Icon"));
        }

        [TestMethod]
        public void PickingAnIcon_WritesTheAttribute_AndClearingRemovesIt()
        {
            const string json = "[{\"name\":\"Button\",\"doc\":\"A button.\",\"family\":\"core\",\"children\":\"None\",\"allowed_children\":[],\"layout_kind\":null,\"base_chain\":[\"Button\",\"ButtonBase\",\"Control\",\"Component\"],\"events\":[],\"properties\":["
                + "{\"name\":\"Text\",\"kind\":\"String\",\"default\":\"\",\"doc\":\"Text.\",\"category\":\"Appearance\",\"browsable\":true,\"bindable\":false,\"localizable\":false,\"editor\":null,\"type_converter\":null,\"inherited_from\":null,\"root_only\":false,\"design_time\":false,\"aliases\":[]},"
                + "{\"name\":\"Icon\",\"kind\":\"String\",\"default\":\"\",\"doc\":\"The icon.\",\"category\":\"Icon\",\"browsable\":true,\"bindable\":false,\"localizable\":false,\"editor\":\"icon\",\"type_converter\":null,\"inherited_from\":null,\"root_only\":false,\"design_time\":false,\"aliases\":[]},"
                + "{\"name\":\"IconSize\",\"kind\":\"String\",\"default\":\"\",\"doc\":\"Size.\",\"category\":\"Icon\",\"browsable\":true,\"bindable\":false,\"localizable\":false,\"editor\":null,\"type_converter\":\"IconSize\",\"inherited_from\":null,\"root_only\":false,\"design_time\":false,\"aliases\":[]}"
                + "]}]";
            var registry = ComponentRegistry.FromJson(json);
            var host = new RichHost("<Button x:Name=\"ok\" Text=\"OK\"/>", registry);
            var element = new KbviewElementObject(host, "0", registry.Find("Button")!);
            var row = element.GetProperties().Find("Icon", false)!;
            Assert.AreEqual(new CategoryAttribute("Icon").Category, row.Category);
            Assert.IsInstanceOfType<KbviewIconEditor>(((Kubuno.Views.Designer.Bindings.KbviewBindableEditor)row.GetEditor(typeof(System.Drawing.Design.UITypeEditor))!).Inner);

            // Each gesture is one undo unit (one batch of edits of the view's text), a file written with forward slashes.
            row.SetValue(element, "resources\\save.svg");
            host.RunScheduled();
            row.SetValue(element, "Save");
            element.GetProperties().Find("IconSize", false)!.SetValue(element, "Large");
            host.RunScheduled();
            row.SetValue(element, string.Empty);
            host.RunScheduled();
            CollectionAssert.AreEqual(new[] { "set 0 Icon=resources/save.svg", "set 0 Icon=Save", "set 0 IconSize=Large", "remove 0 Icon" }, host.Calls.ToArray());
            Assert.AreEqual(3, host.Batches.Count);
        }
    }
}
