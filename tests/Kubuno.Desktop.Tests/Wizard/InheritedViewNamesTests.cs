using System.Linq;
using Kubuno.Desktop.TemplateWizard;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Desktop.Tests.Wizard
{
    /// <summary>The inherited form / inherited user control templates (visual inheritance, docs/EVENTS.md "User controls").</summary>
    [TestClass]
    public class InheritedViewNamesTests
    {
        private const string BaseForm = @"<!-- A base form -->
<Panel DesignWidth=""400"" DesignHeight=""200"" Title=""Base"">
  <Label x:Name=""header"" Text=""Base""/>
  <Button x:Name=""ok"" Modifiers=""Protected"" Text=""OK""/>
  <Panel x:Name=""body"">
    <TextField x:Name=""name"" Modifiers=""Public""/>
    <TextField x:Name=""secret""/>
  </Panel>
  <Panel>
    <Button x:Name=""unreachable"" Modifiers=""Public""/>
  </Panel>
</Panel>";

        [TestMethod]
        public void Overrides_list_the_changeable_controls_inside_their_named_containers()
        {
            var overrides = InheritedViewNames.Overrides(BaseForm);
            Assert.AreEqual("  <Button x:Name=\"ok\"/>\n  <Panel x:Name=\"body\">\n    <TextField x:Name=\"name\"/>\n  </Panel>\n", overrides);
            Assert.AreEqual(string.Empty, InheritedViewNames.Overrides("<Panel><Label x:Name=\"l\"/></Panel>"), "nothing to change: no element");
            Assert.AreEqual(string.Empty, InheritedViewNames.Overrides("not xml"));
        }

        [TestMethod]
        public void The_picker_offers_forms_or_user_controls()
        {
            var files = new[]
            {
                (@"C:\app\src\main_view.kbview", "<Panel/>"),
                (@"C:\app\address_editor.kbview", "<UserControl x:Class=\"AddressEditor\"/>"),
                (@"C:\app\broken.kbview", "<Panel"),
            };
            CollectionAssert.AreEqual(new[] { @"C:\app\src\main_view.kbview" }, InheritedViewNames.Candidates(files, userControl: false).Select(c => c.Path).ToArray());
            var uc = InheritedViewNames.Candidates(files, userControl: true).Single();
            Assert.AreEqual(("UserControl", true), (uc.RootName, uc.IsUserControl));
            Assert.AreEqual("../address_editor.kbview", InheritedViewNames.RelativePath(@"C:\app\src", @"C:\app\address_editor.kbview"));
        }

        [TestMethod]
        public void The_base_type_is_read_from_the_code_behind()
        {
            Assert.AreEqual("MainView", InheritedViewNames.BaseTypeName("use kubuno_desktop::prelude::*;\n#[kubuno_desktop::view(\"main_view.kbview\")]\n#[derive(Default)]\npub struct MainView {}", userControl: false));
            Assert.AreEqual("AddressEditor", InheritedViewNames.BaseTypeName("#[derive(UserControl, Default)]\n#[user_control(view = \"a.kbview\")]\npub struct AddressEditor { base: UserControlCore }", userControl: true));
            Assert.IsNull(InheritedViewNames.BaseTypeName("pub struct Nothing;", userControl: true));
            Assert.AreEqual("crate::address_editor::AddressEditor", InheritedViewNames.BaseTypePath(@"C:\app\address_editor.kbview", "AddressEditor"));
        }
    }
}
