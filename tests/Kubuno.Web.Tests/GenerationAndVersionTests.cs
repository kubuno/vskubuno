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
            Write(Path.Combine(core, "frontend", "packages", "ui", "package.json"), "{\"version\":\"0.1.12\"}");
            Write(Path.Combine(core, "frontend", "packages", "sdk", "package.json"), "{\"version\":\"0.1.10\"}");
            Write(Path.Combine(core, "frontend", "packages", "drive", "package.json"), "{\"version\":\"0.1.7\"}");
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
            StringAssert.Contains(files["kubuno-frontend.esproj"], "<JavaScriptTestFramework>Vitest</JavaScriptTestFramework>");
            StringAssert.Contains(files["launch.json"], "http://localhost:5173");

            var slnx = XDocument.Parse(files["Kubuno.Core.Web.slnx"]);
            var projects = slnx.Descendants("Project").Select(p => (string)p.Attribute("Path")!).ToList();
            CollectionAssert.Contains(projects, "crates/kubuno-core/kubuno-core.rsproj");
            CollectionAssert.Contains(projects, "frontend/kubuno-frontend.esproj");
            Assert.AreEqual("/Libraries/", (string)slnx.Descendants("Project").Single(p => ((string)p.Attribute("Path")!).EndsWith("kubuno-db.rsproj")).Parent!.Attribute("Name")!);
            Assert.AreEqual("AnyCPU", (string)slnx.Descendants("Project").Single(p => ((string)p.Attribute("Path")!).EndsWith(".esproj")).Element("Platform")!.Attribute("Project")!);
            Assert.IsNotNull(slnx.Descendants("Project").Single(p => ((string)p.Attribute("Path")!).EndsWith(".esproj")).Element("Build"), "an esproj mapped to AnyCPU needs <Build /> to be built");
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

            Assert.IsTrue(((string)XDocument.Parse(slnx).Root!.Elements("Project").First().Attribute("Path")!).EndsWith(".rsproj"), "the backend comes first (default startup project)");

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
            Assert.AreEqual("crates/kubuno-core/kubuno-core.rsproj", (string)slnx.Root!.Elements("Project").First().Attribute("Path")!, "the program comes first in the solution (Visual Studio's default startup project)");
            Assert.IsFalse(files.Single(file => file.Path.EndsWith("kubuno-db.rsproj")).Content.Contains("KubunoWebRole"));
        }

        [TestMethod]
        public void The_multi_repository_solution_puts_each_repository_in_its_folder()
        {
            var repositories = new[] { WebRepository.Detect(Path.Combine(_root, "core"), out _)!, WebRepository.Detect(Path.Combine(_root, "calendar"), out _)! };
            var files = WebSolutionGenerator.Plan(repositories, Path.Combine(_root, "Kubuno.Web.slnx"), WebSdkVersions.Current, null);
            var slnx = XDocument.Parse(files.Single(file => file.Mergeable).Content);
            var folders = slnx.Descendants("Folder").Select(f => (string)f.Attribute("Name")!).ToList();
            CollectionAssert.IsSubsetOf(new[] { "/core/", "/core/Libraries/", "/calendar/" }, folders);
            var launch = files.Single(file => file.Path.EndsWith(".slnLaunch")).Content;
            Assert.AreEqual(3, launch.Split(new[] { "\"Name\"" }, StringSplitOptions.None).Length - 1, "the server, the server with Vite, the module");
            StringAssert.Contains(launch, "\"Name\": \"Kubuno Core Web (serveur)\"");
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

