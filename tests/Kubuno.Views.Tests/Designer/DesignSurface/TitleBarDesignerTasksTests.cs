using System.Linq;
using Kubuno.Views.Designer.DesignSurface;
using Kubuno.Views.Designer.Menus;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Views.Tests.Designer.DesignSurface
{
    /// <summary>The title bar's smart tag (docs/SHELL-CONTROLS.md section 5): its tasks, their state and the buttons they add.</summary>
    [TestClass]
    public class TitleBarDesignerTasksTests
    {
        [TestMethod]
        public void Verbs_AddAButtonInEachRegion_ThenSwitchEveryStandardItem()
        {
            var verbs = TitleBarDesignerTasks.Verbs("<Panel Title=\"Drive\" ShowWaffle=\"true\" ShowHelp=\"false\"><Label/></Panel>");
            CollectionAssert.AreEqual(new[] { "Left", "Center", "Right" }, verbs.Where(v => v.Kind == MenuVerbKind.AddTitleBarButton).Select(v => v.Argument).ToList());
            var items = verbs.Where(v => v.Kind == MenuVerbKind.ToggleHeaderItem).ToList();
            CollectionAssert.AreEqual(TitleBarDesignerTasks.HeaderItems.ToList(), items.Select(v => v.Argument).ToList());
            Assert.IsTrue(items.Single(v => v.Argument == "ShowWaffle").Text.StartsWith("✓ "), "a shown item is checked");
            Assert.IsFalse(items.Single(v => v.Argument == "ShowHelp").Text.StartsWith("✓ "), "false is off");
            Assert.IsFalse(items.Single(v => v.Argument == "ShowAccount").Text.StartsWith("✓ "), "absent is off");
        }

        [TestMethod]
        public void Verbs_AreNone_ForAUserControl()
        {
            Assert.AreEqual(0, TitleBarDesignerTasks.Verbs("<UserControl x:Class=\"Row\"><Label/></UserControl>").Count);
        }

        [TestMethod]
        public void ButtonXml_TakesTheCaptionButtonsSize_Or36InTheTallHeader()
        {
            Assert.AreEqual("<IconButton TitleBar.Region=\"Right\" Icon=\"Star\" Diameter=\"30\" Glyph=\"16\" Width=\"30\" Height=\"30\"/>", TitleBarDesignerTasks.ButtonXml("<Panel/>", "Right"));
            Assert.AreEqual("<IconButton TitleBar.Region=\"Center\" Icon=\"Star\" Diameter=\"36\" Glyph=\"18\" Width=\"36\" Height=\"36\"/>", TitleBarDesignerTasks.ButtonXml("<Panel TitleBarHeight=\"64\"/>", "Center"));
        }

        [TestMethod]
        public void IsShown_ReadsTheRootAttribute()
        {
            Assert.IsTrue(TitleBarDesignerTasks.IsShown("<Panel ShowSearch=\"true\"/>", "ShowSearch"));
            Assert.IsTrue(TitleBarDesignerTasks.IsShown("<Panel ShowSearch=\"{Binding CanSearch}\"/>", "ShowSearch"));
            Assert.IsFalse(TitleBarDesignerTasks.IsShown("<Panel ShowSearch=\"false\"/>", "ShowSearch"));
            Assert.IsFalse(TitleBarDesignerTasks.IsShown("<Panel/>", "ShowSearch"));
        }
    }
}
