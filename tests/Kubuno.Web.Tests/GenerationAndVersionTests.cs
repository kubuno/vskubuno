using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Kubuno.Web.Logic.Generation;
using Kubuno.Web.Logic.Versions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Web.Tests
{
    [TestClass]
    public sealed class GenerationAndVersionTests
    {
        private string _root = string.Empty;

        [TestInitialize]
        public void CreateRoot()
        {
            _root = Path.Combine(Path.GetTempPath(), "kubuno-web-gen-" + Guid.NewGuid().ToString("N"));
            WriteCore();
            WriteModule("calendar", "^0.1.1", "seccomp-v0.1.0");
        }

        [TestCleanup]
        public void DeleteRoot()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        private void WriteCore()
        {
            var core = Path.Combine(_root, "core");
            Write(Path.Combine(core, "Cargo.toml"), "[workspace]\nmembers = [\"crates/kubuno-core\", \"crates/kubuno-db\"]\n\n[workspace.package]\nversion = \"0.1.12\"\n");
            Write(Path.Combine(core, "crates", "kubuno-core", "Cargo.toml"), "[package]\nname = \"kubuno-core\"\n\n[[bin]]\nname = \"kubuno-core\"\npath = \"src/main.rs\"\n\n[[bin]]\nname = \"kubuno\"\npath = \"src/bin/kubuno/main.rs\"\n");
            Write(Path.Combine(core, "crates", "kubuno-db", "Cargo.toml"), "[package]\nname = \"kubuno-db\"\n");
            Write(Path.Combine(core, "frontend", "package.json"), "{\"version\":\"0.1.12\"}");
            Write(Path.Combine(core, "frontend", "vite.config.ts"), "export default {}");
            Write(Path.Combine(core, "frontend", "packages", "ui", "package.json"), "{\"name\":\"@kubuno/ui\",\"version\":\"0.1.12\"}");
            Write(Path.Combine(core, "frontend", "packages", "sdk", "package.json"), "{\"name\":\"@kubuno/sdk\",\"version\":\"0.1.10\"}");
            Write(Path.Combine(core, "frontend", "packages", "drive", "package.json"), "{\"name\":\"@kubuno/drive\",\"version\":\"0.1.7\"}");
            foreach (var folder in new[] { "ui", "sdk", "core", "drive", "app" })
            {
                Write(Path.Combine(core, "frontend", "src", folder, "index.ts"), "export {}");
            }
        }

        private string WriteModule(string id, string uiRange, string seccompTag)
        {
            var module = Path.Combine(_root, id);
            Write(Path.Combine(module, "Cargo.toml"), "[package]\nname = \"kubuno-" + id + "\"\nversion = \"0.1.8\"\n\n[[bin]]\nname = \"kubuno-" + id + "\"\npath = \"src/main.rs\"\n\n[workspace]\n\n[workspace.dependencies]\nkubuno-seccomp = { git = \"https://github.com/kubuno/core\", tag = \"" + seccompTag + "\", package = \"kubuno-seccomp\" }\n");
            Write(Path.Combine(module, "module.toml"), "[module]\nid = \"" + id + "\"\nversion = \"0.1.8\"\n\n[process]\nentrypoint = \"kubuno-" + id + "\"\n\n[[sidebar_items]]\npath = \"/" + id + "\"\n");
            Write(Path.Combine(module, "frontend", "package.json"), "{\n  \"version\": \"0.1.8\",\n  \"peerDependencies\": {\n    \"@kubuno/ui\": \"^0.1.1\"\n  },\n  \"devDependencies\": {\n    \"@kubuno/ui\": \"" + uiRange + "\",\n    \"@kubuno/sdk\": \"^0.1.10\"\n  }\n}\n");
            return module;
        }

        [TestMethod]
        public void The_core_is_detected_with_its_run_package_and_bin()
        {
            var core = WebRepository.Detect(Path.Combine(_root, "core"), out _)!;
            Assert.AreEqual(WebRepositoryKind.Core, core.Kind);
            Assert.AreEqual("kubuno-core", core.RunPackage);
            Assert.AreEqual("kubuno-core", core.RunBin);
            Assert.AreEqual(2, core.Members.Count);
            Assert.IsNull(WebRepository.Detect(_root, out var reason));
            Assert.IsNotNull(reason);
        }

        [TestMethod]
        public void The_core_solution_has_the_web_role_the_esproj_and_a_launch_profile()
        {
            var core = WebRepository.Detect(Path.Combine(_root, "core"), out _)!;
            var solutionPath = WebSolutionGenerator.DefaultSolutionPath(core);
            Assert.AreEqual("Kubuno.Core.Web.slnx", Path.GetFileName(solutionPath));
            var files = WebSolutionGenerator.Plan(new[] { core }, solutionPath, WebSdkVersions.Current, null).ToDictionary(file => Path.GetFileName(file.Path), file => file.Content);

            StringAssert.Contains(files["kubuno-core.rsproj"], "<Sdk Name=\"Kubuno.Web.Sdk\" Version=\"1.0.0\" />");
            StringAssert.Contains(files["kubuno-core.rsproj"], "<KubunoWebRole>Core</KubunoWebRole>");
            StringAssert.Contains(files["kubuno-core.rsproj"], "<CargoBin>kubuno-core</CargoBin>");
            StringAssert.Contains(files["kubuno-core.rsproj"], "<CargoBuildScope>Workspace</CargoBuildScope>");
            Assert.IsFalse(files["kubuno-db.rsproj"].Contains("Kubuno.Web.Sdk"), "a library needs no web tooling");
            StringAssert.Contains(files["kubuno-frontend.esproj"], "Microsoft.VisualStudio.JavaScript.Sdk/");
            StringAssert.Contains(files["kubuno-frontend.esproj"], "<KubunoWebRole>CoreFrontend</KubunoWebRole>");
            Assert.IsFalse(files["kubuno-frontend.esproj"].Contains("<JavaScriptTestFramework>"), "vitest is not declared to Test Explorer (docs/WEB.md, Tests)");
            StringAssert.Contains(files["launch.json"], "http://localhost:5173");

            var slnx = XDocument.Parse(files["Kubuno.Core.Web.slnx"]);
            var projects = slnx.Descendants("Project").Select(p => (string)p.Attribute("Path")!).ToList();
            CollectionAssert.Contains(projects, "crates/kubuno-core/kubuno-core.rsproj");
            CollectionAssert.Contains(projects, "frontend/kubuno-frontend.esproj");
            XElement Project(string end) => slnx.Descendants("Project").Single(p => ((string)p.Attribute("Path")!).EndsWith(end));
            string FolderOf(string end) => (string)Project(end).Parent!.Attribute("Name")!;
            Assert.AreEqual("/Server/", FolderOf("kubuno-core.rsproj"));
            Assert.AreEqual("/Libraries/", FolderOf("kubuno-db.rsproj"));
            Assert.AreEqual("/Frontend/", FolderOf("kubuno-frontend.esproj"));
            Assert.AreEqual("/Packages/", FolderOf("kubuno-ui.esproj"));
            Assert.AreEqual("/Packages/", FolderOf("kubuno-sdk.esproj"));
            Assert.AreEqual("/Packages/", FolderOf("kubuno-drive.esproj"));
            CollectionAssert.AreEqual(new[] { "/Server/", "/Libraries/", "/Frontend/", "/Packages/" }, slnx.Root!.Elements("Folder").Select(f => (string)f.Attribute("Name")!).ToList());
            Assert.AreEqual("AnyCPU", (string)Project("kubuno-frontend.esproj").Element("Platform")!.Attribute("Project")!);
            Assert.IsNotNull(Project("kubuno-frontend.esproj").Element("Build"), "an esproj mapped to AnyCPU needs <Build /> to be built");

            // The host app after the packages, the packages one after the other (one shared declaration emit).
            CollectionAssert.AreEquivalent(
                new[] { "frontend/packages/ui/kubuno-ui.esproj", "frontend/packages/sdk/kubuno-sdk.esproj", "frontend/packages/drive/kubuno-drive.esproj" },
                Project("kubuno-frontend.esproj").Elements("BuildDependency").Select(d => (string)d.Attribute("Project")!).ToList());
            Assert.AreEqual("frontend/packages/ui/kubuno-ui.esproj", (string)Project("kubuno-sdk.esproj").Element("BuildDependency")!.Attribute("Project")!);

            // A package built from the host's sources shows them and builds through kubuno-packages.mjs.
            StringAssert.Contains(files["kubuno-ui.esproj"], "<KubunoWebRole>Package</KubunoWebRole>");
            StringAssert.Contains(files["kubuno-ui.esproj"], "<KubunoPackageId>ui</KubunoPackageId>");
            StringAssert.Contains(files["kubuno-ui.esproj"], "<None Include=\"..\\..\\src\\ui\\**\\*\" Link=\"src\\ui\\%(RecursiveDir)%(Filename)%(Extension)\" />");
            StringAssert.Contains(files["kubuno-ui.esproj"], "<KubunoNodeModulesDirectory>");
            StringAssert.Contains(files["kubuno-frontend.esproj"], "src\\ui\\**");
            StringAssert.Contains(files["kubuno-frontend.esproj"], "packages\\ui\\**");
            foreach (var file in files.Where(pair => pair.Key.EndsWith("proj") || pair.Key.EndsWith(".slnx")))
            {
                XDocument.Parse(file.Value);
            }
            StringAssert.Contains(files["Kubuno.Core.Web.slnLaunch"], "\"Path\": \"crates\\\\kubuno-core\\\\kubuno-core.rsproj\"");
        }

        [TestMethod]
        public void A_module_backend_depends_on_its_frontend_and_merging_adds_only_missing_projects()
        {
            var module = WebRepository.Detect(Path.Combine(_root, "calendar"), out _)!;
            Assert.AreEqual(WebRepositoryKind.Module, module.Kind);
            var solutionPath = WebSolutionGenerator.DefaultSolutionPath(module);
            var first = WebSolutionGenerator.Plan(new[] { module }, solutionPath, WebSdkVersions.Current, null);
            var slnx = first.Single(file => file.Mergeable).Content;
            var backend = XDocument.Parse(slnx).Descendants("Project").Single(p => ((string)p.Attribute("Path")!).EndsWith(".rsproj"));
            Assert.AreEqual("frontend/calendar-frontend.esproj", (string)backend.Element("BuildDependency")!.Attribute("Project")!);
            StringAssert.Contains(first.Single(file => file.Path.EndsWith("launch.json")).Content, "http://localhost:8080/calendar");
            foreach (var file in first.Where(file => file.Path.EndsWith("proj") || file.Path.EndsWith(".slnx")))
            {
                // An XML comment with "--" in it made Visual Studio refuse to load the module's .esproj.
                XDocument.Parse(file.Content);
            }

            Assert.IsTrue(((string)XDocument.Parse(slnx).Root!.Descendants("Project").First().Attribute("Path")!).EndsWith(".rsproj"), "the backend comes first (default startup project)");

            var existing = "<Solution>\n  <!-- mine -->\n  <Project Path=\"kubuno-calendar.rsproj\" />\n  <Project Path=\"tools/other.csproj\" />\n</Solution>\n";
            var merged = WebSolutionGenerator.Plan(new[] { module }, solutionPath, WebSdkVersions.Current, existing).Single(file => file.Mergeable).Content;
            var paths = XDocument.Parse(merged).Descendants("Project").Select(p => (string)p.Attribute("Path")!).ToList();
            CollectionAssert.AreEquivalent(new[] { "kubuno-calendar.rsproj", "tools/other.csproj", "frontend/calendar-frontend.esproj" }, paths);
            StringAssert.Contains(merged, "<!-- mine -->");
        }

        [TestMethod]
        public void The_startup_project_is_the_program_never_a_library_and_libraries_get_no_launch_profile()
        {
            var core = WebRepository.Detect(Path.Combine(_root, "core"), out _)!;
            var calendar = WebRepository.Detect(Path.Combine(_root, "calendar"), out _)!;
            Assert.AreEqual(@"crates\kubuno-core\kubuno-core.rsproj", WebSolutionGenerator.StartupProject(new[] { core }, WebSolutionGenerator.DefaultSolutionPath(core)));
            Assert.AreEqual("kubuno-calendar.rsproj", WebSolutionGenerator.StartupProject(new[] { calendar }, WebSolutionGenerator.DefaultSolutionPath(calendar)));
            Assert.AreEqual(@"core\crates\kubuno-core\kubuno-core.rsproj", WebSolutionGenerator.StartupProject(new[] { calendar, core }, Path.Combine(_root, "Kubuno.Web.slnx")), "the core starts first in a multi-repository solution");

            var files = WebSolutionGenerator.Plan(new[] { core }, WebSolutionGenerator.DefaultSolutionPath(core), WebSdkVersions.Current, null);
            var launch = files.Single(file => file.Path.EndsWith(".slnLaunch")).Content;
            Assert.IsFalse(launch.Contains("kubuno-db"), "a library crate is never launched");
            var slnx = XDocument.Parse(files.Single(file => file.Mergeable).Content);
            Assert.AreEqual("crates/kubuno-core/kubuno-core.rsproj", (string)slnx.Root!.Descendants("Project").First().Attribute("Path")!, "the program comes first in the solution (Visual Studio's default startup project)");
            Assert.IsFalse(files.Single(file => file.Path.EndsWith("kubuno-db.rsproj")).Content.Contains("KubunoWebRole"));
        }

        [TestMethod]
        public void The_multi_repository_solution_puts_each_repository_in_its_folder()
        {
            var repositories = new[] { WebRepository.Detect(Path.Combine(_root, "core"), out _)!, WebRepository.Detect(Path.Combine(_root, "calendar"), out _)! };
            var files = WebSolutionGenerator.Plan(repositories, Path.Combine(_root, "Kubuno.Web.slnx"), WebSdkVersions.Current, null);
            var slnx = XDocument.Parse(files.Single(file => file.Mergeable).Content);
            var folders = slnx.Descendants("Folder").Select(f => (string)f.Attribute("Name")!).ToList();
            CollectionAssert.IsSubsetOf(new[] { "/core/", "/core/Server/", "/core/Libraries/", "/core/Frontend/", "/core/Packages/", "/calendar/", "/calendar/Server/", "/calendar/Frontend/" }, folders);
            foreach (var dependency in slnx.Descendants("BuildDependency"))
            {
                var from = (string)dependency.Parent!.Attribute("Path")!;
                var to = (string)dependency.Attribute("Project")!;
                Assert.AreEqual(from.Split('/')[0], to.Split('/')[0], "no dependency between two repositories: " + from + " -> " + to);
            }
            var launch = files.Single(file => file.Path.EndsWith(".slnLaunch")).Content;
            Assert.AreEqual(3, launch.Split(new[] { "\"Name\"" }, StringSplitOptions.None).Length - 1, "the server, the server with Vite, the module");
            StringAssert.Contains(launch, "\"Name\": \"Kubuno Core Web (serveur)\"");
        }

        [TestMethod]
        public void Cargo_workspace_members_of_a_module_are_projects_and_the_module_folder_is_kept()
        {
            var nas = Path.Combine(_root, "p2pnas");
            Write(Path.Combine(nas, "Cargo.toml"), "[workspace]\nmembers = [\"crates/core\", \"crates/server\"]\n");
            Write(Path.Combine(nas, "crates", "core", "Cargo.toml"), "[package]\nname = \"p2pnas-core\"\n");
            Write(Path.Combine(nas, "crates", "server", "Cargo.toml"), "[package]\nname = \"kubuno-p2pnas\"\n\n[[bin]]\nname = \"kubuno-p2pnas\"\npath = \"src/main.rs\"\n\n[dependencies]\np2pnas-core = { path = \"../core\" }\n");
            Write(Path.Combine(nas, "module.toml"), "[module]\nid = \"p2pnas\"\n\n[process]\nentrypoint = \"kubuno-p2pnas\"\n");
            Write(Path.Combine(nas, "frontend", "package.json"), "{\"name\":\"p2pnas-frontend\"}");

            var repository = WebRepository.Detect(nas, out _)!;
            Assert.AreEqual("kubuno-p2pnas", repository.RunPackage);
            Assert.AreEqual(0, ModuleIsolation.Violations(repository).Count, "a path dependency inside the module is fine");
            var files = WebSolutionGenerator.Plan(new[] { repository }, WebSolutionGenerator.DefaultSolutionPath(repository), WebSdkVersions.Current, null)
                .ToDictionary(file => Path.GetFileName(file.Path), file => file.Content);
            StringAssert.Contains(files["kubuno-p2pnas.rsproj"], "<KubunoModuleDirectory>$([System.IO.Path]::GetFullPath('$(MSBuildThisFileDirectory)..\\..'))</KubunoModuleDirectory>");
            StringAssert.Contains(files["kubuno-p2pnas.rsproj"], "<CargoBuildScope>Workspace</CargoBuildScope>");
            var slnx = XDocument.Parse(files["Kubuno.P2pnas.slnx"]);
            Assert.AreEqual("/Libraries/", (string)slnx.Descendants("Project").Single(p => ((string)p.Attribute("Path")!).EndsWith("p2pnas-core.rsproj")).Parent!.Attribute("Name")!);
            CollectionAssert.Contains(WebSolutionGenerator.Describe(repository).ToList(), "Libraries: p2pnas-core.rsproj (library)");
        }

        [TestMethod]
        public void A_module_reaching_another_module_or_the_core_sources_is_refused()
        {
            var calendar = Path.Combine(_root, "calendar");
            Write(Path.Combine(calendar, "frontend", "vite.config.ts"), "export default { resolve: { alias: { '@core': '../../core/frontend/src' } } }");
            var tasks = WriteModule("tasks", "^0.1.12", "seccomp-v0.1.1");
            File.AppendAllText(Path.Combine(tasks, "Cargo.toml"), "\n[dependencies]\nkubuno-calendar = { path = \"../calendar\" }\n");

            var calendarRepository = WebRepository.Detect(calendar, out _)!;
            var tasksRepository = WebRepository.Detect(tasks, out _)!;
            Assert.AreEqual(1, ModuleIsolation.Violations(calendarRepository).Count);
            Assert.AreEqual(1, ModuleIsolation.Violations(tasksRepository).Count);
            var core = WebRepository.Detect(Path.Combine(_root, "core"), out _)!;
            var error = Assert.ThrowsExactly<InvalidOperationException>(() =>
                WebSolutionGenerator.Plan(new[] { core, calendarRepository, tasksRepository }, Path.Combine(_root, "Kubuno.Web.slnx"), WebSdkVersions.Current, null));
            StringAssert.Contains(error.Message, "Module isolation");
            Assert.ThrowsExactly<InvalidOperationException>(() => ModuleIsolation.EnsureIsolated(
                new[] { core, calendarRepository },
                new[] { (Path.Combine(calendar, "kubuno-calendar.rsproj"), Path.Combine(_root, "core", "frontend", "packages", "ui", "kubuno-ui.esproj")) }));
        }

        [TestMethod]
        public void The_version_audit_and_prepare_plans_follow_the_scripts()
        {
            var core = Path.Combine(_root, "core");
            var calendar = Path.Combine(_root, "calendar");
            var published = VersionAudit.SourceNpmVersions(core);
            Assert.AreEqual("0.1.12", published["@kubuno/ui"]);

            Assert.AreEqual(VersionSeverity.Ok, VersionAudit.CheckAlignment(calendar).Single().Severity);
            var floors = VersionAudit.CheckNpmFloors(calendar, published).ToList();
            Assert.AreEqual(1, floors.Count, "the peer range is not a build floor; sdk is current");
            Assert.AreEqual(VersionSeverity.Warn, floors[0].Severity);

            var tooNew = WriteModule("tasks", "^0.2.0", "seccomp-v0.1.1");
            Assert.AreEqual(VersionSeverity.Fail, VersionAudit.CheckNpmFloors(tooNew, published).Single().Severity);

            var edits = VersionAudit.PlanNpmFloors(calendar, published);
            Assert.AreEqual(1, edits.Count);
            VersionAudit.Apply(edits);
            StringAssert.Contains(File.ReadAllText(Path.Combine(calendar, "frontend", "package.json")), "\"@kubuno/ui\": \"^0.1.12\"");

            var tags = VersionAudit.LatestTags(new[] { "v0.1.12", "seccomp-v0.1.0", "seccomp-v0.1.1", "db-v0.10.0", "db-v0.9.0" });
            Assert.AreEqual("seccomp-v0.1.1", tags["seccomp"]);
            Assert.AreEqual("db-v0.10.0", tags["db"]);
            Assert.AreEqual(1, VersionAudit.CheckSharedCrateTags(calendar, tags).Count());
            var tagEdits = VersionAudit.PlanSharedCrateTags(calendar, tags);
            VersionAudit.Apply(tagEdits);
            StringAssert.Contains(File.ReadAllText(Path.Combine(calendar, "Cargo.toml")), "tag = \"seccomp-v0.1.1\"");
            Assert.AreEqual(0, VersionAudit.PlanSharedCrateTags(calendar, tags).Count);
        }

        private static void Write(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }
    }
}

