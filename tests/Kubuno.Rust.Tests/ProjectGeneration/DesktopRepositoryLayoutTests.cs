using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Kubuno.Rust.Logic.ProjectGeneration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Rust.Tests.ProjectGeneration
{
    /// <summary>The desktop repository's one solution: its folders, and how existing solutions are merged.</summary>
    [TestClass]
    public sealed class DesktopRepositoryLayoutTests
    {
        private static readonly string Repo = Path.Combine(Path.GetTempPath(), "kubuno-desktop-layout", "desktop");

        private static RsprojProjectPlanItem Item(string package, string relativeDirectory, bool program)
        {
            var directory = Path.Combine(Repo, relativeDirectory.Replace('/', Path.DirectorySeparatorChar));
            var project = Kubuno.Rust.Cargo.Naming.ProjectNaming.ForCrate(package);
            return new RsprojProjectPlanItem(package, Path.Combine(directory, project + ".rsproj"), Path.Combine(directory, "Cargo.toml"), RsprojPlanAction.Create, content: string.Empty, isLibraryOnly: !program);
        }

        private static readonly RsprojProjectPlanItem[] Members =
        {
            Item("kubuno-desktop-shell", "windows/src/shell", program: true),
            Item("kubuno-chat-desktop", "windows/src/chat", program: true),
            Item("kubuno-drive-desktop", "windows/src/drive/crates/kubuno-drive-desktop", program: true),
            Item("kubuno-drive-desktop-app-controls", "windows/src/drive/crates/kubuno-drive-desktop-app-controls", program: false),
            Item("kubuno-desktop", "windows/src/crates/kubuno-desktop", program: false),
            Item("kubuno-desktop-ui", "windows/src/crates/kubuno-desktop-ui", program: false),
            Item("kubuno-desktop-shell-controls", "windows/src/crates/kubuno-desktop-shell-controls", program: false),
            Item("kubuno-desktop-header-data", "windows/src/crates/kubuno-desktop-header-data", program: false),
            Item("kubuno-desktop-views-ls", "windows/src/crates/kubuno-desktop-views-ls", program: true),
            Item("kubuno-web-views-compiler-core", "windows/src/crates/kubuno-web-views-compiler-core", program: false),
            Item("kubuno-desktop-sync", "common/kubuno-desktop-sync", program: true),
            Item("kubuno-desktop-account", "common/kubuno-desktop-account", program: false),
        };

        [TestMethod]
        public void Every_member_goes_to_its_folder()
        {
            string Folder(string package) => DesktopRepositoryLayout.FolderOf(Members.Single(m => m.PackageName == package), Repo);

            Assert.AreEqual(DesktopRepositoryLayout.Applications, Folder("kubuno-desktop-shell"));
            Assert.AreEqual(DesktopRepositoryLayout.Applications, Folder("kubuno-chat-desktop"));
            Assert.AreEqual(DesktopRepositoryLayout.Applications, Folder("kubuno-drive-desktop"));
            Assert.AreEqual(DesktopRepositoryLayout.DriveEngine, Folder("kubuno-drive-desktop-app-controls"));
            Assert.AreEqual(DesktopRepositoryLayout.Framework, Folder("kubuno-desktop"));
            Assert.AreEqual(DesktopRepositoryLayout.Framework, Folder("kubuno-desktop-ui"));
            Assert.AreEqual(DesktopRepositoryLayout.SharedControls, Folder("kubuno-desktop-shell-controls"));
            Assert.AreEqual(DesktopRepositoryLayout.SharedControls, Folder("kubuno-desktop-header-data"));
            Assert.AreEqual(DesktopRepositoryLayout.Tools, Folder("kubuno-desktop-views-ls"));
            Assert.AreEqual(DesktopRepositoryLayout.Web, Folder("kubuno-web-views-compiler-core"));
            Assert.AreEqual(DesktopRepositoryLayout.Common, Folder("kubuno-desktop-sync"));
            Assert.AreEqual(DesktopRepositoryLayout.Common, Folder("kubuno-desktop-account"));
        }

        [TestMethod]
        public void A_fresh_solution_lists_the_folders_in_order_with_project_named_files()
        {
            var plan = RsprojSlnxGenerator.PlanWithFolders(Repo, null, Members, m => DesktopRepositoryLayout.FolderOf(m, Repo), DesktopRepositoryLayout.FolderOrder);
            var root = XDocument.Parse(plan.Content).Root!;
            var folders = root.Elements("Folder").Select(f => (string)f.Attribute("Name")!).ToArray();
            CollectionAssert.AreEqual(DesktopRepositoryLayout.FolderOrder.ToArray(), folders);
            Assert.IsFalse(root.Elements("Project").Any(), "no project at the solution root");
            var framework = root.Elements("Folder").Single(f => (string)f.Attribute("Name")! == DesktopRepositoryLayout.Framework);
            CollectionAssert.AreEqual(
                new[] { "windows/src/crates/kubuno-desktop/Kubuno.Desktop.rsproj", "windows/src/crates/kubuno-desktop-ui/Kubuno.Desktop.UI.rsproj" },
                framework.Elements("Project").Select(p => (string)p.Attribute("Path")!).ToArray());
            StringAssert.Contains(plan.Content, "<Folder Name=\"/Common (multi-OS)/\">");
        }

        [TestMethod]
        public void An_existing_solution_only_gets_its_missing_projects_in_their_folders()
        {
            var existing = "<Solution>\n  <Configurations>\n    <Platform Name=\"x64\" />\n  </Configurations>\n  <Folder Name=\"/Framework/\">\n    <Project Path=\"windows/src/crates/kubuno-desktop/Kubuno.Desktop.rsproj\" Type=\"6c7c4cb5-6e36-4c6f-9c6f-9c6e9b4d4c13\" />\n  </Folder>\n</Solution>\n";
            var plan = RsprojSlnxGenerator.PlanWithFolders(Repo, existing, Members.Where(m => m.PackageName is "kubuno-desktop" or "kubuno-desktop-ui" or "kubuno-desktop-account").ToArray(), m => DesktopRepositoryLayout.FolderOf(m, Repo), DesktopRepositoryLayout.FolderOrder);
            Assert.IsTrue(plan.Changed);
            CollectionAssert.AreEquivalent(new[] { "Kubuno.Desktop.UI", "Kubuno.Desktop.Account" }, plan.AddedProjectNames.ToArray());
            var root = XDocument.Parse(plan.Content).Root!;
            Assert.AreEqual(2, root.Elements("Folder").Single(f => (string)f.Attribute("Name")! == "/Framework/").Elements("Project").Count());
            Assert.AreEqual(1, root.Elements("Folder").Single(f => (string)f.Attribute("Name")! == "/Common (multi-OS)/").Elements("Project").Count());

            var again = RsprojSlnxGenerator.PlanWithFolders(Repo, plan.Content, Members.Where(m => m.PackageName is "kubuno-desktop" or "kubuno-desktop-ui" or "kubuno-desktop-account").ToArray(), m => DesktopRepositoryLayout.FolderOf(m, Repo), DesktopRepositoryLayout.FolderOrder);
            Assert.IsFalse(again.Changed);
        }

        [TestMethod]
        public void The_repository_workspaces_are_found_and_vendored_crates_skipped()
        {
            var root = Path.Combine(Path.GetTempPath(), "kubuno-ws-" + Guid.NewGuid().ToString("N"));
            try
            {
                Write(Path.Combine(root, "windows", "Cargo.toml"), "[workspace]\nmembers = []\n");
                Write(Path.Combine(root, "common", "Cargo.toml"), "[workspace]\nmembers = []\n");
                Write(Path.Combine(root, "windows", "target", "x", "Cargo.toml"), "[workspace]\n");
                Write(Path.Combine(root, "windows", "src", "Cargo.toml"), "[package]\nname = \"x\"\n");
                var found = DesktopRepositoryLayout.WorkspaceManifests(root).Select(p => p.Substring(root.Length + 1)).ToArray();
                CollectionAssert.AreEqual(new[] { Path.Combine("common", "Cargo.toml"), Path.Combine("windows", "Cargo.toml") }, found);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        private static void Write(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }
    }
}
