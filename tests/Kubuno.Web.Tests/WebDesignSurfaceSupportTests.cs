using Kubuno.Web.Logic.WebDesigner;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Web.Tests
{
    /// <summary>Which keys of the WebView2 design surface go to Visual Studio (docs/WEB-VIEWS.md section 11, question 1).</summary>
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

    /// <summary>The WebView2 profile of the web design surface (docs/WEB-VIEWS.md section 11).</summary>
    [TestClass]
    public sealed class WebView2FoldersTests
    {
        [TestMethod]
        public void The_profile_folder_is_per_hive()
        {
            Assert.AreEqual(@"C:\Users\u\AppData\Local\Kubuno\webview2\18.0_6f0e0a8bKubunoWV", WebView2Folders.UserDataFolder(@"C:\Users\u\AppData\Local", @"Software\Microsoft\VisualStudio\18.0_6f0e0a8bKubunoWV"));
            Assert.AreEqual("18.0_6f0e0a8b", WebView2Folders.HiveName(@"Software\Microsoft\VisualStudio\18.0_6f0e0a8b\"));
            Assert.AreEqual("default", WebView2Folders.HiveName(null));
            Assert.AreEqual("ab", WebView2Folders.HiveName("a:b"));
        }
    }
}
