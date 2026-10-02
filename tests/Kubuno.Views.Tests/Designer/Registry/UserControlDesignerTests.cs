using System.Drawing;
using System.IO;
using Kubuno.Views.Designer;
using Kubuno.Views.Designer.DesignSurface;
using Kubuno.Views.Designer.PropertyBrowser;
using Kubuno.Views.Designer.Registry;
using Kubuno.Views.Designer.Toolbox;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Views.Tests.Designer.Registry
{
    /// <summary>User controls in the designer (docs/EVENTS.md, "User controls"): what the registry export tells the IDE.</summary>
    [TestClass]
    public class UserControlDesignerTests
    {
        private const string Json = @"[
          {""name"":""AddressEditor"",""doc"":""An address editor."",""family"":""project"",""icon"":""address-editor"",""children"":""None"",""allowed_children"":[],
           ""properties"":[
             {""name"":""AccentColor"",""kind"":""String"",""default"":"""",""doc"":""The accent."",""category"":""Appearance"",""browsable"":true,""bindable"":false,""editor"":""color""},
             {""name"":""Countries"",""kind"":""String"",""default"":"""",""doc"":""The countries."",""category"":""Data"",""browsable"":true,""bindable"":false,""editor"":""lines""},
             {""name"":""Format"",""kind"":{""Enum"":[""French"",""International""]},""default"":""French"",""doc"":""The format."",""category"":""Behavior"",""browsable"":true}],
           ""events"":[],""base_chain"":[""AddressEditor"",""UserControl"",""ContainerControl"",""ScrollableControl"",""Control"",""Component""],
           ""origin"":""project"",""kind"":""user_control"",""linked"":true,""design_size"":[360,200]}
        ]";

        [TestMethod]
        public void A_user_control_is_added_at_its_design_size_and_its_typed_properties_get_their_editors()
        {
            var registry = ComponentRegistry.FromJson(Json);
            var editor = registry.Find("AddressEditor");
            Assert.IsNotNull(editor);
            CollectionAssert.AreEqual(new double[] { 360, 200 }, editor!.DesignSize);
            Assert.AreEqual((360, 200), ToolboxInsertionPlanner.DefaultSize("AddressEditor", registry), "WinForms adds a UserControl at its own size");
            Assert.IsInstanceOfType(RichEditors.EditorFor(editor.Properties.Find(p => p.Name == "AccentColor")), typeof(KbviewColorEditor));
            var lines = editor.Properties.Find(p => p.Name == "Countries");
            Assert.IsInstanceOfType(RichEditors.EditorFor(lines), typeof(KbviewLinesEditor));
            Assert.IsInstanceOfType(RichEditors.ConverterFor(lines), typeof(LinesConverter));
        }

        [TestMethod]
        public void String_lists_are_one_item_per_line_and_one_line_in_the_grid()
        {
            Assert.AreEqual("France\nBelgique", LinesText.Join(new[] { "France", "", "Belgique\r" }));
            CollectionAssert.AreEqual(new[] { "France", "Belgique" }, (System.Collections.ICollection)LinesText.Split("France\r\nBelgique\n"));
            Assert.AreEqual("France; Belgique", LinesText.Display("France\nBelgique"));
            Assert.AreEqual("France\nBelgique", LinesText.FromTyped("France ; Belgique"));
        }

        [TestMethod]
        public void A_toolbox_bitmap_is_the_project_control_s_own_image()
        {
            var path = Path.Combine(Path.GetTempPath(), "kubuno-toolbox-bitmap-test.png");
            using (var bmp = new Bitmap(32, 32))
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(255, 200, 40, 40));
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }

            var meta = new ComponentMeta { Name = "AddressEditor", Kind = "user_control", ToolboxIcon = path };
            Assert.IsTrue(NativeToolboxInstaller.IsImageFile(path));
            Assert.AreEqual("file:" + path, NativeToolboxInstaller.ProjectIconKey(meta));
            var pixels = NativeToolboxInstaller.ImageFileIcon(path);
            Assert.IsNotNull(pixels);
            Assert.AreEqual(16 * 16 * 4, pixels!.Length, "scaled to the Toolbox's 16x16");
            Assert.AreEqual(255, pixels[3], "opaque pixel, premultiplied BGRA");
            Assert.IsNull(NativeToolboxInstaller.ImageFileIcon(path + ".missing.png"));
            Assert.IsFalse(NativeToolboxInstaller.IsImageFile("map-pin"));
        }

        [TestMethod]
        public void The_designer_opens_in_the_language_the_application_starts_in()
        {
            var fr = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
            Assert.AreEqual("fr", DesignSurfaceResourcesProtocol.DefaultDesignCulture(new[] { "de", "fr" }, fr));
            Assert.AreEqual("fr-FR", DesignSurfaceResourcesProtocol.DefaultDesignCulture(new[] { "fr", "fr-FR" }, fr));
            Assert.AreEqual("fr-CA", DesignSurfaceResourcesProtocol.DefaultDesignCulture(new[] { "fr-CA" }, fr));
            Assert.AreEqual(string.Empty, DesignSurfaceResourcesProtocol.DefaultDesignCulture(new[] { "de" }, fr), "no resources in that language: the neutral values");
        }

        [TestMethod]
        public void The_out_of_date_bar_says_to_build()
        {
            DesignerText.ForceFrench = true;
            StringAssert.Contains(DesignerText.RuntimeBarMessage(DesignSurfaceRuntimeState.OutOfDate), "Générez le projet");
            Assert.AreEqual("Générer", DesignerText.RuntimeBarAction(DesignSurfaceRuntimeState.OutOfDate));
            DesignerText.ForceFrench = false;
        }
    }
}
