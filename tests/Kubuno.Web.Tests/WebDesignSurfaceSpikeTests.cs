using System.Collections.Generic;
using System.Linq;
using Kubuno.Web.Logic.WebDesigner;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Web.Tests
{
    /// <summary>The pure half of the WV-9a WebView2 design surface spike (docs/WEB-VIEWS.md, "WV-9a findings").</summary>
    [TestClass]
    public sealed class WebSurfaceProtocolTests
    {
        [TestMethod]
        public void Host_messages_use_the_desktop_wire_shapes()
        {
            Assert.AreEqual("{\"type\":\"setText\",\"text\":\"<Stack><Button Text=\\\"a&b\\\"/></Stack>\"}", WebSurfaceProtocol.EncodeSetText("<Stack><Button Text=\"a&b\"/></Stack>"));
            Assert.AreEqual("{\"type\":\"select\",\"id\":\"1.0\"}", WebSurfaceProtocol.EncodeSelect("1.0"));
            Assert.AreEqual("{\"type\":\"select\",\"id\":null}", WebSurfaceProtocol.EncodeSelect(null));
            Assert.AreEqual("{\"type\":\"setDesignMode\",\"on\":true}", WebSurfaceProtocol.EncodeSetDesignMode(true));
            Assert.AreEqual("{\"type\":\"dragEnter\",\"component\":\"Button\"}", WebSurfaceProtocol.EncodeDragEnter("Button"));
            Assert.AreEqual("{\"type\":\"dragOver\",\"x\":12.5,\"y\":40}", WebSurfaceProtocol.EncodeDragOver(12.5, 40));
            Assert.AreEqual("{\"type\":\"drop\",\"x\":0.333,\"y\":7}", WebSurfaceProtocol.EncodeDrop(1.0 / 3, 7));
            Assert.AreEqual("{\"type\":\"dragLeave\"}", WebSurfaceProtocol.EncodeDragLeave());
            Assert.AreEqual("{\"type\":\"setCanvasBackground\",\"color\":\"#1F1F1F\"}", WebSurfaceProtocol.EncodeSetCanvasBackground("#1F1F1F"));
        }

        [TestMethod]
        public void Theme_and_drop_channel_messages()
        {
            var colors = new Dictionary<string, string> { ["text"] = "#FFFFFF", ["canvas"] = "#1F1F1F" };
            Assert.AreEqual("{\"type\":\"setVsTheme\",\"mode\":\"dark\",\"colors\":{\"canvas\":\"#1F1F1F\",\"text\":\"#FFFFFF\"}}", WebSurfaceProtocol.EncodeSetVsTheme(true, colors));
            Assert.AreEqual("{\"type\":\"setDropChannel\",\"channel\":\"html5\"}", WebSurfaceProtocol.EncodeSetDropChannel(DropChannel.Html5));
            Assert.AreEqual("{\"type\":\"setDropChannel\",\"channel\":\"host\"}", WebSurfaceProtocol.EncodeSetDropChannel(DropChannel.Host));
            Assert.AreEqual("{\"type\":\"showFontSpecimen\",\"on\":true}", WebSurfaceProtocol.EncodeShowFontSpecimen(true));
            StringAssert.StartsWith(
                WebSurfaceProtocol.EncodeSetComponents(SpikeElementCatalog.Elements),
                "{\"type\":\"setComponents\",\"components\":[{\"name\":\"Stack\",\"container\":true,\"xml\":\"<Stack Direction=\\\"LeftToRight\\\" Gap=\\\"8\\\" Padding=\\\"8\\\"/>\"},{\"name\":\"Label\",\"container\":false,");
        }

        [TestMethod]
        public void Parses_the_handshake_and_checks_it()
        {
            Assert.IsTrue(WebSurfaceProtocol.TryParse("{\"type\":\"surfaceInfo\",\"version\":1,\"target\":\"web\",\"views\":\"0.0.0-spike\",\"ui\":\"spike\"}", out var message));
            var info = (SurfaceInfoMessage)message!;
            Assert.AreEqual("0.0.0-spike", info.Views);
            Assert.IsNull(WebSurfaceProtocol.CheckSurfaceInfo(info));
            Assert.IsTrue(WebSurfaceProtocol.TryParse("{\"type\":\"surfaceInfo\",\"version\":2,\"target\":\"web\"}", out message));
            Assert.IsNotNull(WebSurfaceProtocol.CheckSurfaceInfo((SurfaceInfoMessage)message!));
            Assert.IsTrue(WebSurfaceProtocol.TryParse("{\"type\":\"surfaceInfo\",\"version\":1,\"target\":\"desktop\"}", out message));
            Assert.IsNotNull(WebSurfaceProtocol.CheckSurfaceInfo((SurfaceInfoMessage)message!));
        }

        [TestMethod]
        public void Parses_selection_changes()
        {
            Assert.IsTrue(WebSurfaceProtocol.TryParse("{\"type\":\"selectionChanged\",\"id\":\"1\",\"ids\":[\"1\",\"2\"],\"bounds\":{\"x\":1,\"y\":2,\"width\":30,\"height\":40.5}}", out var message));
            var selection = (SelectionChangedMessage)message!;
            CollectionAssert.AreEqual(new[] { "1", "2" }, (System.Collections.ICollection)selection.Ids);
            Assert.AreEqual("1", selection.PrimaryId);
            Assert.AreEqual(new SurfaceBounds(1, 2, 30, 40.5), selection.Bounds);

            Assert.IsTrue(WebSurfaceProtocol.TryParse("{\"type\":\"selectionChanged\",\"id\":\"\"}", out message));
            Assert.AreEqual(string.Empty, ((SelectionChangedMessage)message!).PrimaryId, "\"\" is the root element");

            Assert.IsTrue(WebSurfaceProtocol.TryParse("{\"type\":\"selectionChanged\",\"id\":null}", out message));
            Assert.IsNull(((SelectionChangedMessage)message!).PrimaryId);

            Assert.IsFalse(WebSurfaceProtocol.TryParse("{\"type\":\"selectionChanged\",\"id\":3}", out _));
        }

        [TestMethod]
        public void Parses_every_edit_kind()
        {
            Assert.IsTrue(WebSurfaceProtocol.TryParse("{\"type\":\"editRequest\",\"op\":{\"kind\":\"setAttribute\",\"elementId\":\"0\",\"name\":\"Text\",\"value\":\"OK\"}}", out var message));
            var op = ((EditRequestMessage)message!).Op;
            Assert.AreEqual(SurfaceEditKind.SetAttribute, op.Kind);
            Assert.AreEqual("Text", op.Name);
            Assert.AreEqual("OK", op.Value);

            Assert.IsTrue(WebSurfaceProtocol.TryParse("{\"type\":\"editRequest\",\"op\":{\"kind\":\"removeElement\",\"elementId\":\"2.1\"}}", out message));
            Assert.AreEqual(SurfaceEditKind.RemoveElement, ((EditRequestMessage)message!).Op.Kind);

            Assert.IsTrue(WebSurfaceProtocol.TryParse("{\"type\":\"editRequest\",\"op\":{\"kind\":\"insertChild\",\"parentId\":\"\",\"index\":2,\"xml\":\"<Button/>\"}}", out message));
            op = ((EditRequestMessage)message!).Op;
            Assert.AreEqual(SurfaceEditKind.InsertChild, op.Kind);
            Assert.AreEqual(string.Empty, op.ParentId);
            Assert.AreEqual(2, op.Index);
            Assert.AreEqual("<Button/>", op.Xml);

            Assert.IsTrue(WebSurfaceProtocol.TryParse("{\"type\":\"editRequest\",\"op\":{\"kind\":\"moveElement\",\"elementId\":\"1\",\"newParentId\":\"0\",\"index\":0}}", out message));
            Assert.AreEqual(SurfaceEditKind.MoveElement, ((EditRequestMessage)message!).Op.Kind);

            Assert.IsFalse(WebSurfaceProtocol.TryParse("{\"type\":\"editRequest\",\"op\":{\"kind\":\"setAttribute\",\"elementId\":\"0\",\"name\":\"Text\"}}", out _), "value missing");
            Assert.IsFalse(WebSurfaceProtocol.TryParse("{\"type\":\"editRequest\",\"op\":{\"kind\":\"explode\",\"elementId\":\"0\"}}", out _));
        }

        [TestMethod]
        public void Parses_drop_targets()
        {
            Assert.IsTrue(WebSurfaceProtocol.TryParse("{\"type\":\"dropTargetChanged\",\"target\":{\"valid\":true,\"parentId\":\"1\",\"index\":1,\"markerLeft\":10,\"markerTop\":20,\"markerRight\":110,\"markerBottom\":22}}", out var message));
            var target = ((DropTargetChangedMessage)message!).Target!;
            Assert.IsTrue(target.Valid);
            Assert.AreEqual("1", target.ParentId);
            Assert.AreEqual(1, target.Index);
            Assert.AreEqual(new SurfaceBounds(10, 20, 100, 2), target.Marker);

            Assert.IsTrue(WebSurfaceProtocol.TryParse("{\"type\":\"dropTargetChanged\",\"target\":null}", out message));
            Assert.IsNull(((DropTargetChangedMessage)message!).Target);
            Assert.IsFalse(WebSurfaceProtocol.TryParse("{\"type\":\"dropTargetChanged\",\"target\":{\"valid\":true}}", out _));
        }

        [TestMethod]
        public void Parses_the_web_additions()
        {
            Assert.IsTrue(WebSurfaceProtocol.TryParse("{\"type\":\"focusState\",\"editing\":true}", out var message));
            Assert.IsTrue(((FocusStateMessage)message!).Editing);

            Assert.IsTrue(WebSurfaceProtocol.TryParse("{\"type\":\"unhandledKey\",\"key\":\"F4\",\"ctrl\":false,\"shift\":true}", out message));
            var key = (UnhandledKeyMessage)message!;
            Assert.AreEqual("F4", key.Key);
            Assert.IsTrue(key.Shift);
            Assert.IsFalse(key.Alt);

            Assert.IsTrue(WebSurfaceProtocol.TryParse("{\"type\":\"metrics\",\"dpr\":1.75,\"width\":800,\"height\":600}", out message));
            Assert.AreEqual(1.75, ((MetricsMessage)message!).DevicePixelRatio);

            Assert.IsTrue(WebSurfaceProtocol.TryParse("{\"type\":\"toolboxDragDetected\"}", out message));
            Assert.IsInstanceOfType(message, typeof(ToolboxDragDetectedMessage));

            Assert.IsTrue(WebSurfaceProtocol.TryParse("{\"type\":\"surfaceError\",\"message\":\"bad\",\"line\":3,\"column\":4}", out message));
            Assert.AreEqual(3, ((SurfaceErrorMessage)message!).Line);
        }

        [TestMethod]
        public void Rejects_garbage_without_throwing()
        {
            foreach (var json in new[] { null, "", "  ", "not json", "[1,2]", "{\"type\":5}", "{\"no\":\"type\"}", "{\"type\":\"unknownThing\"}", "{\"type\":\"metrics\",\"dpr\":\"x\"}" })
            {
                Assert.IsFalse(WebSurfaceProtocol.TryParse(json, out var message), json);
                Assert.IsNull(message);
            }
        }
    }

    [TestClass]
    public sealed class AcceleratorRoutingTests
    {
        private const int VkDelete = 0x2E;
        private const int VkBack = 0x08;
        private const int VkTab = 0x09;
        private const int VkF4 = 0x73;
        private const int VkF7 = 0x76;
        private const int VkF2 = 0x71;
        private const int VkLeft = 0x25;

        [TestMethod]
        public void Visual_Studio_bindings_win_while_the_surface_is_not_editing_text()
        {
            Assert.AreEqual(KeyRoute.VisualStudio, AcceleratorRouting.Route('S', control: true, shift: false, alt: false, pageEditingText: false), "Ctrl+S");
            Assert.AreEqual(KeyRoute.VisualStudio, AcceleratorRouting.Route('Z', true, false, false, false), "Ctrl+Z is Edit.Undo");
            Assert.AreEqual(KeyRoute.VisualStudio, AcceleratorRouting.Route('Y', true, false, false, false), "Ctrl+Y is Edit.Redo");
            Assert.AreEqual(KeyRoute.VisualStudio, AcceleratorRouting.Route('B', true, true, false, false), "Ctrl+Shift+B");
            Assert.AreEqual(KeyRoute.VisualStudio, AcceleratorRouting.Route(VkF4, false, false, false, false), "F4");
            Assert.AreEqual(KeyRoute.VisualStudio, AcceleratorRouting.Route(VkF7, false, false, false, false), "F7");
            Assert.AreEqual(KeyRoute.VisualStudio, AcceleratorRouting.Route(VkDelete, false, false, false, false), "Del is Edit.Delete");
            Assert.AreEqual(KeyRoute.VisualStudio, AcceleratorRouting.Route(VkTab, true, false, false, false), "Ctrl+Tab is the window switcher");
            Assert.AreEqual(KeyRoute.VisualStudio, AcceleratorRouting.Route('F', false, false, true, false), "Alt+F opens the File menu");
        }

        [TestMethod]
        public void The_surface_keeps_its_navigation_keys()
        {
            Assert.AreEqual(KeyRoute.Page, AcceleratorRouting.Route(VkTab, false, false, false, false));
            Assert.AreEqual(KeyRoute.Page, AcceleratorRouting.Route(VkTab, false, true, false, false), "Shift+Tab");
            Assert.AreEqual(KeyRoute.Page, AcceleratorRouting.Route(VkLeft, false, false, false, false), "arrows nudge or move the selection");
            Assert.AreEqual(KeyRoute.Page, AcceleratorRouting.Route(VkF2, false, false, false, false), "F2 starts inline text editing");
        }

        [TestMethod]
        public void A_page_text_editor_keeps_the_text_editing_keys_only()
        {
            Assert.AreEqual(KeyRoute.Page, AcceleratorRouting.Route(VkDelete, false, false, false, true));
            Assert.AreEqual(KeyRoute.Page, AcceleratorRouting.Route(VkBack, false, false, false, true));
            Assert.AreEqual(KeyRoute.Page, AcceleratorRouting.Route('Z', true, false, false, true), "the editor's own undo");
            Assert.AreEqual(KeyRoute.Page, AcceleratorRouting.Route('V', true, false, false, true));
            Assert.AreEqual(KeyRoute.VisualStudio, AcceleratorRouting.Route('S', true, false, false, true), "Ctrl+S still saves");
            Assert.AreEqual(KeyRoute.VisualStudio, AcceleratorRouting.Route(VkF4, false, false, false, true));
            Assert.AreEqual(KeyRoute.VisualStudio, AcceleratorRouting.Route('B', true, true, false, true));
        }
    }

    [TestClass]
    public sealed class SpikeViewDocumentTests
    {
        private const string Sample =
            "<!-- spike -->\n" +
            "<Stack Direction=\"TopDown\" Gap='12'>\n" +
            "  <Label Text=\"Title\"/>\n" +
            "  <Stack Direction=\"LeftToRight\">\n" +
            "    <Button Text=\"Save\" Variant=\"Primary\" />\n" +
            "    <Button Text=\"Cancel\"/>\n" +
            "  </Stack>\n" +
            "  <!-- fields -->\n" +
            "  <TextField Placeholder=\"Name\"/>\n" +
            "</Stack>\n";

        private static SpikeViewDocument Doc(string text = Sample) => SpikeViewDocument.Parse(text, out var error) ?? throw new AssertFailedException(error ?? "not parsed");

        private static string Apply(SurfaceEditOp op, string text = Sample)
        {
            var change = Doc(text).Apply(op, out var error);
            Assert.IsNotNull(change, error);
            return change!.Value.ApplyTo(text);
        }

        [TestMethod]
        public void Element_ids_are_the_desktop_dot_paths()
        {
            var doc = Doc();
            Assert.AreEqual("Stack", doc.Find("")!.Name);
            Assert.AreEqual("Label", doc.Find("0")!.Name);
            Assert.AreEqual("Cancel", doc.Find("1.1")!.Attribute("Text"));
            Assert.AreEqual("TextField", doc.Find("2")!.Name, "comments are not elements");
            Assert.AreEqual("12", doc.Find("")!.Attribute("Gap"));
            Assert.IsNull(doc.Find("7"));
            Assert.IsNull(doc.Find("x"));
        }

        [TestMethod]
        public void Setting_an_attribute_touches_only_its_value()
        {
            var after = Apply(SurfaceEditOp.SetAttribute("1.0", "Text", "Save <all> & \"quit\""));
            Assert.AreEqual(Sample.Replace("Text=\"Save\"", "Text=\"Save &lt;all> &amp; &quot;quit&quot;\""), after);
            Assert.AreEqual("Save <all> & \"quit\"", Doc(after).Find("1.0")!.Attribute("Text"));

            Assert.AreEqual(Sample.Replace("Gap='12'", "Gap='16'"), Apply(SurfaceEditOp.SetAttribute("", "Gap", "16")), "single quotes kept");
        }

        [TestMethod]
        public void A_new_attribute_goes_after_the_last_one()
        {
            Assert.AreEqual(Sample.Replace("<Button Text=\"Cancel\"/>", "<Button Text=\"Cancel\" Variant=\"Ghost\"/>"), Apply(SurfaceEditOp.SetAttribute("1.1", "Variant", "Ghost")));
            Assert.AreEqual(Sample.Replace("Variant=\"Primary\" />", "Variant=\"Primary\" Enabled=\"false\" />"), Apply(SurfaceEditOp.SetAttribute("1.0", "Enabled", "false")));
        }

        [TestMethod]
        public void Removing_an_element_removes_its_line()
        {
            Assert.AreEqual(Sample.Replace("    <Button Text=\"Cancel\"/>\n", string.Empty), Apply(SurfaceEditOp.RemoveElement("1.1")));
            Assert.IsNull(Doc().Apply(SurfaceEditOp.RemoveElement(""), out var error));
            Assert.IsNotNull(error);
        }

        [TestMethod]
        public void Inserting_a_child_follows_the_siblings_indentation()
        {
            Assert.AreEqual(
                Sample.Replace("    <Button Text=\"Cancel\"/>\n", "    <CheckBox Text=\"x\"/>\n    <Button Text=\"Cancel\"/>\n"),
                Apply(SurfaceEditOp.InsertChild("1", 1, "<CheckBox Text=\"x\"/>")));
            Assert.AreEqual(
                Sample.Replace("  <TextField Placeholder=\"Name\"/>\n", "  <TextField Placeholder=\"Name\"/>\n  <Button Text=\"Button\"/>\n"),
                Apply(SurfaceEditOp.InsertChild("", 99, "<Button Text=\"Button\"/>")));
        }

        [TestMethod]
        public void Inserting_into_a_self_closing_container_opens_it()
        {
            const string text = "<Stack>\n  <Stack Gap=\"4\" />\n</Stack>\n";
            Assert.AreEqual("<Stack>\n  <Stack Gap=\"4\">\n    <Label/>\n  </Stack>\n</Stack>\n", Apply(SurfaceEditOp.InsertChild("0", 0, "<Label/>"), text));
        }

        [TestMethod]
        public void Refuses_invalid_edits()
        {
            Assert.IsNull(Doc().Apply(SurfaceEditOp.InsertChild("0", 0, "<Button/>"), out var error), "a Label is not a container");
            Assert.IsNotNull(error);
            Assert.IsNull(Doc().Apply(SurfaceEditOp.InsertChild("", 0, "<Button"), out error));
            Assert.IsNull(Doc().Apply(SurfaceEditOp.SetAttribute("9", "Text", "x"), out error));
            Assert.IsNull(Doc().Apply(SurfaceEditOp.SetAttribute("0", "bad name", "x"), out error));
        }

        [TestMethod]
        public void Moving_an_element_keeps_its_markup()
        {
            var after = Apply(SurfaceEditOp.MoveElement("1.1", "", 0));
            var doc = Doc(after);
            Assert.AreEqual("Cancel", doc.Find("0")!.Attribute("Text"));
            Assert.AreEqual(1, doc.Find("2")!.Children.Count);
            Assert.AreEqual("2", SpikeViewDocument.ShiftAfterRemoval("3", "1"));
            Assert.AreEqual("1.0", SpikeViewDocument.ShiftAfterRemoval("1.0", "2"));
            Assert.AreEqual("1.1", SpikeViewDocument.ShiftAfterRemoval("1.2", "1.0"));
        }

        [TestMethod]
        public void Changes_are_one_minimal_replacement()
        {
            var change = SpikeTextChange.Between("<A x=\"1\"/>", "<A x=\"22\"/>");
            Assert.AreEqual(6, change.Start);
            Assert.AreEqual(1, change.OldLength);
            Assert.AreEqual("22", change.NewText);
            Assert.IsTrue(SpikeTextChange.Between("same", "same").IsEmpty);
        }

        [TestMethod]
        public void Reports_malformed_markup_with_a_position()
        {
            Assert.IsNull(SpikeViewDocument.Parse("<Stack>\n  <Label>\n</Stack>", out var error));
            StringAssert.Contains(error, "(3,");
            Assert.IsNull(SpikeViewDocument.Parse("<A/><B/>", out _));
            Assert.IsNull(SpikeViewDocument.Parse("<A x=\"1\" x=\"2\"/>", out _));
        }
    }

    [TestClass]
    public sealed class WebDesignSpikeSupportTests
    {
        [TestMethod]
        public void The_profile_folder_is_per_hive()
        {
            Assert.AreEqual(@"C:\Users\u\AppData\Local\Kubuno\webview2\18.0_6f0e0a8bKubunoWV", WebView2Folders.UserDataFolder(@"C:\Users\u\AppData\Local", @"Software\Microsoft\VisualStudio\18.0_6f0e0a8bKubunoWV"));
            Assert.AreEqual("18.0_6f0e0a8b", WebView2Folders.HiveName(@"Software\Microsoft\VisualStudio\18.0_6f0e0a8b\"));
            Assert.AreEqual("default", WebView2Folders.HiveName(null));
            Assert.AreEqual("ab", WebView2Folders.HiveName("a:b"));
        }

        [TestMethod]
        public void Toolbox_text_channel_round_trips()
        {
            Assert.AreEqual("kubuno-toolbox:Button", SpikeElementCatalog.ToolboxText("Button"));
            Assert.AreEqual("Button", SpikeElementCatalog.ParseToolboxText("kubuno-toolbox:Button"));
            Assert.AreEqual("ProgressBar", SpikeElementCatalog.ParseToolboxText("kubuno-toolbox:ProgressBar"), "a desktop Toolbox item the spike does not draw");
            Assert.IsNull(SpikeElementCatalog.ParseToolboxText("hello"));
            Assert.IsNull(SpikeElementCatalog.ParseToolboxText("kubuno-toolbox:<script>"));
            Assert.AreEqual("<Button Text=\"Button\"/>", SpikeElementCatalog.ToolboxXml("Button"));
            Assert.AreEqual("<ProgressBar/>", SpikeElementCatalog.ToolboxXml("ProgressBar"));
            Assert.IsNull(SpikeElementCatalog.ToolboxXml("a b"));
            Assert.AreEqual("true", SpikeElementCatalog.Find("Button")!.Properties.Single(p => p.Name == "Enabled").Default, "an absent Enabled means enabled");
        }
    }
}
