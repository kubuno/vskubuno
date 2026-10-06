using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Kubuno.Web.Logic.WebDesigner;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Web.Tests
{
    /// <summary>
    /// A project reached through a junction (<c>C:\kubuno-build</c> → <c>E:\kubuno-build</c>): the real path, the
    /// project-relative paths the designer sends and the dev server's discovery agree whichever spelling is used. The
    /// junction is created in a temporary folder (<c>mklink /J</c>, no administrator rights needed).
    /// </summary>
    [TestClass]
    public sealed class RealPathTests
    {
        private static string? s_base;
        private static string? s_real;
        private static string? s_link;

        [ClassInitialize]
        public static void CreateJunction(TestContext _)
        {
            s_base = Path.Combine(Path.GetTempPath(), "kubuno-realpath-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(s_base);
            // %TEMP% may be an 8.3 spelling (MARTIN~1): the expected paths are the real ones.
            s_base = RealPaths.Canonical(s_base);
            s_real = Path.Combine(s_base, "Real");
            s_link = Path.Combine(s_base, "link");
            Directory.CreateDirectory(Path.Combine(s_real, "frontend", "src", "menus"));
            File.WriteAllText(Path.Combine(s_real, "frontend", "kubuno.views.json"), "{ \"target\": \"web\" }");
            File.WriteAllText(Path.Combine(s_real, "frontend", "src", "menus", "WaffleMenu.kbcontrol"), "<UserControl/>");
            File.WriteAllText(Path.Combine(s_real, "frontend", "src", "menus", "WaffleMenu.ts"), "export class WaffleMenu {}");
            using var mklink = Process.Start(new ProcessStartInfo("cmd.exe", $"/d /c mklink /J \"{s_link}\" \"{s_real}\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true });
            mklink!.WaitForExit(10000);
            Assert.IsTrue(Directory.Exists(Path.Combine(s_link, "frontend")), "the junction could not be created");
        }

        [ClassCleanup]
        public static void RemoveJunction()
        {
            if (s_link is not null && Directory.Exists(s_link))
            {
                Directory.Delete(s_link); // the junction only, never its target's content
            }

            if (s_base is not null && Directory.Exists(s_base))
            {
                Directory.Delete(s_base, recursive: true);
            }
        }

        private static string Link(string relative) => Path.Combine(s_link!, relative);

        private static string Real(string relative) => Path.Combine(s_real!, relative);

        [TestMethod]
        public void A_junction_path_resolves_to_its_target_and_the_on_disk_case()
        {
            Assert.AreEqual(Real("frontend"), RealPaths.Canonical(Link("frontend")), ignoreCase: false);
            Assert.AreEqual(Real(@"frontend\src\menus\WaffleMenu.ts"), RealPaths.Canonical(Link(@"FRONTEND\SRC\menus\wafflemenu.TS")), ignoreCase: false);
            // A file not created yet: its existing folder is resolved, the rest kept.
            Assert.AreEqual(Real(@"frontend\.kubuno\design-server.json"), RealPaths.Canonical(Link(@"frontend\.kubuno\design-server.json")), ignoreCase: false);
            // Trailing separators and a real path are left as they are.
            Assert.AreEqual(Real("frontend"), RealPaths.Canonical(Real("frontend") + "\\"), ignoreCase: false);
        }

        [TestMethod]
        public void Same_and_RelativeTo_accept_either_spelling()
        {
            Assert.IsTrue(RealPaths.Same(Link("frontend"), Real("frontend")));
            Assert.IsTrue(RealPaths.Same(Real("frontend") + "\\", Link("FRONTEND")));
            Assert.IsFalse(RealPaths.Same(Link("frontend"), Real(@"frontend\src")));
            Assert.AreEqual(@"src\menus\WaffleMenu.kbcontrol", RealPaths.RelativeTo(Real("frontend"), Link(@"frontend\src\menus\WaffleMenu.kbcontrol")));
            Assert.AreEqual(@"src\menus\WaffleMenu.kbcontrol", RealPaths.RelativeTo(Link("frontend"), Real(@"frontend\src\menus\WaffleMenu.kbcontrol")));
            Assert.IsNull(RealPaths.RelativeTo(Link(@"frontend\src"), Real("frontend")));
        }

        [TestMethod]
        public void The_designer_sends_the_same_project_paths_whichever_spelling_opened_the_view()
        {
            var view = @"frontend\src\menus\WaffleMenu.kbcontrol";
            foreach (var (viewPath, other) in new[] { (Link(view), Real(view)), (Real(view), Link(view)) })
            {
                var project = WebViewProject.Find(viewPath);
                Assert.IsNotNull(project, viewPath);
                Assert.AreEqual("src/menus/WaffleMenu.kbcontrol", project!.RelativePath(viewPath));
                // A path in the other spelling (Vite hands out real paths, Visual Studio the opened ones).
                Assert.AreEqual("src/menus/WaffleMenu.kbcontrol", project.RelativePath(other));
                Assert.AreEqual("./WaffleMenu", project.CodeBehindModule(viewPath));
                CollectionAssert.AreEqual(new[] { "/src/menus/WaffleMenu" }, project.UserControls().Select(u => u.Module).ToArray());
            }
        }

        [TestMethod]
        public void A_dev_server_announced_with_either_spelling_serves_the_project()
        {
            static string Discovery(string root) => JsonSerializer.Serialize(new { version = 1, urls = new[] { "http://localhost:5173/" }, designPath = "/__kubuno_design__/", pid = 1, root });

            Assert.IsTrue(DesignServerDiscovery.ServesRoot(Discovery(Real("frontend")), Link("frontend")));
            Assert.IsTrue(DesignServerDiscovery.ServesRoot(Discovery(Link("frontend")), Real("frontend")));
            Assert.IsTrue(DesignServerDiscovery.ServesRoot(Discovery(Real("frontend").ToUpperInvariant()), Link("frontend")));
            // A discovery file copied from another checkout names another folder.
            Assert.IsFalse(DesignServerDiscovery.ServesRoot(Discovery(Path.Combine(s_base!, "other", "frontend")), Link("frontend")));
            // Written on another machine (a Linux checkout shared over the network), or without a root: nothing to compare.
            Assert.IsTrue(DesignServerDiscovery.ServesRoot(Discovery("/home/dev/kubuno/drive/frontend"), Link("frontend")));
            Assert.IsTrue(DesignServerDiscovery.ServesRoot("{\"version\":1,\"urls\":[]}", Link("frontend")));

            Assert.IsTrue(DesignServerDiscovery.StartedFromLink(Link("frontend"), RealPaths.Canonical));
            Assert.IsFalse(DesignServerDiscovery.StartedFromLink(Real("frontend"), RealPaths.Canonical));
            Assert.IsFalse(DesignServerDiscovery.StartedFromLink(Real("frontend").Replace('\\', '/'), RealPaths.Canonical));
            Assert.IsFalse(DesignServerDiscovery.StartedFromLink("/home/dev/x", RealPaths.Canonical));
        }
    }

    /// <summary>
    /// A module project (drive's <c>frontend</c>: <c>kubuno.views.json</c> with the host registry in <c>node_modules</c>, no
    /// <c>design.entry</c>): its dev server serves the design page from <c>@kubuno/host-runtime</c>, and the bundled design
    /// host must compile with the project's own registry.
    /// </summary>
    [TestClass]
    public sealed class ModuleDesignProjectTests
    {
        private const string Root = @"C:\repo\drive\frontend";

        private const string Registry = "{\"schema\":1,\"target\":\"web\",\"components\":[{\"name\":\"Label\",\"properties\":[{\"name\":\"Text\"},{\"name\":\"HtmlTag\"}]}]}";

        private static MemoryFiles DriveLike() => new MemoryFiles()
            .Add(@"C:\repo\drive\Cargo.toml", "[package]\nname = \"kubuno-drive\"")
            .Add(Root + @"\Kubuno.Drive.Web.esproj", "<Project/>")
            .Add(Root + @"\package.json", "{\"devDependencies\":{\"@kubuno/views-compiler\":\"^0.1.1\",\"@kubuno/ui\":\"^0.1.16\"}}")
            .Add(Root + @"\kubuno.views.json", "{ \"target\": \"web\", \"hostRegistry\": \"node_modules/@kubuno/ui/kbview-registry.web.json\", \"registries\": [], \"sources\": [\"src\"] }")
            .Add(Root + @"\node_modules\@kubuno\ui\kbview-registry.web.json", Registry)
            .Add(Root + @"\src\DriveApp.kbview", "<Stack/>")
            .Add(Root + @"\src\DriveApp.ts", "export class DriveApp {}");

        [TestMethod]
        public void A_module_without_host_runtime_has_no_design_page_on_its_dev_server()
        {
            var files = DriveLike();
            var project = WebViewProject.Find(Root + @"\src\DriveApp.kbview", files)!;
            Assert.IsNotNull(project);
            Assert.AreEqual(Root + @"\node_modules\@kubuno\ui\kbview-registry.web.json", project.HostRegistryPath, ignoreCase: true);
            Assert.IsTrue(project.UsesHostRuntimeEntry);
            Assert.IsNull(project.HostRuntimePackageJson());
            Assert.IsFalse(project.DesignPageAvailable);

            files.Add(Root + @"\node_modules\@kubuno\host-runtime\package.json", "{\"name\":\"@kubuno/host-runtime\",\"version\":\"0.1.1\"}");
            Assert.AreEqual(Root + @"\node_modules\@kubuno\host-runtime\package.json", project.HostRuntimePackageJson(), ignoreCase: true);
            Assert.IsTrue(project.DesignPageAvailable);
        }

        [TestMethod]
        public void Host_runtime_is_found_upwards_like_node_and_a_design_entry_needs_none()
        {
            var files = DriveLike().Add(@"C:\repo\drive\node_modules\@kubuno\host-runtime\package.json", "{}");
            Assert.IsTrue(WebViewProject.Find(Root + @"\src\DriveApp.kbview", files)!.DesignPageAvailable);

            // The core names its own design entry: no package needed.
            var core = new MemoryFiles()
                .Add(@"C:\repo\core\frontend\kubuno.views.json", "{ \"design\": { \"entry\": \"src/views/design/entry.tsx\" } }")
                .Add(@"C:\repo\core\frontend\src\A.kbview", "<Stack/>");
            var project = WebViewProject.Find(@"C:\repo\core\frontend\src\A.kbview", core)!;
            Assert.IsFalse(project.UsesHostRuntimeEntry);
            Assert.IsTrue(project.DesignPageAvailable);

            // An entry naming the package itself needs it.
            var named = new MemoryFiles()
                .Add(@"C:\repo\m\frontend\kubuno.views.json", "{ \"design\": { \"entry\": \"@kubuno/host-runtime/entry\" } }")
                .Add(@"C:\repo\m\frontend\src\A.kbview", "<Stack/>");
            Assert.IsFalse(WebViewProject.Find(@"C:\repo\m\frontend\src\A.kbview", named)!.DesignPageAvailable);
        }

        [TestMethod]
        public void The_bundled_page_gets_the_projects_own_host_registry()
        {
            var project = WebViewProject.Find(Root + @"\src\DriveApp.kbview", DriveLike())!;
            var text = project.HostRegistryText();
            Assert.AreEqual(Registry, text);
            Assert.IsTrue(WebDesignMessages.ShouldSendHostRegistry("bundled", text));
            Assert.IsFalse(WebDesignMessages.ShouldSendHostRegistry("project", text), "the project-mode page reads it from project.json");
            Assert.IsFalse(WebDesignMessages.ShouldSendHostRegistry(null, text), "no surfaceInfo yet");
            Assert.IsFalse(WebDesignMessages.ShouldSendHostRegistry("bundled", null));

            using var message = JsonDocument.Parse(WebDesignMessages.EncodeSetHostRegistry(text!));
            Assert.AreEqual("setHostRegistry", message.RootElement.GetProperty("type").GetString());
            Assert.AreEqual(Registry, message.RootElement.GetProperty("text").GetString());
        }
    }
}
