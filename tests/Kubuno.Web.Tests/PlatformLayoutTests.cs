using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Kubuno.Web.Logic.Generation;
using Kubuno.Web.Logic.Modules;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Web.Tests
{
    /// <summary>
    /// The platform layout of a module repository (docs/WEB.md, "Solutions"): common/, server/, web/, desktop/ (one
    /// workspace, desktop/common|windows|linux|macos), mobile/ - its solution folders, the transition from the former
    /// layout, and the module's files for F5 and the .kbpkg (ModuleLayout).
    /// </summary>
    [TestClass]
    public sealed class PlatformLayoutTests
    {
        private const string RsprojType = "6c7c4cb5-6e36-4c6f-9c6f-9c6e9b4d4c13";

        private string _root = string.Empty;

        [TestInitialize]
        public void CreateRoot() => _root = Path.Combine(Path.GetTempPath(), "kubuno-platform-" + Guid.NewGuid().ToString("N"));

        [TestCleanup]
        public void DeleteRoot()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        [TestMethod]
        public void A_platform_layout_module_gets_a_folder_per_part_in_order()
        {
            var drive = WriteDrive();
            var repository = WebRepository.Detect(drive, out var reason)!;
            Assert.IsNotNull(repository, reason);
            Assert.IsTrue(repository.IsPlatformLayout);
            Assert.AreEqual("kubuno-drive", repository.RunPackage);
            CollectionAssert.AreEquivalent(new[] { "kubuno-drive", "kubuno-drive-core" }, repository.Members.Select(member => member.PackageName).ToArray());
            CollectionAssert.AreEquivalent(
                new[] { "kubuno-drive-desktop-common", "kubuno-drive-desktop-core-storage", "kubuno-drive-desktop", "kubuno-drive-desktop-windows", "kubuno-drive-desktop-linux", "kubuno-drive-desktop-macos" },
                repository.DesktopMembers.Select(member => member.PackageName).ToArray(),
                "the vendored crate and the build output are never projects");

            var solutionPath = WebSolutionGenerator.DefaultSolutionPath(repository);
            Assert.AreEqual(Path.Combine(drive, "Kubuno.Drive.slnx"), solutionPath);
            var files = WebSolutionGenerator.Plan(new[] { repository }, solutionPath, WebSdkVersions.Current, null);
            var root = XDocument.Parse(files.Single(file => file.Mergeable).Content).Root!;
            CollectionAssert.AreEqual(
                new[] { "/Common/", "/Server/", "/Web/", "/Desktop/", "/Desktop/Common/", "/Desktop/Windows/", "/Desktop/Linux/", "/Desktop/macOS/", "/Mobile/" },
                FolderNames(root));

            CollectionAssert.AreEqual(new[] { "common/core/Kubuno.Drive.Core.rsproj" }, ProjectPaths(root, "/Common/"));
            CollectionAssert.AreEqual(new[] { "server/Kubuno.Drive.Server.rsproj" }, ProjectPaths(root, "/Server/"));
            CollectionAssert.AreEqual(new[] { "web/Kubuno.Drive.Web.esproj" }, ProjectPaths(root, "/Web/"));
            Assert.AreEqual(0, ProjectPaths(root, "/Desktop/").Length, "the Desktop folder only holds its OS folders");
            CollectionAssert.AreEquivalent(
                new[] { "desktop/common/kubuno-drive-desktop-common/Kubuno.Drive.Desktop.Common.rsproj", "desktop/common/kubuno-drive-desktop-core-storage/Kubuno.Drive.Desktop.Core.Storage.rsproj" },
                ProjectPaths(root, "/Desktop/Common/"));
            CollectionAssert.AreEquivalent(
                new[] { "desktop/windows/kubuno-drive-desktop/Kubuno.Drive.Desktop.rsproj", "desktop/windows/kubuno-drive-desktop-windows/Kubuno.Drive.Desktop.Windows.rsproj" },
                ProjectPaths(root, "/Desktop/Windows/"));
            CollectionAssert.AreEqual(new[] { "desktop/linux/kubuno-drive-desktop-linux/Kubuno.Drive.Desktop.Linux.rsproj" }, ProjectPaths(root, "/Desktop/Linux/"));
            CollectionAssert.AreEqual(new[] { "desktop/macos/kubuno-drive-desktop-macos/Kubuno.Drive.Desktop.Macos.rsproj" }, ProjectPaths(root, "/Desktop/macOS/"));

            // mobile/ holds only a README: shown as a solution item so the folder is visible.
            var mobile = Folder(root, "/Mobile/");
            Assert.AreEqual(0, mobile.Elements("Project").Count());
            CollectionAssert.AreEqual(new[] { "mobile/README.md" }, mobile.Elements("File").Select(file => (string)file.Attribute("Path")!).ToArray());

            // The server builds the web frontend first; the desktop projects depend on neither.
            var server = Folder(root, "/Server/").Elements("Project").Single();
            Assert.AreEqual(RsprojType, (string)server.Attribute("Type")!);
            CollectionAssert.AreEqual(new[] { "web/Kubuno.Drive.Web.esproj" }, server.Elements("BuildDependency").Select(dependency => (string)dependency.Attribute("Project")!).ToArray());
            Assert.IsFalse(root.Elements("Folder").Where(folder => ((string)folder.Attribute("Name")!).StartsWith("/Desktop/", StringComparison.Ordinal)).Descendants("BuildDependency").Any());

            // F5: server + web; the desktop profile starts the Windows program, the desktop startup project.
            var launch = files.Single(file => file.Path.EndsWith(".slnLaunch", StringComparison.Ordinal)).Content;
            StringAssert.Contains(launch, "\"Path\": \"server\\\\Kubuno.Drive.Server.rsproj\"");
            StringAssert.Contains(launch, "\"Path\": \"web\\\\Kubuno.Drive.Web.esproj\"");
            StringAssert.Contains(launch, "\"Name\": \"Drive desktop (Windows)\"");
            StringAssert.Contains(launch, "\"Path\": \"desktop\\\\windows\\\\kubuno-drive-desktop\\\\Kubuno.Drive.Desktop.rsproj\"");
            Assert.AreEqual(@"server\Kubuno.Drive.Server.rsproj", WebSolutionGenerator.StartupProject(new[] { repository }, solutionPath));
            Assert.AreEqual(Path.Combine(drive, "desktop", "windows", "kubuno-drive-desktop", "Kubuno.Drive.Desktop.rsproj"), WebSolutionGenerator.DesktopStartupProject(repository));

            // The generated projects: the server finds its module one folder up; one cargo run per desktop workspace.
            var serverProject = Content(files, "Kubuno.Drive.Server.rsproj");
            StringAssert.Contains(serverProject, "<KubunoWebRole>Module</KubunoWebRole>");
            StringAssert.Contains(serverProject, "<KubunoModuleDirectory>$([System.IO.Path]::GetFullPath('$(MSBuildThisFileDirectory)..'))</KubunoModuleDirectory>");
            Assert.IsFalse(serverProject.Contains("CargoBuildScope"), "the server workspace has one package");
            var core = Content(files, "Kubuno.Drive.Core.rsproj");
            StringAssert.Contains(core, "<CargoPackage>kubuno-drive-core</CargoPackage>");
            Assert.IsFalse(core.Contains("Kubuno.Web.Sdk"), "the shared rules are a plain library");
            var desktopCommon = Content(files, "Kubuno.Drive.Desktop.Common.rsproj");
            StringAssert.Contains(desktopCommon, "<CargoBuildScope>Workspace</CargoBuildScope>");
            StringAssert.Contains(Content(files, "Kubuno.Drive.Desktop.rsproj"), "<CargoBin>drive</CargoBin>");

            // Every path stays inside the repository.
            Assert.IsTrue(files.All(file => file.Path.StartsWith(drive + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)));
            Assert.IsFalse(root.Descendants().Select(element => (string?)element.Attribute("Path") ?? (string?)element.Attribute("Project") ?? string.Empty).Any(path => path.StartsWith("..", StringComparison.Ordinal)));

            var describe = WebSolutionGenerator.Describe(repository);
            CollectionAssert.Contains(describe.ToList(), "Common: Kubuno.Drive.Core.rsproj (kubuno-drive-core, library)");
            CollectionAssert.Contains(describe.ToList(), "Desktop/Windows: Kubuno.Drive.Desktop.rsproj (kubuno-drive-desktop, program)");
            CollectionAssert.Contains(describe.ToList(), "Mobile: mobile/README.md (no mobile project yet)");
        }

        [TestMethod]
        public void A_second_run_changes_nothing_and_a_new_crate_lands_in_its_os_folder()
        {
            var drive = WriteDrive();
            var repository = WebRepository.Detect(drive, out _)!;
            var solutionPath = WebSolutionGenerator.DefaultSolutionPath(repository);
            var first = WebSolutionGenerator.Plan(new[] { repository }, solutionPath, WebSdkVersions.Current, null).Single(file => file.Mergeable).Content;
            Assert.AreEqual(first, WebSolutionGenerator.Plan(new[] { repository }, solutionPath, WebSdkVersions.Current, first).Single(file => file.Mergeable).Content);

            // A crate added to desktop/linux later: merged into the existing /Desktop/Linux/ folder.
            Write(Path.Combine(drive, "desktop", "linux", "kubuno-drive-desktop-linux-tray", "Cargo.toml"), "[package]\nname = \"kubuno-drive-desktop-linux-tray\"\n");
            var merged = XDocument.Parse(WebSolutionGenerator.Plan(new[] { WebRepository.Detect(drive, out _)! }, solutionPath, WebSdkVersions.Current, first).Single(file => file.Mergeable).Content).Root!;
            CollectionAssert.AreEquivalent(
                new[] { "desktop/linux/kubuno-drive-desktop-linux/Kubuno.Drive.Desktop.Linux.rsproj", "desktop/linux/kubuno-drive-desktop-linux-tray/Kubuno.Drive.Desktop.Linux.Tray.rsproj" },
                ProjectPaths(merged, "/Desktop/Linux/"));
            Assert.AreEqual(1, merged.Descendants("File").Count(), "the README is not added twice");
        }

        [TestMethod]
        public void The_transition_mixes_moved_and_former_parts()
        {
            // The server and the desktop moved, the frontend not yet: the former frontend/ goes to Web, the per-OS desktop
            // workspace of the former layout (desktop/windows/crates/...) to Desktop/Windows.
            var drive = Path.Combine(_root, "drive");
            WriteModuleToml(drive);
            Write(Path.Combine(drive, "server", "Cargo.toml"), "[package]\nname = \"kubuno-drive\"\n\n[[bin]]\nname = \"kubuno-drive\"\npath = \"src/main.rs\"\n\n[workspace]\n");
            Write(Path.Combine(drive, "frontend", "package.json"), "{\"name\":\"drive-frontend\"}");
            Write(Path.Combine(drive, "desktop", "windows", "Cargo.toml"), "[workspace]\nmembers = [\"crates/*\"]\n");
            Write(Path.Combine(drive, "desktop", "windows", "crates", "kubuno-drive-desktop", "Cargo.toml"), "[package]\nname = \"kubuno-drive-desktop\"\n\n[[bin]]\nname = \"drive\"\npath = \"src/main.rs\"\n");
            Write(Path.Combine(drive, "desktop", "windows", "crates", "kubuno-drive-desktop-localization", "Cargo.toml"), "[package]\nname = \"kubuno-drive-desktop-localization\"\n");
            Write(Path.Combine(drive, "desktop", "windows", "suppa", "Cargo.toml"), "[package]\nname = \"suppaftp\"\n");

            var repository = WebRepository.Detect(drive, out var reason)!;
            Assert.IsNotNull(repository, reason);
            Assert.IsTrue(repository.IsPlatformLayout);
            var root = XDocument.Parse(WebSolutionGenerator.Plan(new[] { repository }, WebSolutionGenerator.DefaultSolutionPath(repository), WebSdkVersions.Current, null).Single(file => file.Mergeable).Content).Root!;
            CollectionAssert.AreEqual(new[] { "/Server/", "/Web/", "/Desktop/", "/Desktop/Windows/" }, FolderNames(root), "empty folders are omitted");
            CollectionAssert.AreEqual(new[] { "frontend/Kubuno.Drive.Web.esproj" }, ProjectPaths(root, "/Web/"));
            CollectionAssert.AreEqual(new[] { "frontend/Kubuno.Drive.Web.esproj" }, Folder(root, "/Server/").Descendants("BuildDependency").Select(dependency => (string)dependency.Attribute("Project")!).ToArray());
            CollectionAssert.AreEquivalent(
                new[] { "desktop/windows/crates/kubuno-drive-desktop/Kubuno.Drive.Desktop.rsproj", "desktop/windows/crates/kubuno-drive-desktop-localization/Kubuno.Drive.Desktop.Localization.rsproj" },
                ProjectPaths(root, "/Desktop/Windows/"));

            // The server not moved yet (root Cargo.toml) with the desktop already in desktop/: the root package is Server.
            var tasks = Path.Combine(_root, "tasks");
            Write(Path.Combine(tasks, "module.toml"), "[module]\nid = \"tasks\"\nversion = \"0.1.0\"\n\n[process]\nentrypoint = \"kubuno-tasks\"\n");
            Write(Path.Combine(tasks, "Cargo.toml"), "[package]\nname = \"kubuno-tasks\"\n\n[[bin]]\nname = \"kubuno-tasks\"\npath = \"src/main.rs\"\n\n[workspace]\n");
            Write(Path.Combine(tasks, "desktop", "Cargo.toml"), "[workspace]\nmembers = [\"windows/kubuno-tasks-desktop\"]\n");
            Write(Path.Combine(tasks, "desktop", "windows", "kubuno-tasks-desktop", "Cargo.toml"), "[package]\nname = \"kubuno-tasks-desktop\"\n\n[[bin]]\nname = \"tasks\"\npath = \"src/main.rs\"\n");
            var mixed = WebRepository.Detect(tasks, out _)!;
            Assert.IsTrue(mixed.IsPlatformLayout);
            var mixedRoot = XDocument.Parse(WebSolutionGenerator.Plan(new[] { mixed }, WebSolutionGenerator.DefaultSolutionPath(mixed), WebSdkVersions.Current, null).Single(file => file.Mergeable).Content).Root!;
            CollectionAssert.AreEqual(new[] { "/Server/", "/Desktop/", "/Desktop/Windows/" }, FolderNames(mixedRoot));
            CollectionAssert.AreEqual(new[] { "Kubuno.Tasks.Server.rsproj" }, ProjectPaths(mixedRoot, "/Server/"));
        }

        [TestMethod]
        public void The_former_layout_keeps_its_folders()
        {
            // Root Cargo.toml + frontend/ + desktop/windows/crates: Server, Frontend, Desktop as before (0f81b62).
            var drive = Path.Combine(_root, "drive");
            WriteModuleToml(drive);
            Write(Path.Combine(drive, "Cargo.toml"), "[package]\nname = \"kubuno-drive\"\n\n[[bin]]\nname = \"kubuno-drive\"\npath = \"src/main.rs\"\n\n[workspace]\n");
            Write(Path.Combine(drive, "frontend", "package.json"), "{\"name\":\"drive-frontend\"}");
            Write(Path.Combine(drive, "desktop", "windows", "Cargo.toml"), "[workspace]\nmembers = [\"crates/kubuno-drive-desktop\"]\n");
            Write(Path.Combine(drive, "desktop", "windows", "crates", "kubuno-drive-desktop", "Cargo.toml"), "[package]\nname = \"kubuno-drive-desktop\"\n\n[[bin]]\nname = \"drive\"\npath = \"src/main.rs\"\n");
            Write(Path.Combine(drive, "mobile", "README.md"), "# Mobile\n");

            var repository = WebRepository.Detect(drive, out _)!;
            Assert.IsFalse(repository.IsPlatformLayout);
            Assert.IsNull(repository.MobileReadme, "the former layout has no Mobile folder");
            var files = WebSolutionGenerator.Plan(new[] { repository }, WebSolutionGenerator.DefaultSolutionPath(repository), WebSdkVersions.Current, null);
            var solution = files.Single(file => file.Mergeable).Content;
            Assert.AreEqual(
                "<Solution>\n"
                + "  <Configurations>\n    <Platform Name=\"x64\" />\n  </Configurations>\n"
                + "  <Folder Name=\"/Server/\">\n    <Project Path=\"Kubuno.Drive.Server.rsproj\" Type=\"" + RsprojType + "\">\n      <BuildDependency Project=\"frontend/Kubuno.Drive.Web.esproj\" />\n    </Project>\n  </Folder>\n"
                + "  <Folder Name=\"/Frontend/\">\n    <Project Path=\"frontend/Kubuno.Drive.Web.esproj\">\n      <Platform Project=\"AnyCPU\" />\n      <Build />\n    </Project>\n  </Folder>\n"
                + "  <Folder Name=\"/Desktop/\">\n    <Project Path=\"desktop/windows/crates/kubuno-drive-desktop/Kubuno.Drive.Desktop.rsproj\" Type=\"" + RsprojType + "\" />\n  </Folder>\n"
                + "</Solution>\n",
                solution);
            var launch = files.Single(file => file.Path.EndsWith(".slnLaunch", StringComparison.Ordinal)).Content;
            Assert.IsFalse(launch.Contains("desktop"), "no desktop profile in the former layout");
            Assert.IsNull(WebSolutionGenerator.DesktopStartupProject(repository));
        }

        [TestMethod]
        public void Mobile_projects_get_their_platform_folders_and_no_readme()
        {
            var drive = WriteDrive();
            Write(Path.Combine(drive, "mobile", "common", "kubuno-drive-mobile-core", "Cargo.toml"), "[package]\nname = \"kubuno-drive-mobile-core\"\n");
            Write(Path.Combine(drive, "mobile", "android", "kubuno-drive-android", "Cargo.toml"), "[package]\nname = \"kubuno-drive-android\"\n");
            var repository = WebRepository.Detect(drive, out _)!;
            Assert.IsNull(repository.MobileReadme);
            var root = XDocument.Parse(WebSolutionGenerator.Plan(new[] { repository }, WebSolutionGenerator.DefaultSolutionPath(repository), WebSdkVersions.Current, null).Single(file => file.Mergeable).Content).Root!;
            CollectionAssert.AreEqual(
                new[] { "/Common/", "/Server/", "/Web/", "/Desktop/", "/Desktop/Common/", "/Desktop/Windows/", "/Desktop/Linux/", "/Desktop/macOS/", "/Mobile/", "/Mobile/Common/", "/Mobile/Android/" },
                FolderNames(root));
            CollectionAssert.AreEqual(new[] { "mobile/common/kubuno-drive-mobile-core/Kubuno.Drive.Mobile.Core.rsproj" }, ProjectPaths(root, "/Mobile/Common/"));
            Assert.AreEqual(0, root.Descendants("File").Count());
        }

        [TestMethod]
        public void Nested_folders_keep_their_order_in_a_multi_repository_solution()
        {
            var drive = WriteDrive();
            var calendar = Path.Combine(_root, "calendar");
            Write(Path.Combine(calendar, "Cargo.toml"), "[package]\nname = \"kubuno-calendar\"\n\n[[bin]]\nname = \"kubuno-calendar\"\npath = \"src/main.rs\"\n\n[workspace]\n");
            Write(Path.Combine(calendar, "module.toml"), "[module]\nid = \"calendar\"\nversion = \"0.1.0\"\n\n[process]\nentrypoint = \"kubuno-calendar\"\n");
            Write(Path.Combine(calendar, "frontend", "package.json"), "{}");
            var repositories = new[] { WebRepository.Detect(calendar, out _)!, WebRepository.Detect(drive, out _)! };
            var root = XDocument.Parse(WebSolutionGenerator.Plan(repositories, Path.Combine(_root, "Kubuno.Web.slnx"), WebSdkVersions.Current, null).Single(file => file.Mergeable).Content).Root!;
            CollectionAssert.AreEqual(
                new[]
                {
                    "/calendar/", "/calendar/Server/", "/calendar/Frontend/",
                    "/drive/", "/drive/Common/", "/drive/Server/", "/drive/Web/", "/drive/Desktop/", "/drive/Desktop/Common/", "/drive/Desktop/Windows/", "/drive/Desktop/Linux/", "/drive/Desktop/macOS/", "/drive/Mobile/",
                },
                FolderNames(root));
            CollectionAssert.AreEqual(new[] { "drive/mobile/README.md" }, Folder(root, "/drive/Mobile/").Elements("File").Select(file => (string)file.Attribute("Path")!).ToArray());
        }

        [TestMethod]
        public void Platform_folders_and_ranks_follow_the_paths()
        {
            var root = Path.Combine(_root, "m");
            Assert.AreEqual("Common", PlatformLayout.FolderOf(root, Path.Combine(root, "common", "core")));
            Assert.AreEqual("Server", PlatformLayout.FolderOf(root, Path.Combine(root, "server")));
            Assert.AreEqual("Server", PlatformLayout.FolderOf(root, root));
            Assert.AreEqual("Web", PlatformLayout.FolderOf(root, Path.Combine(root, "web")));
            Assert.AreEqual("Desktop/macOS", PlatformLayout.FolderOf(root, Path.Combine(root, "desktop", "macos", "x")));
            Assert.AreEqual("Desktop", PlatformLayout.FolderOf(root, Path.Combine(root, "desktop", "tools", "x")));
            Assert.AreEqual("Mobile/iOS", PlatformLayout.FolderOf(root, Path.Combine(root, "mobile", "ios", "x")));
            Assert.IsTrue(PlatformLayout.Rank("/Desktop/") < PlatformLayout.Rank("/Desktop/Common/"));
            Assert.IsTrue(PlatformLayout.Rank("Desktop/macOS") < PlatformLayout.Rank("Mobile"));
            Assert.IsFalse(PlatformLayout.IsListed(root, Path.Combine(root, "desktop", "common", "vendor", "suppaftp-10.0.0")));
            Assert.IsFalse(PlatformLayout.IsListed(root, Path.Combine(root, "server", "target", "x")));
            Assert.IsFalse(PlatformLayout.IsListed(root, Path.Combine(_root, "other")), "never a path outside the repository");
            Assert.IsTrue(PlatformLayout.IsListed(root, Path.Combine(root, "desktop", "windows", "kubuno-drive-desktop")));
        }

        [TestMethod]
        public void The_module_files_are_found_in_both_layouts_and_deployed_the_same()
        {
            var drive = WriteDrive();
            var server = Path.Combine(drive, "server");
            Write(Path.Combine(server, "migrations", "000001_drive_schema.up.sql"), "select 1;");
            Write(Path.Combine(server, "config.toml.example"), "port = 3101\n");
            Write(Path.Combine(drive, "LICENSE"), "AGPL");
            Write(Path.Combine(drive, "web", "dist", "entry.js"), "export {}");
            var executable = Path.Combine(_root, "target", "kubuno-drive.exe");
            Write(executable, "MZ");

            Assert.AreEqual(drive, ModuleLayout.ModuleDirectory(server), "server/ finds module.toml one folder up");
            Assert.AreEqual(drive, ModuleLayout.ModuleDirectory(drive));
            Assert.AreEqual(Path.Combine(drive, "web"), ModuleLayout.FrontendDirectory(drive));
            Assert.AreEqual(server, ModuleLayout.ServerDirectory(drive));

            var manifest = ModuleManifest.Load(Path.Combine(drive, "module.toml"));
            var target = Path.Combine(_root, "dev-core", "modules", "drive");
            var copies = ModuleDeployment.Plan(drive, executable, target, manifest);
            CollectionAssert.IsSubsetOf(
                new[] { Path.Combine(target, "migrations", "000001_drive_schema.up.sql"), Path.Combine(target, "frontend", "entry.js"), Path.Combine(target, "module.toml") },
                copies.Select(copy => copy.Target).ToList());

            var entries = KbpkgPackager.PlanEntries(drive, executable, manifest).Select(entry => entry.Key).ToList();
            CollectionAssert.IsSubsetOf(new[] { "module.toml", "frontend/entry.js", "migrations/000001_drive_schema.up.sql", "config.toml.example", "LICENSE" }, entries);
            Assert.AreEqual(Path.Combine(server, "config.toml.example"), ModuleLayout.RootFile(drive, "config.toml.example"));

            // The former layout: everything at the root.
            var calendar = Path.Combine(_root, "calendar");
            Write(Path.Combine(calendar, "module.toml"), "[module]\nid = \"calendar\"\n");
            Write(Path.Combine(calendar, "migrations", "1.sql"), "select 1;");
            Write(Path.Combine(calendar, "frontend", "package.json"), "{}");
            Assert.AreEqual(calendar, ModuleLayout.ModuleDirectory(calendar));
            Assert.AreEqual(Path.Combine(calendar, "frontend"), ModuleLayout.FrontendDirectory(calendar));
            Assert.AreEqual(Path.Combine(calendar, "migrations"), ModuleLayout.MigrationsDirectory(calendar));
            Assert.AreEqual(calendar, ModuleLayout.ServerDirectory(calendar));
        }

        [TestMethod]
        public void The_web_sdk_finds_the_module_folder_and_frontend_of_both_layouts()
        {
            // Sdk.targets evaluated by MSBuild itself: KubunoModuleDirectory is the manifest's folder, or its parent when that
            // one holds module.toml (server/); the frontend project is web\*.esproj when web\package.json exists, else
            // frontend\*.esproj.
            var drive = WriteDrive();
            Write(Path.Combine(drive, "web", "Kubuno.Drive.Web.esproj"), "<Project />");
            var platform = Evaluate(Path.Combine(drive, "server"));
            Assert.AreEqual(drive, platform["KubunoModuleDirectory"]);
            Assert.AreEqual(Path.Combine(drive, "web", "Kubuno.Drive.Web.esproj"), platform["_KubunoFrontendProject"]);
            Assert.AreEqual(Path.Combine(drive, "dist"), platform["KubunoKbpkgOutputDirectory"]);

            var calendar = Path.Combine(_root, "calendar");
            Write(Path.Combine(calendar, "module.toml"), "[module]\nid = \"calendar\"\n");
            Write(Path.Combine(calendar, "Cargo.toml"), "[package]\nname = \"kubuno-calendar\"\n");
            Write(Path.Combine(calendar, "frontend", "package.json"), "{}");
            Write(Path.Combine(calendar, "frontend", "Kubuno.Calendar.Web.esproj"), "<Project />");
            var former = Evaluate(calendar);
            Assert.AreEqual(calendar, former["KubunoModuleDirectory"]);
            Assert.AreEqual(Path.Combine(calendar, "frontend", "Kubuno.Calendar.Web.esproj"), former["_KubunoFrontendProject"]);
        }

        /// <summary>
        /// Evaluates Kubuno.Web.Sdk's Sdk.targets for a module backend whose Cargo manifest is in <paramref name="crateDirectory"/>
        /// (<c>dotnet msbuild -getProperty/-getItem</c>, no build).
        /// </summary>
        private Dictionary<string, string> Evaluate(string crateDirectory)
        {
            var targets = Path.Combine(RepositoryRoot(), "sdk", "Kubuno.Web.Sdk", "Sdk", "Sdk.targets");
            var project = Path.Combine(crateDirectory, "probe.proj");
            Write(project, "<Project>\n  <PropertyGroup>\n    <KubunoWebRole>Module</KubunoWebRole>\n    <CargoManifestPath>$(MSBuildProjectDirectory)\\Cargo.toml</CargoManifestPath>\n    <_KubunoWebTasksAssembly>none.dll</_KubunoWebTasksAssembly>\n  </PropertyGroup>\n  <Import Project=\"" + targets + "\" />\n</Project>\n");
            var start = new System.Diagnostics.ProcessStartInfo("dotnet", "msbuild \"" + project + "\" -nologo -getProperty:KubunoModuleDirectory -getProperty:KubunoKbpkgOutputDirectory -getItem:_KubunoFrontendProject")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using var process = System.Diagnostics.Process.Start(start)!;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            Assert.AreEqual(0, process.ExitCode, output);
            using var json = System.Text.Json.JsonDocument.Parse(output);
            var properties = json.RootElement.GetProperty("Properties");
            var items = json.RootElement.GetProperty("Items").GetProperty("_KubunoFrontendProject");
            return new Dictionary<string, string>
            {
                ["KubunoModuleDirectory"] = properties.GetProperty("KubunoModuleDirectory").GetString()!,
                ["KubunoKbpkgOutputDirectory"] = properties.GetProperty("KubunoKbpkgOutputDirectory").GetString()!,
                ["_KubunoFrontendProject"] = string.Join(";", items.EnumerateArray().Select(item => item.GetProperty("FullPath").GetString())),
            };
        }

        /// <summary>
        /// The headless generator (docs/WEB.md, "Solutions"): runs "Kubuno Web: Generate Solution" on the repository named
        /// by <c>KUBUNO_GEN_REPO</c> - a dry run printing the plan, or with <c>KUBUNO_GEN_WRITE=1</c> writing the files like
        /// the command (<c>KUBUNO_GEN_FRESH=1</c>: the solution and its .slnLaunch planned fresh, replacing the existing
        /// ones). Inconclusive (skipped) without the variable.
        /// </summary>
        [TestMethod]
        [TestCategory("Headless")]
        public void Generate_the_repository_named_by_KUBUNO_GEN_REPO()
        {
            var directory = Environment.GetEnvironmentVariable("KUBUNO_GEN_REPO");
            if (string.IsNullOrWhiteSpace(directory))
            {
                Assert.Inconclusive("Set KUBUNO_GEN_REPO to a Kubuno repository to run the generator on it.");
                return;
            }

            var repository = WebRepository.Detect(directory!, out var reason);
            Assert.IsNotNull(repository, reason);
            var existing = Directory.GetFiles(repository!.Root, "Kubuno.*.slnx");
            var solutionPath = existing.Length == 1 ? existing[0] : WebSolutionGenerator.DefaultSolutionPath(repository);
            var fresh = Environment.GetEnvironmentVariable("KUBUNO_GEN_FRESH") == "1";
            var existingContent = !fresh && File.Exists(solutionPath) ? File.ReadAllText(solutionPath) : null;
            var files = WebSolutionGenerator.Plan(new[] { repository }, solutionPath, WebSdkVersions.Current, existingContent);

            var report = new List<string> { "Kubuno web: " + solutionPath + (repository.IsPlatformLayout ? " (platform layout)" : " (former layout)") };
            report.AddRange(WebSolutionGenerator.Describe(repository).Select(line => "  " + line));
            report.AddRange(ModuleIsolation.Violations(repository).Select(violation => "  WARNING module isolation: " + violation));
            if (Environment.GetEnvironmentVariable("KUBUNO_GEN_WRITE") == "1")
            {
                report.AddRange(WebSolutionGenerator.Apply(files, replaceSolutionFiles: fresh));
            }
            else
            {
                report.Add("Dry run (set KUBUNO_GEN_WRITE=1 to write):");
                foreach (var file in files)
                {
                    report.Add("  " + (File.Exists(file.Path) ? (file.Mergeable || (fresh && file.Path.EndsWith(".slnLaunch", StringComparison.OrdinalIgnoreCase)) ? "would update " : "would keep   ") : "would create ") + file.Path);
                }

                report.Add(files.Single(file => file.Mergeable).Content);
                report.AddRange(files.Where(file => file.Path.EndsWith(".slnLaunch", StringComparison.OrdinalIgnoreCase)).Select(file => file.Content));
            }

            var text = string.Join(Environment.NewLine, report);
            Console.WriteLine(text);
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "kubuno-gen-report.txt"), text);
        }

        private string WriteDrive()
        {
            var drive = Path.Combine(_root, "drive");
            WriteModuleToml(drive);
            Write(Path.Combine(drive, "common", "core", "Cargo.toml"), "[package]\nname = \"kubuno-drive-core\"\nversion = \"0.1.0\"\n");
            Write(Path.Combine(drive, "common", "vectors", "listing.json"), "{}");
            Write(Path.Combine(drive, "server", "Cargo.toml"), "[package]\nname = \"kubuno-drive\"\nversion = \"0.1.8\"\n\n[[bin]]\nname = \"kubuno-drive\"\npath = \"src/main.rs\"\n\n[dependencies]\nkubuno-drive-core = { path = \"../common/core\" }\n\n[workspace]\n");
            Write(Path.Combine(drive, "server", "target", "debug", "build", "x", "Cargo.toml"), "[package]\nname = \"build-output\"\n");
            Write(Path.Combine(drive, "web", "package.json"), "{\"name\":\"drive-frontend\",\"version\":\"0.1.8\"}");
            Write(Path.Combine(drive, "desktop", "Cargo.toml"), "[workspace]\nresolver = \"2\"\nmembers = [\n  \"common/*\",\n  \"common/vendor/suppaftp-10.0.0\",\n  \"windows/*\",\n  \"linux/*\",\n  \"macos/*\",\n]\n");
            Write(Path.Combine(drive, "desktop", "common", "kubuno-drive-desktop-common", "Cargo.toml"), "[package]\nname = \"kubuno-drive-desktop-common\"\n");
            Write(Path.Combine(drive, "desktop", "common", "kubuno-drive-desktop-core-storage", "Cargo.toml"), "[package]\nname = \"kubuno-drive-desktop-core-storage\"\n");
            Write(Path.Combine(drive, "desktop", "common", "vendor", "suppaftp-10.0.0", "Cargo.toml"), "[package]\nname = \"suppaftp\"\n");
            Write(Path.Combine(drive, "desktop", "windows", "kubuno-drive-desktop", "Cargo.toml"), "[package]\nname = \"kubuno-drive-desktop\"\n\n[[bin]]\nname = \"drive\"\npath = \"src/main.rs\"\n");
            Write(Path.Combine(drive, "desktop", "windows", "kubuno-drive-desktop-windows", "Cargo.toml"), "[package]\nname = \"kubuno-drive-desktop-windows\"\n");
            Write(Path.Combine(drive, "desktop", "linux", "kubuno-drive-desktop-linux", "Cargo.toml"), "[package]\nname = \"kubuno-drive-desktop-linux\"\n\n[[bin]]\nname = \"drive\"\npath = \"src/main.rs\"\n");
            Write(Path.Combine(drive, "desktop", "macos", "kubuno-drive-desktop-macos", "Cargo.toml"), "[package]\nname = \"kubuno-drive-desktop-macos\"\n\n[[bin]]\nname = \"drive\"\npath = \"src/main.rs\"\n");
            Write(Path.Combine(drive, "mobile", "README.md"), "# Drive mobile\n");
            return drive;
        }

        private static void WriteModuleToml(string module) =>
            Write(Path.Combine(module, "module.toml"), "[module]\nid = \"drive\"\nversion = \"0.1.8\"\n\n[process]\nentrypoint = \"kubuno-drive\"\n\n[[sidebar_items]]\npath = \"/drive\"\n");

        private static string[] FolderNames(XElement root) => root.Elements("Folder").Select(folder => (string)folder.Attribute("Name")!).ToArray();

        private static XElement Folder(XElement root, string name) => root.Elements("Folder").Single(folder => (string)folder.Attribute("Name")! == name);

        private static string[] ProjectPaths(XElement root, string folder) => Folder(root, folder).Elements("Project").Select(project => (string)project.Attribute("Path")!).ToArray();

        private static string Content(IReadOnlyList<GeneratedFile> files, string fileName) =>
            files.Single(file => string.Equals(Path.GetFileName(file.Path), fileName, StringComparison.Ordinal)).Content;

        /// <summary>The checkout this file was compiled from (the build output may live elsewhere).</summary>
        private static string RepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string thisFile = "") =>
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

        private static void Write(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }
    }
}
