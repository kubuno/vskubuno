using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Kubuno.Web.Logic.WebDesigner;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Web.Tests
{
    /// <summary>In-memory files for <see cref="WebViewProject"/> (paths are compared case-insensitively, like Windows).</summary>
    internal sealed class MemoryFiles : IWebProjectFiles
    {
        private readonly Dictionary<string, string> _files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public MemoryFiles Add(string path, string text = "")
        {
            _files[Path.GetFullPath(path)] = text;
            return this;
        }

        public bool Exists(string path) => _files.ContainsKey(Path.GetFullPath(path));

        public string? ReadText(string path) => _files.TryGetValue(Path.GetFullPath(path), out var text) ? text : null;

        public IEnumerable<string> Files(string directory)
        {
            var dir = Path.GetFullPath(directory).TrimEnd('\\') + "\\";
            return _files.Keys.Where(f => f.StartsWith(dir, StringComparison.OrdinalIgnoreCase) && f.IndexOf('\\', dir.Length) < 0).ToList();
        }

        public IEnumerable<string> Directories(string directory)
        {
            var dir = Path.GetFullPath(directory).TrimEnd('\\') + "\\";
            return _files.Keys
                .Where(f => f.StartsWith(dir, StringComparison.OrdinalIgnoreCase) && f.IndexOf('\\', dir.Length) > 0)
                .Select(f => dir + f.Substring(dir.Length, f.IndexOf('\\', dir.Length) - dir.Length))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    [TestClass]
    public sealed class WebViewProjectTests
    {
        private const string Root = @"C:\repo\core\frontend";

        private static MemoryFiles CoreLike() => new MemoryFiles()
            .Add(@"C:\repo\core\Cargo.toml", "[workspace]")
            .Add(Root + @"\kubuno.views.json", "{ \"target\": \"web\", \"hostRegistry\": \"packages/ui/kbview-registry.web.json\", \"registries\": [\"src/core/shell/menus/kbview-controls.json\"], \"sources\": [\"src\"], \"design\": { \"entry\": \"src/views/design/entry.tsx\" } }")
            .Add(Root + @"\packages\ui\kbview-registry.web.json", "{}")
            .Add(Root + @"\src\core\shell\menus\kbview-controls.json", "{}")
            .Add(Root + @"\src\core\shell\menus\WaffleMenu.kbcontrol", "<UserControl/>")
            .Add(Root + @"\src\core\shell\menus\WaffleMenu.ts", "export class WaffleMenu {}")
            .Add(Root + @"\src\core\shell\menus\Row.kbcontrol", "<UserControl/>")
            .Add(Root + @"\src\pages\Home.kbview", "<Stack/>")
            .Add(Root + @"\node_modules\x\Ignored.kbcontrol", "<UserControl/>");

        [TestMethod]
        public void A_view_under_kubuno_views_json_is_a_web_view_and_reads_its_configuration()
        {
            var files = CoreLike();
            var project = WebViewProject.Find(Root + @"\src\core\shell\menus\WaffleMenu.kbcontrol", files);
            Assert.IsNotNull(project);
            Assert.AreEqual(Root, project!.Root, ignoreCase: true);
            Assert.AreEqual(Root + @"\packages\ui\kbview-registry.web.json", project.HostRegistryPath, ignoreCase: true);
            CollectionAssert.AreEqual(new[] { Root + @"\src\core\shell\menus\kbview-controls.json" }, project.RegistryPaths.ToArray());
            Assert.AreEqual("src/views/design/entry.tsx", project.DesignEntry);
            Assert.AreEqual("src/core/shell/menus/WaffleMenu.kbcontrol", project.RelativePath(Root + @"\src\core\shell\menus\WaffleMenu.kbcontrol"));
        }

        [TestMethod]
        public void Code_behind_class_and_design_data_follow_the_compiler()
        {
            var files = CoreLike();
            var project = WebViewProject.Find(Root + @"\src\core\shell\menus\WaffleMenu.kbcontrol", files)!;
            var view = Root + @"\src\core\shell\menus\WaffleMenu.kbcontrol";
            Assert.AreEqual(Root + @"\src\core\shell\menus\WaffleMenu.ts", project.CodeBehindOf(view), ignoreCase: true);
            Assert.AreEqual("./WaffleMenu", project.CodeBehindModule(view));
            Assert.IsNull(project.CodeBehindModule(Root + @"\src\core\shell\menus\Row.kbcontrol"));
            Assert.AreEqual("WaffleMenu", WebViewProject.ClassNameOf(view));
            Assert.AreEqual(view + ".design.json", WebViewProject.DesignDataPath(view));
        }

        [TestMethod]
        public void User_controls_are_the_kbcontrols_of_the_sources_with_their_module()
        {
            var project = WebViewProject.Find(Root + @"\src\pages\Home.kbview", CoreLike())!;
            var controls = project.UserControls();
            CollectionAssert.AreEqual(new[] { "Row", "WaffleMenu" }, controls.Select(c => c.Name).ToArray());
            Assert.AreEqual("/src/core/shell/menus/Row.kbcontrol", controls[0].Module, "no code-behind: the view itself");
            Assert.AreEqual("/src/core/shell/menus/WaffleMenu", controls[1].Module, "rendered by its code-behind");
        }

        [TestMethod]
        public void A_package_depending_on_the_compiler_is_a_web_project_with_defaults()
        {
            var files = new MemoryFiles()
                .Add(@"C:\repo\drive\frontend\package.json", "{ \"devDependencies\": { \"@kubuno/views-compiler\": \"^0.1.0\" } }")
                .Add(@"C:\repo\drive\frontend\node_modules\@kubuno\ui\kbview-registry.web.json", "{}")
                .Add(@"C:\repo\drive\frontend\src\Files.kbview", "<Stack/>");
            var project = WebViewProject.Find(@"C:\repo\drive\frontend\src\Files.kbview", files);
            Assert.IsNotNull(project);
            Assert.AreEqual(@"C:\repo\drive\frontend\node_modules\@kubuno\ui\kbview-registry.web.json", project!.HostRegistryPath, ignoreCase: true);
            CollectionAssert.AreEqual(new[] { @"C:\repo\drive\frontend\src" }, project.Sources.ToArray());
        }

        [TestMethod]
        public void Desktop_views_and_other_targets_are_not_web_views()
        {
            var files = new MemoryFiles()
                .Add(@"C:\repo\desktop\app\Cargo.toml", "[dependencies]\nkubuno-desktop = { path = \"..\" }")
                .Add(@"C:\repo\desktop\app\src\main_view.kbview", "<Form/>")
                .Add(@"C:\repo\desktop\app\src\package.json", "{ \"dependencies\": { \"react\": \"19\" } }")
                .Add(@"C:\repo\other\kubuno.views.json", "{ \"target\": \"desktop\" }")
                .Add(@"C:\repo\other\src\a.kbview", "<Form/>");
            Assert.IsNull(WebViewProject.Find(@"C:\repo\desktop\app\src\main_view.kbview", files), "a crate of kubuno-desktop");
            Assert.IsNull(WebViewProject.Find(@"C:\repo\other\src\a.kbview", files), "an explicit desktop target");
            Assert.IsNull(WebViewProject.Find(@"C:\nowhere\c.kbview", files));
            Assert.IsNull(WebViewProject.Find(null, files));
        }

        [TestMethod]
        public void The_profile_rule_of_the_language_server_decides()
        {
            var files = new MemoryFiles()
                .Add(@"C:\repo\core\Cargo.toml", "[workspace]\nmembers = [\"crates/*\"]")
                .Add(@"C:\repo\core\frontend\Kubuno.Core.Frontend.esproj", "<Project/>")
                .Add(@"C:\repo\core\frontend\src\A.kbview", "<Stack/>")
                .Add(@"C:\repo\bad\kubuno.views.json", "{ not json")
                .Add(@"C:\repo\bad\src\b.kbview", "<Form/>")
                .Add(@"C:\repo\both\Cargo.toml", "kubuno-desktop = \"1\"")
                .Add(@"C:\repo\both\package.json", "{ \"devDependencies\": { \"@kubuno/views-compiler\": \"1\" } }")
                .Add(@"C:\repo\both\src\c.kbview", "<Form/>");
            Assert.AreEqual(@"C:\repo\core\frontend", WebViewProject.Find(@"C:\repo\core\frontend\src\A.kbview", files)?.Root, true, "a JavaScript project");
            Assert.AreEqual(@"C:\repo\bad", WebViewProject.Find(@"C:\repo\bad\src\b.kbview", files)?.Root, true, "an unreadable kubuno.views.json is still the web compiler's");
            Assert.AreEqual(@"C:\repo\both", WebViewProject.Find(@"C:\repo\both\src\c.kbview", files)?.Root, true, "package.json is looked at before Cargo.toml in a folder");
        }
    }

    [TestClass]
    public sealed class WebRegistryTests
    {
        private const string Host = "{\"schema\":1,\"target\":\"web\",\"components\":[" +
            "{\"name\":\"Stack\",\"family\":\"core\",\"origin\":\"builtin\",\"web\":{\"module\":\"@kubuno/views\",\"export\":\"Stack\"}}," +
            "{\"name\":\"Button\",\"family\":\"core\",\"web\":{\"module\":\"@ui\",\"export\":\"Button\"}}]}";

        private const string Controls = "{\"schema\":1,\"components\":[" +
            "{\"name\":\"AppTileGrid\",\"family\":\"components\",\"web\":{\"module\":\"./AppTileGrid\",\"export\":\"AppTileGrid\",\"alternates\":[{\"module\":\"../shared/Grid\"}]}}," +
            "{\"name\":\"Button\",\"family\":\"core\",\"web\":{\"module\":\"./Shadow\"}}," +
            "{\"name\":\"Remote\",\"family\":\"components\",\"web\":{\"module\":\"@ui\"}}]}";

        [TestMethod]
        public void Host_first_then_project_controls_marked_and_rewritten()
        {
            var registry = WebRegistry.Build(Host, new[] { ("src/core/shell/menus/kbview-controls.json", Controls) });
            Assert.AreEqual(2, registry.HostCount);
            Assert.AreEqual(2, registry.ProjectCount, "a project element never shadows a host one");
            using var all = JsonDocument.Parse(registry.ComponentsJson);
            var names = all.RootElement.EnumerateArray().Select(c => c.GetProperty("name").GetString()).ToArray();
            CollectionAssert.AreEqual(new[] { "Stack", "Button", "AppTileGrid", "Remote" }, names);
            var grid = all.RootElement[2];
            Assert.AreEqual("project", grid.GetProperty("origin").GetString());
            Assert.AreEqual("/src/core/shell/menus/AppTileGrid", grid.GetProperty("web").GetProperty("module").GetString());
            Assert.AreEqual("/src/core/shell/shared/Grid", grid.GetProperty("web").GetProperty("alternates")[0].GetProperty("module").GetString());
            Assert.AreEqual("@ui", all.RootElement[3].GetProperty("web").GetProperty("module").GetString(), "a host specifier is kept");
            using var project = JsonDocument.Parse(registry.ProjectComponentsJson);
            Assert.AreEqual(2, project.RootElement.GetArrayLength());
        }

        [TestMethod]
        public void The_version_follows_the_content_and_garbage_is_ignored()
        {
            var a = WebRegistry.Build(Host, Array.Empty<(string, string)>());
            var b = WebRegistry.Build(Host, new[] { ("x.json", Controls) });
            Assert.AreNotEqual(a.Version, b.Version);
            Assert.AreEqual(a.Version, WebRegistry.Build(Host, Array.Empty<(string, string)>()).Version);
            var broken = WebRegistry.Build("{ nope", new[] { ("x.json", "[") });
            Assert.AreEqual(0, broken.HostCount);
            Assert.AreEqual("[]", broken.ComponentsJson);
        }

        [TestMethod]
        public void Posix_normalisation()
        {
            Assert.AreEqual("src/a/b", WebRegistry.NormalizePosix("src/./a/x/../b"));
            Assert.AreEqual("../up", WebRegistry.NormalizePosix("../up"));
        }
    }

    [TestClass]
    public sealed class DesignServerDiscoveryTests
    {
        [TestMethod]
        public void Candidate_urls_put_local_ones_first_and_end_with_the_design_path()
        {
            var json = "{\"version\":1,\"urls\":[\"http://192.168.1.220:5173/\",\"http://localhost:5173/\",\"ftp://x/\",\"nope\"],\"designPath\":\"/__kubuno_design__/\",\"pid\":4242}";
            var urls = DesignServerDiscovery.CandidateUrls(json);
            CollectionAssert.AreEqual(new[] { "http://localhost:5173/__kubuno_design__/", "http://192.168.1.220:5173/__kubuno_design__/" }, urls.Select(u => u.AbsoluteUri).ToArray());
            Assert.AreEqual(4242, DesignServerDiscovery.ProcessId(json));
            Assert.AreEqual("http://localhost:5173/__kubuno_design__/project.json", DesignServerDiscovery.ProjectInfoUrl(urls[0]).AbsoluteUri);
        }

        [TestMethod]
        public void Missing_designPath_defaults_and_bad_files_give_nothing()
        {
            var urls = DesignServerDiscovery.CandidateUrls("{\"urls\":[\"http://127.0.0.1:5199\"]}");
            Assert.AreEqual("http://127.0.0.1:5199/__kubuno_design__/", urls.Single().AbsoluteUri);
            Assert.AreEqual(0, DesignServerDiscovery.CandidateUrls("{\"version\":2,\"urls\":[\"http://localhost:1/\"]}").Count, "another version");
            Assert.AreEqual(0, DesignServerDiscovery.CandidateUrls("{ broken").Count);
            Assert.AreEqual(0, DesignServerDiscovery.CandidateUrls(null).Count);
            Assert.IsNull(DesignServerDiscovery.ProcessId("{}"));
            Assert.AreEqual(Path.Combine(@"C:\p", ".kubuno", "design-server.json"), DesignServerDiscovery.FilePath(@"C:\p"));
        }
    }

    [TestClass]
    public sealed class WebDesignMessagesTests
    {
        [TestMethod]
        public void Document_info_carries_the_design_data_as_json()
        {
            var json = WebDesignMessages.EncodeSetDocumentInfo("src/a/W.kbcontrol", "./W", "W", new[] { new WebUserControl("W", "/src/a/W") }, "{ \"props\": { \"apps\": [1, 2] }, }");
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            Assert.AreEqual("setDocumentInfo", root.GetProperty("type").GetString());
            Assert.AreEqual("src/a/W.kbcontrol", root.GetProperty("file").GetString());
            Assert.AreEqual("./W", root.GetProperty("codeBehind").GetString());
            Assert.AreEqual("/src/a/W", root.GetProperty("userControls")[0].GetProperty("module").GetString());
            Assert.AreEqual(2, root.GetProperty("designData").GetProperty("props").GetProperty("apps").GetArrayLength());
            using var none = JsonDocument.Parse(WebDesignMessages.EncodeSetDocumentInfo("v.kbview", null, null, Array.Empty<WebUserControl>(), "{ broken"));
            Assert.AreEqual(JsonValueKind.Null, none.RootElement.GetProperty("designData").ValueKind);
            Assert.AreEqual(JsonValueKind.Null, none.RootElement.GetProperty("codeBehind").ValueKind);
        }

        [TestMethod]
        public void The_handshake_checks_version_target_and_abi()
        {
            Assert.IsNull(WebDesignMessages.CheckSurfaceInfo("{\"type\":\"surfaceInfo\",\"version\":1,\"target\":\"web\",\"views\":\"1\",\"mode\":\"project\"}"));
            Assert.IsNull(WebDesignMessages.CheckSurfaceInfo("{\"type\":\"surfaceInfo\",\"version\":1,\"target\":\"web\",\"views\":1}"), "a number ABI");
            StringAssert.Contains(WebDesignMessages.CheckSurfaceInfo("{\"type\":\"surfaceInfo\",\"version\":2,\"target\":\"web\"}"), "version 2");
            StringAssert.Contains(WebDesignMessages.CheckSurfaceInfo("{\"type\":\"surfaceInfo\",\"version\":1,\"target\":\"desktop\"}"), "desktop");
            StringAssert.Contains(WebDesignMessages.CheckSurfaceInfo("{\"type\":\"surfaceInfo\",\"version\":1,\"target\":\"web\",\"views\":\"2\"}"), "ABI 2");
            Assert.AreEqual("bundled", WebDesignMessages.SurfaceMode("{\"type\":\"surfaceInfo\",\"mode\":\"bundled\"}"));
        }

        [TestMethod]
        public void Page_messages_are_read_without_throwing()
        {
            Assert.AreEqual("ready", WebDesignMessages.TypeOf("{\"type\":\"ready\"}"));
            Assert.IsNull(WebDesignMessages.TypeOf("[1]"));
            Assert.IsNull(WebDesignMessages.TypeOf("{ nope"));
            Assert.IsTrue(WebDesignMessages.FlagOf("{\"type\":\"focusState\",\"editing\":true}", "editing"));
            Assert.IsFalse(WebDesignMessages.FlagOf("garbage", "editing"));
            Assert.AreEqual("z", WebDesignMessages.StringOf("{\"key\":\"z\"}", "key"));
            Assert.AreEqual(0x5A, WebDesignMessages.VirtualKeyOf("z"));
            Assert.AreEqual(0x2E, WebDesignMessages.VirtualKeyOf("Delete"));
            Assert.AreEqual(0x76, WebDesignMessages.VirtualKeyOf("F7"));
            Assert.AreEqual(0, WebDesignMessages.VirtualKeyOf("Dead"));
            Assert.AreEqual("{\"type\":\"setZoom\",\"zoom\":1.25}", WebDesignMessages.EncodeSetZoom(1.25));
            Assert.AreEqual("{\"type\":\"setLanguage\",\"lang\":\"ar\"}", WebDesignMessages.EncodeSetLanguage("ar"));
        }
    }

    [TestClass]
    public sealed class DesignSurfaceKeyRoutingTests
    {
        [TestMethod]
        public void The_design_surface_keeps_its_editing_keys()
        {
            Assert.AreEqual(KeyRoute.Page, AcceleratorRouting.RouteDesignSurface(0x2E, false, false, false, false), "Del removes the selection");
            Assert.AreEqual(KeyRoute.Page, AcceleratorRouting.RouteDesignSurface(0x08, false, false, false, false));
            Assert.AreEqual(KeyRoute.Page, AcceleratorRouting.RouteDesignSurface(0x25, false, true, false, false), "Shift+Left nudges");
            Assert.AreEqual(KeyRoute.Page, AcceleratorRouting.RouteDesignSurface('C', true, false, false, false));
            Assert.AreEqual(KeyRoute.Page, AcceleratorRouting.RouteDesignSurface('D', true, false, false, false));
        }

        [TestMethod]
        public void Visual_Studio_keeps_undo_save_properties_and_views()
        {
            Assert.AreEqual(KeyRoute.VisualStudio, AcceleratorRouting.RouteDesignSurface('Z', true, false, false, false));
            Assert.AreEqual(KeyRoute.VisualStudio, AcceleratorRouting.RouteDesignSurface('Y', true, false, false, false));
            Assert.AreEqual(KeyRoute.VisualStudio, AcceleratorRouting.RouteDesignSurface('S', true, false, false, false));
            Assert.AreEqual(KeyRoute.VisualStudio, AcceleratorRouting.RouteDesignSurface(0x73, false, false, false, false), "F4");
            Assert.AreEqual(KeyRoute.VisualStudio, AcceleratorRouting.RouteDesignSurface(0x76, false, true, false, false), "Shift+F7");
            Assert.AreEqual(KeyRoute.VisualStudio, AcceleratorRouting.RouteDesignSurface('C', true, true, false, false), "Ctrl+Shift+C is not the designer's");
            Assert.AreEqual(KeyRoute.VisualStudio, AcceleratorRouting.RouteDesignSurface(0x2E, false, false, true, false), "Alt chords");
            Assert.AreEqual(KeyRoute.Page, AcceleratorRouting.RouteDesignSurface('Z', true, false, false, true), "a page text editor keeps its own undo");
        }
    }
}
