using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kubuno.Views.Logic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Views.Tests.LanguageService
{
    /// <summary>The profile detection shared with kubuno-views-ls (docs/WEB-VIEWS.md section 5, WV-7).</summary>
    [TestClass]
    public class ViewTargetsTests
    {
        private static (ViewTarget Target, string? Root) Detect(string view, Dictionary<string, string> files) =>
            ViewTargets.Detect(
                view,
                files.ContainsKey,
                p => files.TryGetValue(p, out var t) ? t : null,
                dir => files.Keys.Where(k => string.Equals(Path.GetDirectoryName(k), dir, System.StringComparison.OrdinalIgnoreCase)).ToList());

        private static string P(params string[] parts) => Path.Combine(new[] { Path.GetPathRoot(Path.GetTempPath())! }.Concat(parts).ToArray());

        [TestMethod]
        public void Nothing_found_is_the_desktop()
        {
            Assert.AreEqual(ViewTarget.Desktop, Detect(P("app", "src", "main_view.kbview"), new Dictionary<string, string>()).Target);
        }

        [TestMethod]
        public void The_core_frontend_is_web_through_its_views_configuration()
        {
            var files = new Dictionary<string, string>
            {
                [P("core", "Cargo.toml")] = "[workspace]",
                [P("core", "frontend", "kubuno.views.json")] = "{ \"target\": \"web\", \"sources\": [\"src\"] }",
                [P("core", "frontend", "Kubuno.Core.Frontend.esproj")] = "<Project/>",
            };
            var found = Detect(P("core", "frontend", "src", "core", "shell", "menus", "AccountMenu.kbcontrol"), files);
            Assert.AreEqual(ViewTarget.Web, found.Target);
            Assert.AreEqual(P("core", "frontend"), found.Root);
        }

        [TestMethod]
        public void A_package_with_the_compiler_or_an_esproj_is_web_and_an_explicit_target_wins()
        {
            var files = new Dictionary<string, string>
            {
                [P("notes", "Cargo.toml")] = "[package]\nname = \"notes\"",
                [P("notes", "frontend", "package.json")] = "{ \"devDependencies\": { \"@kubuno/views-compiler\": \"^0.1.0\" } }",
            };
            Assert.AreEqual(ViewTarget.Web, Detect(P("notes", "frontend", "src", "A.kbview"), files).Target);

            files.Remove(P("notes", "frontend", "package.json"));
            files[P("notes", "frontend", "Kubuno.Notes.Frontend.esproj")] = "<Project/>";
            Assert.AreEqual(ViewTarget.Web, Detect(P("notes", "frontend", "src", "A.kbview"), files).Target);

            files[P("notes", "frontend", "src", "kubuno.views.json")] = "{\"target\" : \"desktop\"}";
            Assert.AreEqual(ViewTarget.Desktop, Detect(P("notes", "frontend", "src", "A.kbview"), files).Target);
        }

        [TestMethod]
        public void A_desktop_crate_is_the_desktop()
        {
            var files = new Dictionary<string, string>
            {
                [P("desktop", "src", "shell", "Cargo.toml")] = "[dependencies]\nkubuno-desktop = { path = \"..\" }",
                [P("desktop", "kubuno.views.json")] = "{}",
            };
            Assert.AreEqual(ViewTarget.Desktop, Detect(P("desktop", "src", "shell", "src", "main_view.kbview"), files).Target);
        }
    }
}
