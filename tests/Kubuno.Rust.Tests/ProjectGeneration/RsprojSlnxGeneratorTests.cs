using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Kubuno.Rust.Logic.ProjectGeneration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Rust.Tests.ProjectGeneration
{
    [TestClass]
    public class RsprojSlnxGeneratorTests
    {
        private const string Root = @"C:\ws";

        private static RsprojProjectPlanItem Item(string name, bool libraryOnly = false, string? folder = null) =>
            new(name, Path.Combine(Root, folder ?? name, name + ".rsproj"), Path.Combine(Root, folder ?? name, "Cargo.toml"), RsprojPlanAction.Create, content: "<Project />", isLibraryOnly: libraryOnly);

        private static List<string> ProjectPaths(string content) =>
            XDocument.Parse(content).Descendants("Project").Select(project => (string)project.Attribute("Path")!).ToList();

        [TestMethod]
        public void Plan_BuildsAFreshSolution_WithAppsAtTheRootAndLibrariesInAFolder()
        {
            var plan = RsprojSlnxGenerator.Plan(Root, existingContent: null, new List<RsprojProjectPlanItem>
            {
                Item("app", folder: @"src\app"),
                Item("ui", libraryOnly: true, folder: @"crates\ui"),
            });

            Assert.IsTrue(plan.Changed);
            var document = XDocument.Parse(plan.Content);
            Assert.AreEqual("x64", (string)document.Root!.Element("Configurations")!.Element("Platform")!.Attribute("Name")!);
            var app = document.Root.Elements("Project").Single();
            Assert.AreEqual("src/app/app.rsproj", (string)app.Attribute("Path")!);
            Assert.AreEqual("6c7c4cb5-6e36-4c6f-9c6f-9c6e9b4d4c13", (string)app.Attribute("Type")!);
            var folder = document.Root.Element("Folder")!;
            Assert.AreEqual(RsprojSlnxGenerator.LibrariesFolderName, (string)folder.Attribute("Name")!);
            Assert.AreEqual("crates/ui/ui.rsproj", (string)folder.Element("Project")!.Attribute("Path")!);
        }

        [TestMethod]
        public void Plan_FreshSolution_HasNoLibrariesFolder_WithoutLibraries()
        {
            var plan = RsprojSlnxGenerator.Plan(Root, existingContent: null, new List<RsprojProjectPlanItem> { Item("app") });

            Assert.IsNull(XDocument.Parse(plan.Content).Root!.Element("Folder"));
        }

        [TestMethod]
        public void Plan_LeavesAnUpToDateSolutionByteForByte()
        {
            var existing = "<Solution>\r\n  <!-- mine -->\r\n  <Configurations>\r\n    <Platform Name=\"x64\" />\r\n  </Configurations>\r\n  <Project Path=\"app/app.rsproj\" Type=\"6c7c4cb5-6e36-4c6f-9c6f-9c6e9b4d4c13\" />\r\n</Solution>\r\n";

            var plan = RsprojSlnxGenerator.Plan(Root, existing, new List<RsprojProjectPlanItem> { Item("app") });

            Assert.IsFalse(plan.Changed);
            Assert.AreEqual(existing, plan.Content);
        }

        [TestMethod]
        public void Plan_MergesOnlyTheMissingProjects_KeepingEverythingElse()
        {
            var existing = "<Solution>\r\n  <!-- mine -->\r\n  <Configurations>\r\n    <Platform Name=\"x64\" />\r\n  </Configurations>\r\n  <Project Path=\"app/app.rsproj\" Type=\"6c7c4cb5-6e36-4c6f-9c6f-9c6e9b4d4c13\" />\r\n  <Folder Name=\"/Docs/\">\r\n    <File Path=\"README.md\" />\r\n  </Folder>\r\n</Solution>\r\n";

            var plan = RsprojSlnxGenerator.Plan(Root, existing, new List<RsprojProjectPlanItem>
            {
                Item("app"),
                Item("tool"),
                Item("ui", libraryOnly: true),
            });

            Assert.IsTrue(plan.Changed);
            CollectionAssert.AreEquivalent(new[] { "tool", "ui" }, plan.AddedProjectNames.ToList());
            StringAssert.Contains(plan.Content, "<!-- mine -->");
            StringAssert.Contains(plan.Content, "<File Path=\"README.md\" />");
            StringAssert.Contains(plan.Content, "  <Project Path=\"tool/tool.rsproj\" Type=\"6c7c4cb5-6e36-4c6f-9c6f-9c6e9b4d4c13\" />\r\n");
            Assert.IsFalse(plan.Content.Replace("\r\n", string.Empty).Contains("\n"), "The file's CRLF line endings are kept.");
            CollectionAssert.AreEqual(new[] { "app/app.rsproj", "tool/tool.rsproj", "ui/ui.rsproj" }, ProjectPaths(plan.Content));
            var libraries = XDocument.Parse(plan.Content).Root!.Elements("Folder").Single(folder => (string)folder.Attribute("Name")! == RsprojSlnxGenerator.LibrariesFolderName);
            Assert.AreEqual("ui/ui.rsproj", (string)libraries.Element("Project")!.Attribute("Path")!);
        }

        [TestMethod]
        public void Plan_AddsTheX64Platform_KeepingAnyCpuForOtherProjects()
        {
            var existing = "<Solution>\n  <Project Path=\"host/host.csproj\" />\n</Solution>\n";

            var plan = RsprojSlnxGenerator.Plan(Root, existing, new List<RsprojProjectPlanItem> { Item("app") });

            var platforms = XDocument.Parse(plan.Content).Root!.Element("Configurations")!.Elements("Platform").Select(platform => (string)platform.Attribute("Name")!).ToList();
            CollectionAssert.AreEqual(new[] { "Any CPU", "x64" }, platforms);
            CollectionAssert.AreEqual(new[] { "host/host.csproj", "app/app.rsproj" }, ProjectPaths(plan.Content));
        }

        [TestMethod]
        public void Plan_FillsAnEmptySolution()
        {
            var plan = RsprojSlnxGenerator.Plan(Root, "<Solution />", new List<RsprojProjectPlanItem> { Item("app") });

            Assert.IsTrue(plan.Changed);
            CollectionAssert.AreEqual(new[] { "app/app.rsproj" }, ProjectPaths(plan.Content));
            Assert.AreEqual("x64", (string)XDocument.Parse(plan.Content).Root!.Element("Configurations")!.Element("Platform")!.Attribute("Name")!);
        }

        [TestMethod]
        public void Plan_LeavesAFileItCannotRead_Untouched()
        {
            var plan = RsprojSlnxGenerator.Plan(Root, "not xml <", new List<RsprojProjectPlanItem> { Item("app") });

            Assert.IsFalse(plan.Changed);
            Assert.AreEqual("not xml <", plan.Content);
        }
    }
}
