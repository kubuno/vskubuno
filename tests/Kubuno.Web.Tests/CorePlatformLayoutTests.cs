using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Kubuno.Rust.Cargo.Naming;
using Kubuno.Web.Logic.DevCore;
using Kubuno.Web.Logic.Generation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Web.Tests
{
    /// <summary>
    /// The core repository in the platform layout (docs/WEB.md, "Solutions"; 2026-10-06): server/ (kubuno-core, its own
    /// workspace), common/ (the shared server crates, a second workspace), web/ (the host app and the npm packages),
    /// desktop/ (Kubuno Desktop, merged from the former kubuno/desktop repository: one workspace over desktop/common,
    /// windows, linux, macos) and mobile/ - its detection, its solution and where F5 finds the built core.
    /// </summary>
    [TestClass]
    public sealed class CorePlatformLayoutTests
    {
        private string _root = string.Empty;

        [TestInitialize]
        public void CreateRoot() => _root = Path.Combine(Path.GetTempPath(), "kubuno-core-platform-" + Guid.NewGuid().ToString("N"));

        [TestCleanup]
        public void DeleteRoot()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        [TestMethod]
        public void The_core_is_recognized_in_both_layouts()
        {
            var core = WriteCore();
            Assert.IsTrue(CoreRepository.IsCore(core));
            Assert.IsTrue(CoreRepository.IsPlatformLayout(core));
            Assert.AreEqual(Path.Combine(core, "server"), CoreRepository.ServerDirectory(core));
            Assert.AreEqual(Path.Combine(core, "server"), CoreRepository.ServerWorkspace(core));
            Assert.AreEqual(Path.Combine(core, "web"), CoreRepository.WebDirectory(core));
            Assert.AreEqual(Path.Combine(core, "web", "packages"), CoreRepository.PackagesDirectory(core));
            Assert.AreEqual(Path.Combine(core, "desktop"), CoreRepository.DesktopWorkspace(core));
            Assert.AreEqual(core, CoreRepository.RepositoryOf(Path.Combine(core, "server")), "the server's workspace belongs to the core");
            Assert.IsFalse(CoreRepository.IsCore(Path.Combine(core, "server")));

            // The former layout: crates/kubuno-core and frontend/.
            var former = Path.Combine(_root, "former");
            Write(Path.Combine(former, "Cargo.toml"), "[workspace]\nmembers = [\"crates/kubuno-core\"]\n");
            Write(Path.Combine(former, "crates", "kubuno-core", "Cargo.toml"), "[package]\nname = \"kubuno-core\"\n");
            Write(Path.Combine(former, "frontend", "package.json"), "{\"name\":\"kubuno-frontend\"}");
            Assert.IsTrue(CoreRepository.IsCore(former));
            Assert.IsFalse(CoreRepository.IsPlatformLayout(former));
            Assert.AreEqual(former, CoreRepository.ServerWorkspace(former));
            Assert.AreEqual(Path.Combine(former, "frontend", "packages"), CoreRepository.PackagesDirectory(former));

            // A module's server/ is not the core.
            var module = Path.Combine(_root, "drive");
            Write(Path.Combine(module, "server", "Cargo.toml"), "[package]\nname = \"kubuno-drive\"\n");
            Assert.IsFalse(CoreRepository.IsCore(module));
        }

        [TestMethod]
        public void The_core_solution_has_a_folder_per_part_with_kubuno_desktop()
        {
            var core = WriteCore();
            var repository = WebRepository.Detect(core, out var reason)!;
            Assert.IsNotNull(repository, reason);
            Assert.AreEqual(WebRepositoryKind.Core, repository.Kind);
            Assert.IsTrue(repository.IsPlatformLayout);
            Assert.AreEqual("kubuno-core", repository.RunPackage);
            Assert.AreEqual("kubuno-core", repository.RunBin);
            CollectionAssert.AreEquivalent(new[] { "kubuno-core", "kubuno-db", "kubuno-vectors" }, repository.Members.Select(member => member.PackageName).ToArray());

            var solutionPath = WebSolutionGenerator.DefaultSolutionPath(repository);
            Assert.AreEqual(Path.Combine(core, "Kubuno.Core.slnx"), solutionPath);
            var files = WebSolutionGenerator.Plan(new[] { repository }, solutionPath, WebSdkVersions.Current, null);
            var root = XDocument.Parse(files.Single(file => file.Mergeable).Content).Root!;
            CollectionAssert.AreEqual(
                new[] { "/Common/", "/Server/", "/Web/", "/Desktop/", "/Desktop/Common/", "/Desktop/Windows/", "/Desktop/Linux/", "/Desktop/macOS/", "/Mobile/" },
                root.Elements("Folder").Select(folder => (string)folder.Attribute("Name")!).ToArray());
            CollectionAssert.AreEquivalent(new[] { "common/kubuno-db/Kubuno.Core.Db.rsproj", "common/kubuno-vectors/Kubuno.Core.Vectors.rsproj" }, ProjectPaths(root, "/Common/"));
            CollectionAssert.AreEqual(new[] { "server/Kubuno.Core.Server.rsproj" }, ProjectPaths(root, "/Server/"));
            CollectionAssert.AreEquivalent(
                new[] { "web/Kubuno.Core.Frontend.esproj", "web/packages/ui/Kubuno.Web.UI.esproj", "web/packages/sdk/Kubuno.Web.Sdk.esproj", "web/packages/vectors/Kubuno.Web.Vectors.esproj" },
                ProjectPaths(root, "/Web/"));
            CollectionAssert.AreEquivalent(
                new[] { "desktop/common/kubuno-desktop-sync/Kubuno.Desktop.Sync.rsproj", "desktop/common/kubuno-desktop-shell-common/Kubuno.Desktop.Shell.Common.rsproj" },
                ProjectPaths(root, "/Desktop/Common/"));
            CollectionAssert.AreEquivalent(
                new[] { "desktop/windows/kubuno-desktop-ui/Kubuno.Desktop.UI.rsproj", "desktop/windows/kubuno-desktop-shell/Kubuno.Desktop.Shell.rsproj", "desktop/windows/kubuno-desktop-views-ls/Kubuno.Desktop.Views.LS.rsproj" },
                ProjectPaths(root, "/Desktop/Windows/"));
            CollectionAssert.AreEqual(new[] { "desktop/linux/kubuno-desktop-shell-linux/Kubuno.Desktop.Shell.Linux.rsproj" }, ProjectPaths(root, "/Desktop/Linux/"));
            CollectionAssert.AreEqual(new[] { "desktop/macos/kubuno-desktop-shell-macos/Kubuno.Desktop.Shell.Macos.rsproj" }, ProjectPaths(root, "/Desktop/macOS/"));
            CollectionAssert.AreEqual(new[] { "mobile/README.md" }, Folder(root, "/Mobile/").Elements("File").Select(file => (string)file.Attribute("Path")!).ToArray());

            // The host app builds after the packages built from its sources; the server and Kubuno Desktop stand alone.
            var app = Folder(root, "/Web/").Elements("Project").Single(project => (string)project.Attribute("Path")! == "web/Kubuno.Core.Frontend.esproj");
            CollectionAssert.Contains(app.Elements("BuildDependency").Select(dependency => (string)dependency.Attribute("Project")!).ToList(), "web/packages/ui/Kubuno.Web.UI.esproj");
            Assert.IsFalse(Folder(root, "/Server/").Descendants("BuildDependency").Any());

            // The server project: Kubuno.Web.Sdk (the current version) and the Core role; the shared crates build their workspace.
            var server = Content(files, "Kubuno.Core.Server.rsproj");
            StringAssert.Contains(server, "<Sdk Name=\"Kubuno.Web.Sdk\" Version=\"1.0.2\" />");
            StringAssert.Contains(server, "<KubunoWebRole>Core</KubunoWebRole>");
            StringAssert.Contains(server, "<CargoBin>kubuno-core</CargoBin>");
            StringAssert.Contains(Content(files, "Kubuno.Core.Db.rsproj"), "<CargoBuildScope>Workspace</CargoBuildScope>");
            StringAssert.Contains(Content(files, "Kubuno.Desktop.Shell.rsproj"), "<CargoBin>kubuno-desktop</CargoBin>");
            StringAssert.Contains(Content(files, "Kubuno.Web.UI.esproj"), "(core/web/packages/build.sh)");

            // F5: the core (and Vite), and Kubuno Desktop on Windows.
            var launch = files.Single(file => file.Path.EndsWith(".slnLaunch", StringComparison.Ordinal)).Content;
            StringAssert.Contains(launch, "\"Name\": \"Kubuno Core (server)\"");
            StringAssert.Contains(launch, "\"Path\": \"server\\\\Kubuno.Core.Server.rsproj\"");
            StringAssert.Contains(launch, "\"Name\": \"Kubuno Desktop (Windows)\"");
            StringAssert.Contains(launch, "\"Path\": \"desktop\\\\windows\\\\kubuno-desktop-shell\\\\Kubuno.Desktop.Shell.rsproj\"");
            Assert.AreEqual(@"server\Kubuno.Core.Server.rsproj", WebSolutionGenerator.StartupProject(new[] { repository }, solutionPath));

            Assert.IsTrue(files.All(file => file.Path.StartsWith(core + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)));
        }

        [TestMethod]
        public void A_module_finds_the_core_built_in_its_server_workspace()
        {
            var core = WriteCore();
            var module = Path.Combine(_root, "calendar");
            Directory.CreateDirectory(module);
            Assert.AreEqual(core, DevCoreLocator.SiblingCoreRepository(module));
            CollectionAssert.Contains(DevCoreLocator.Candidates(module, null, null).ToList(), Path.Combine(core, "server", "target", "debug", "kubuno-core.exe"));
        }

        private string WriteCore()
        {
            var core = Path.Combine(_root, "core");
            Write(Path.Combine(core, "server", "Cargo.toml"), "[package]\nname    = \"kubuno-core\"\nversion = \"0.1.15\"\n\n[[bin]]\nname = \"kubuno-core\"\npath = \"src/main.rs\"\n\n[[bin]]\nname = \"kubuno\"\npath = \"src/bin/kubuno/main.rs\"\n\n[workspace]\nresolver = \"2\"\n");
            Write(Path.Combine(core, "common", "Cargo.toml"), "[workspace]\nresolver = \"2\"\nmembers = [\"kubuno-db\", \"kubuno-vectors\"]\n");
            Write(Path.Combine(core, "common", "kubuno-db", "Cargo.toml"), "[package]\nname = \"kubuno-db\"\n");
            Write(Path.Combine(core, "common", "kubuno-vectors", "Cargo.toml"), "[package]\nname = \"kubuno-vectors\"\n");
            Write(Path.Combine(core, "common", "vectors", "sync", "outbox.json"), "{}");
            Write(Path.Combine(core, "web", "package.json"), "{\"name\":\"kubuno-frontend\",\"version\":\"0.1.15\"}");
            Directory.CreateDirectory(Path.Combine(core, "web", "src", "ui"));
            Write(Path.Combine(core, "web", "packages", "ui", "package.json"), "{\"name\":\"@kubuno/ui\",\"version\":\"0.1.17\"}");
            Write(Path.Combine(core, "web", "packages", "sdk", "package.json"), "{\"name\":\"@kubuno/sdk\",\"version\":\"0.1.13\"}");
            Write(Path.Combine(core, "web", "packages", "vectors", "package.json"), "{\"name\":\"@kubuno/vectors\",\"version\":\"0.1.0\"}");
            Write(Path.Combine(core, "desktop", "Cargo.toml"), "[workspace]\nresolver = \"2\"\nmembers = [\n  \"common/kubuno-desktop-sync\",\n  \"common/kubuno-desktop-shell-common\",\n  \"windows/kubuno-desktop-ui\",\n  \"windows/kubuno-desktop-shell\",\n  \"windows/kubuno-desktop-views-ls\",\n  \"linux/kubuno-desktop-shell-linux\",\n  \"macos/kubuno-desktop-shell-macos\",\n]\n");
            Write(Path.Combine(core, "desktop", "common", "kubuno-desktop-sync", "Cargo.toml"), "[package]\nname = \"kubuno-desktop-sync\"\n");
            Write(Path.Combine(core, "desktop", "common", "kubuno-desktop-shell-common", "Cargo.toml"), "[package]\nname = \"kubuno-desktop-shell-common\"\n");
            Write(Path.Combine(core, "desktop", "windows", "kubuno-desktop-ui", "Cargo.toml"), "[package]\nname = \"kubuno-desktop-ui\"\n");
            Write(Path.Combine(core, "desktop", "windows", "kubuno-desktop-shell", "Cargo.toml"), "[package]\nname = \"kubuno-desktop-shell\"\n\n[[bin]]\nname = \"kubuno-desktop\"\npath = \"src/main.rs\"\n");
            Write(Path.Combine(core, "desktop", "windows", "kubuno-desktop-views-ls", "Cargo.toml"), "[package]\nname = \"kubuno-desktop-views-ls\"\n\n[[bin]]\nname = \"kubuno-views-ls\"\npath = \"src/main.rs\"\n");
            Write(Path.Combine(core, "desktop", "linux", "kubuno-desktop-shell-linux", "Cargo.toml"), "[package]\nname = \"kubuno-desktop-shell-linux\"\n\n[[bin]]\nname = \"kubuno-desktop\"\npath = \"src/main.rs\"\n");
            Write(Path.Combine(core, "desktop", "macos", "kubuno-desktop-shell-macos", "Cargo.toml"), "[package]\nname = \"kubuno-desktop-shell-macos\"\n\n[[bin]]\nname = \"kubuno-desktop\"\npath = \"src/main.rs\"\n");
            Write(Path.Combine(core, "desktop", "target", "debug", "build", "x", "Cargo.toml"), "[package]\nname = \"build-output\"\n");
            Write(Path.Combine(core, "mobile", "README.md"), "# Kubuno mobile\n");
            return core;
        }

        private static XElement Folder(XElement root, string name) => root.Elements("Folder").Single(folder => (string)folder.Attribute("Name")! == name);

        private static string[] ProjectPaths(XElement root, string folder) => Folder(root, folder).Elements("Project").Select(project => (string)project.Attribute("Path")!).ToArray();

        private static string Content(IReadOnlyList<GeneratedFile> files, string fileName) =>
            files.Single(file => string.Equals(Path.GetFileName(file.Path), fileName, StringComparison.Ordinal)).Content;

        private static void Write(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }
    }
}
