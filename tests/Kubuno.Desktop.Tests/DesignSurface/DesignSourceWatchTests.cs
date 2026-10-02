using System.Linq;
using Kubuno.Desktop.Logic.DesignSurface;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Desktop.Tests.DesignSurface
{
    /// <summary>The "out of date" bar of the designer (docs/EVENTS.md, "User controls"): what is watched, what counts.</summary>
    [TestClass]
    public class DesignSourceWatchTests
    {
        [TestMethod]
        public void The_project_and_its_control_libraries_are_watched_not_kubuno_itself()
        {
            const string toml = "[package]\nname = \"app\"\n\n[dependencies]\nkubuno = { path = \"Z:/src/desktop/windows/src/crates/kubuno\" }\nui-lib = { path = \"../UiLib\" }\nserde = \"1\"\n";
            var dirs = DesignSourceWatch.WatchedDirectories(@"C:\s\App\Cargo.toml", toml);
            CollectionAssert.AreEqual(new[] { @"C:\s\App", @"C:\s\UiLib" }, dirs.ToArray());
        }

        [TestMethod]
        public void A_kubuno_named_control_library_is_watched_the_framework_crates_are_not()
        {
            const string toml = "[package]\nname = \"app\"\n\n[dependencies]\nkubuno = { path = \"../kubuno\" }\nkubuno-views = { path = \"../kubuno-views\" }\n" +
                "kubuno_ui = { path = \"../kubuno-ui\" }\nkubuno-shell-controls = { path = \"../ShellControls\" }\nkubuno-acme-widgets = { path = \"../Acme\" }\n";
            var dirs = DesignSourceWatch.WatchedDirectories(@"C:\s\App\Cargo.toml", toml);
            CollectionAssert.AreEqual(new[] { @"C:\s\App", @"C:\s\ShellControls", @"C:\s\Acme" }, dirs.ToArray());
        }

        [TestMethod]
        public void Only_files_declaring_controls_and_user_control_views_count()
        {
            Assert.IsTrue(DesignSourceWatch.AffectsDesign(@"C:\s\App\src\address_editor.rs", "#[derive(UserControl, Default)]\npub struct AddressEditor {}"));
            Assert.IsTrue(DesignSourceWatch.AffectsDesign(@"C:\s\App\src\round.rs", "#[derive(kubuno::views::component::Component, Default)] struct R;"));
            Assert.IsTrue(DesignSourceWatch.AffectsDesign(@"C:\s\App\src\address_editor.kbview", "<!-- an editor -->\n<UserControl x:Class=\"AddressEditor\"/>"));
            Assert.IsTrue(DesignSourceWatch.AffectsDesign(@"C:\s\App\src\address_editor.kbcontrol", "<UserControl x:Class=\"AddressEditor\"/>"), "a user control view (.kbcontrol)");
            Assert.IsFalse(DesignSourceWatch.AffectsDesign(@"C:\s\App\src\main_view.kbview", "<Panel DesignWidth=\"800\"/>"), "a form's own view is read live");
            Assert.IsFalse(DesignSourceWatch.AffectsDesign(@"C:\s\App\src\main_view.rs", "#[kubuno::view(\"main_view.kbview\")]\npub struct MainView {}"), "a form's code-behind");
            Assert.IsFalse(DesignSourceWatch.AffectsDesign(@"C:\s\App\target\debug\build\x.rs", "#[derive(UserControl)]"), "build output");
            Assert.IsFalse(DesignSourceWatch.AffectsDesign(@"C:\s\App\src\a.rs", null));
        }
    }
}
