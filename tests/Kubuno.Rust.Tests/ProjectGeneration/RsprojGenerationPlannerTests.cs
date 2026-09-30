using System.Collections.Generic;
using System.IO;
using Kubuno.Rust.Cargo.Metadata;
using Kubuno.Rust.Logic.ProjectGeneration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Rust.Tests.ProjectGeneration
{
    [TestClass]
    public class RsprojGenerationPlannerTests
    {
        private const string Root = @"C:\ws";

        private static CargoTarget Bin(string name) => new() { Name = name, Kind = new[] { CargoTargetKind.Bin } };
        private static CargoTarget Lib(string name) => new() { Name = name, Kind = new[] { CargoTargetKind.Lib } };

        private static CargoPackage Package(string name, string manifestDir, IReadOnlyList<CargoTarget> targets, string? defaultRun = null) => new()
        {
            Id = $"path+file:///{manifestDir}#{name}#0.1.0",
            Name = name,
            ManifestPath = Path.Combine(manifestDir, "Cargo.toml"),
            Targets = targets,
            DefaultRun = defaultRun,
        };

        private static CargoMetadata Metadata(params CargoPackage[] packages)
        {
            var ids = new List<string>();
            foreach (var package in packages)
            {
                ids.Add(package.Id);
            }

            return new CargoMetadata { Packages = packages, WorkspaceMembers = ids, WorkspaceRoot = Root };
        }

        private static RsprojGenerationOptions Options(bool includeLibraryOnly = false) => new("1.0.0", includeLibraryOnly);

        [TestMethod]
        public void Plan_CreatesOneProjectPerMemberWithABin()
        {
            var app = Package("app", Path.Combine(Root, "app"), new[] { Bin("app") });
            var metadata = Metadata(app);

            var plan = RsprojGenerationPlanner.Plan(metadata, Options(), _ => false);

            Assert.AreEqual(1, plan.Count);
            Assert.AreEqual("app", plan[0].PackageName);
            Assert.AreEqual(RsprojPlanAction.Create, plan[0].Action);
            Assert.AreEqual(Path.Combine(Root, "app", "app.rsproj"), plan[0].ProjectPath);
            StringAssert.Contains(plan[0].Content, "<CargoPackage>app</CargoPackage>");
            StringAssert.DoesNotMatch(plan[0].Content, new System.Text.RegularExpressions.Regex("CargoBin"));
        }

        [TestMethod]
        public void Plan_SkipsAMemberWhoseProjectFileAlreadyExists_ButStillReturnsItAsSkipped()
        {
            var app = Package("app", Path.Combine(Root, "app"), new[] { Bin("app") });
            var metadata = Metadata(app);
            var expectedPath = Path.Combine(Root, "app", "app.rsproj");

            var plan = RsprojGenerationPlanner.Plan(metadata, Options(), path => path == expectedPath);

            Assert.AreEqual(1, plan.Count);
            Assert.AreEqual(RsprojPlanAction.SkipExisting, plan[0].Action);
            // Content is still populated (for preview/diagnostics) even though it must not be written.
            StringAssert.Contains(plan[0].Content, "<CargoPackage>app</CargoPackage>");
        }

        [TestMethod]
        public void Plan_ExcludesLibraryOnlyMembers_ByDefault()
        {
            var lib = Package("mylib", Path.Combine(Root, "mylib"), new[] { Lib("mylib") });
            var metadata = Metadata(lib);

            var plan = RsprojGenerationPlanner.Plan(metadata, Options(includeLibraryOnly: false), _ => false);

            Assert.AreEqual(0, plan.Count);
        }

        [TestMethod]
        public void Plan_IncludesLibraryOnlyMembers_WhenOptedIn()
        {
            var lib = Package("mylib", Path.Combine(Root, "mylib"), new[] { Lib("mylib") });
            var metadata = Metadata(lib);

            var plan = RsprojGenerationPlanner.Plan(metadata, Options(includeLibraryOnly: true), _ => false);

            Assert.AreEqual(1, plan.Count);
            Assert.IsTrue(plan[0].IsLibraryOnly);
        }

        [TestMethod]
        public void Plan_ExcludesPackagesNotListedAsWorkspaceMembers()
        {
            var app = Package("app", Path.Combine(Root, "app"), new[] { Bin("app") });
            var external = Package("external-dep", Path.Combine(Root, "external-dep"), new[] { Bin("external-dep") });
            var metadata = new CargoMetadata
            {
                Packages = new[] { app, external },
                WorkspaceMembers = new[] { app.Id }, // "external-dep" deliberately left out
                WorkspaceRoot = Root,
            };

            var plan = RsprojGenerationPlanner.Plan(metadata, Options(), _ => false);

            Assert.AreEqual(1, plan.Count);
            Assert.AreEqual("app", plan[0].PackageName);
        }

        [TestMethod]
        public void Plan_LeavesCargoBinUnset_ForASingleBinNamedAfterThePackage()
        {
            var app = Package("app", Path.Combine(Root, "app"), new[] { Bin("app") });
            var plan = RsprojGenerationPlanner.Plan(Metadata(app), Options(), _ => false);

            StringAssert.DoesNotMatch(plan[0].Content, new System.Text.RegularExpressions.Regex("CargoBin"));
        }

        [TestMethod]
        public void Plan_SetsCargoBin_ForASingleBinNotNamedAfterThePackage()
        {
            // drive-app's only bin is "drive": the SDK's default (the package name) would look for drive-app.exe.
            var app = Package("drive-app", Path.Combine(Root, "drive-app"), new[] { Bin("drive") });
            var plan = RsprojGenerationPlanner.Plan(Metadata(app), Options(), _ => false);

            StringAssert.Contains(plan[0].Content, "<CargoBin>drive</CargoBin>");
        }

        [TestMethod]
        public void Plan_BuildsTheWholeWorkspace_WhenAMemberIsADylib()
        {
            var ui = Package("ui", Path.Combine(Root, "ui"), new[] { new CargoTarget { Name = "ui", Kind = new[] { CargoTargetKind.Dylib }, CrateTypes = new[] { CargoTargetKind.Dylib } } });
            var app = Package("app", Path.Combine(Root, "app"), new[] { Bin("app") });
            var plan = RsprojGenerationPlanner.Plan(Metadata(ui, app), Options(includeLibraryOnly: true), _ => false);

            Assert.AreEqual(2, plan.Count);
            foreach (var item in plan)
            {
                StringAssert.Contains(item.Content, "<CargoBuildScope>Workspace</CargoBuildScope>");
            }
        }

        [TestMethod]
        public void Plan_BuildsEachPackage_WhenNoMemberIsADylib()
        {
            var core = Package("core", Path.Combine(Root, "core"), new[] { Lib("core") });
            var app = Package("app", Path.Combine(Root, "app"), new[] { Bin("app") });
            var plan = RsprojGenerationPlanner.Plan(Metadata(core, app), Options(includeLibraryOnly: true), _ => false);

            foreach (var item in plan)
            {
                StringAssert.DoesNotMatch(item.Content, new System.Text.RegularExpressions.Regex("CargoBuildScope"));
            }
        }

        [TestMethod]
        public void Plan_SetsCargoBin_ToDefaultRun_WhenAPackageHasMultipleBins()
        {
            var app = Package("app", Path.Combine(Root, "app"), new[] { Bin("app"), Bin("tool") }, defaultRun: "tool");
            var plan = RsprojGenerationPlanner.Plan(Metadata(app), Options(), _ => false);

            StringAssert.Contains(plan[0].Content, "<CargoBin>tool</CargoBin>");
        }

        [TestMethod]
        public void Plan_SetsCargoBin_ToTheBinNamedAfterThePackage_WithNoDefaultRun()
        {
            var app = Package("app", Path.Combine(Root, "app"), new[] { Bin("helper"), Bin("app") });
            var plan = RsprojGenerationPlanner.Plan(Metadata(app), Options(), _ => false);

            StringAssert.Contains(plan[0].Content, "<CargoBin>app</CargoBin>");
        }

        [TestMethod]
        public void Plan_SetsCargoBin_ToTheFirstBin_AsALastResort()
        {
            var app = Package("app", Path.Combine(Root, "app"), new[] { Bin("alpha"), Bin("beta") });
            var plan = RsprojGenerationPlanner.Plan(Metadata(app), Options(), _ => false);

            StringAssert.Contains(plan[0].Content, "<CargoBin>alpha</CargoBin>");
        }

        [TestMethod]
        public void Plan_OmitsCargoManifestPath_WhenTheProjectSitsNextToTheManifest()
        {
            var app = Package("app", Path.Combine(Root, "app"), new[] { Bin("app") });
            var plan = RsprojGenerationPlanner.Plan(Metadata(app), Options(), _ => false);

            StringAssert.DoesNotMatch(plan[0].Content, new System.Text.RegularExpressions.Regex("CargoManifestPath"));
        }

        [TestMethod]
        public void Plan_SetsCargoManifestPath_WhenTheProjectDirectoryIsResolvedElsewhere()
        {
            var app = Package("app", Path.Combine(Root, "app"), new[] { Bin("app") });
            var mirrorRoot = @"C:\mirror";
            var options = new RsprojGenerationOptions("1.0.0", resolveProjectDirectory: package => Path.Combine(mirrorRoot, package.Name));

            var plan = RsprojGenerationPlanner.Plan(Metadata(app), options, _ => false);

            Assert.AreEqual(Path.Combine(mirrorRoot, "app", "app.rsproj"), plan[0].ProjectPath);
            StringAssert.Contains(plan[0].Content, "<CargoManifestPath>");
            // Points back at the real manifest, not a sibling one.
            StringAssert.Contains(plan[0].Content, "app");
            StringAssert.Contains(plan[0].Content, "Cargo.toml");
        }

        [TestMethod]
        public void Plan_OrdersResultsByPackageName()
        {
            var zeta = Package("zeta", Path.Combine(Root, "zeta"), new[] { Bin("zeta") });
            var alpha = Package("alpha", Path.Combine(Root, "alpha"), new[] { Bin("alpha") });
            var plan = RsprojGenerationPlanner.Plan(Metadata(zeta, alpha), Options(), _ => false);

            Assert.AreEqual("alpha", plan[0].PackageName);
            Assert.AreEqual("zeta", plan[1].PackageName);
        }
    }
}
