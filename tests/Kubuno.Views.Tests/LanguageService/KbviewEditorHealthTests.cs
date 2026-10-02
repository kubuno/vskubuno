using Kubuno.Views.LanguageService;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Views.Tests.LanguageService
{
    [TestClass]
    public class KbviewEditorHealthTests
    {
        private const string Devenv = @"C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\devenv.exe";

        [TestMethod]
        public void RootSuffix_IsWhatFollowsTheInstanceId()
        {
            Assert.AreEqual("Exp", KbviewEditorHealth.RootSuffix(@"Software\Microsoft\VisualStudio\18.0_dc9e2338Exp"));
            Assert.AreEqual("KubunoXmlns", KbviewEditorHealth.RootSuffix(@"Software\Microsoft\VisualStudio\18.0_dc9e2338KubunoXmlns\"));
            Assert.AreEqual(string.Empty, KbviewEditorHealth.RootSuffix(@"Software\Microsoft\VisualStudio\18.0_dc9e2338"));
            Assert.AreEqual("Exp", KbviewEditorHealth.RootSuffix(@"Software\Microsoft\VisualStudio\18.0Exp"));
            Assert.AreEqual(string.Empty, KbviewEditorHealth.RootSuffix(null));
        }

        [TestMethod]
        public void RepairCommand_QuotesDevenvAndAddsTheRootSuffixOnlyWhenThereIsOne()
        {
            Assert.AreEqual($"\"{Devenv}\" /updateconfiguration", KbviewEditorHealth.RepairCommand(Devenv, @"Software\Microsoft\VisualStudio\18.0_dc9e2338"));
            Assert.AreEqual($"\"{Devenv}\" /updateconfiguration /rootsuffix Exp", KbviewEditorHealth.RepairCommand(Devenv, @"Software\Microsoft\VisualStudio\18.0_dc9e2338Exp"));
        }

        [TestMethod]
        public void LogLine_NamesTheFileTheCauseAndTheRepair()
        {
            var line = KbviewEditorHealth.LogLine(@"C:\app\src\main_view.kbview", "XML", KbviewEditorHealth.Cause.PackageNotLoaded, "LoadPackage failed with 0x80070002", "\"devenv.exe\" /updateconfiguration");
            StringAssert.Contains(line, "'main_view.kbview'");
            StringAssert.Contains(line, "content type 'XML'");
            StringAssert.Contains(line, "not registered");
            StringAssert.Contains(line, "0x80070002");
            StringAssert.Contains(line, "/updateconfiguration");

            var other = KbviewEditorHealth.LogLine(@"C:\app\src\row.kbcontrol", "XML", KbviewEditorHealth.Cause.OtherEditorChosen, null, "x");
            StringAssert.Contains(other, "another editor was chosen");
        }

        [TestMethod]
        public void InfoBarText_IsLocalized()
        {
            StringAssert.Contains(KbviewEditorHealth.InfoBarText(@"C:\v\a.kbview", KbviewEditorHealth.Cause.PackageNotLoaded, french: true), "« a.kbview »");
            StringAssert.Contains(KbviewEditorHealth.InfoBarText(@"C:\v\a.kbview", KbviewEditorHealth.Cause.PackageNotLoaded, french: false), "'a.kbview'");
        }

        [TestMethod]
        public void IsKbviewContentType()
        {
            Assert.IsTrue(KbviewEditorHealth.IsKbviewContentType("kbview"));
            Assert.IsFalse(KbviewEditorHealth.IsKbviewContentType("XML"));
            Assert.IsFalse(KbviewEditorHealth.IsKbviewContentType(null));
        }
    }
}
