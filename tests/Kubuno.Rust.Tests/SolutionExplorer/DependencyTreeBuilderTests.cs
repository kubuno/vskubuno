using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Kubuno.Cargo.Metadata;
using Kubuno.Cargo.Registry;
using Kubuno.Cargo.Toolchain;
using Kubuno.VisualStudio.Core.SolutionExplorer;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.VisualStudio.Tests.SolutionExplorer
{
    /// <summary>
    /// The Dependencies node mapping, fed real `cargo metadata` output (cargo 1.98.1) of a package that
    /// has every kind of dependency: see Kubuno.Cargo.Tests' DependencyMetadataParsingTests.
    /// </summary>
    [TestClass]
    public class DependencyTreeBuilderTests
    {
        private static readonly RustcVersionInfo Toolchain = RustcVersionInfo.Parse(new[]
        {
            "rustc 1.98.1 (48a229cea 2026-09-01)",
            "host: x86_64-pc-windows-msvc",
            "release: 1.98.1",
        })!;

        [TestInitialize]
        public void English() => DependenciesText.ForceFrench = false;

        [TestCleanup]
        public void Reset() => DependenciesText.ForceFrench = null;

        private static CargoMetadata Fixture(string name) =>
            CargoMetadataReader.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name)));

        private static DependencyTreeModel Resolved()
        {
            var full = Fixture("dependencies-full.json");
            var app = full.Packages.Single(p => p.Name == "app");
            return DependencyTreeBuilder.Build(app, full, Toolchain, resolveError: null);
        }

        private static DependencyItem Item(DependencyTreeModel model, string displayName) =>
            model.AllItems.Single(i => i.DisplayName == displayName);

        [TestMethod]
        public void CategoriesMirrorTheDotNetGroupsInOrder()
        {
            var model = Resolved();

            CollectionAssert.AreEqual(
                new[] { DependencyCategory.ProcMacros, DependencyCategory.Toolchain, DependencyCategory.Crates, DependencyCategory.Projects, DependencyCategory.Git },
                model.Groups.Select(g => g.Category).ToArray());
            CollectionAssert.AreEqual(
                new[] { "Procedural macros", "Toolchain", "Crates", "Projects", "Git" },
                model.Groups.Select(g => g.Text).ToArray());
            Assert.IsTrue(model.IsResolved);
            Assert.AreEqual(0, model.Diagnostics.Count);
        }

        [TestMethod]
        public void EachDependencyLandsInItsCategoryWithItsResolvedVersion()
        {
            var model = Resolved();

            CollectionAssert.AreEqual(new[] { "serde_derive (1.0.229)" }, model.Groups[0].Items.Select(i => i.Text).ToArray());
            CollectionAssert.AreEqual(
                new[] { "anyhow (1.0.104)", "autocfg (1.5.1) [build]", "itoa (^1)", "json (serde_json 1.0.151)", "scopeguard (1.2.0)", "serde (1.0.229)", "static_assertions (1.1.0) [dev]" },
                model.Groups.Single(g => g.Category == DependencyCategory.Crates).Items.Select(i => i.Text).ToArray());
            CollectionAssert.AreEqual(new[] { "corelib" }, model.Groups.Single(g => g.Category == DependencyCategory.Projects).Items.Select(i => i.Text).ToArray());
            CollectionAssert.AreEqual(new[] { "ryu (1.0.18)" }, model.Groups.Single(g => g.Category == DependencyCategory.Git).Items.Select(i => i.Text).ToArray());
        }

        [TestMethod]
        public void ProcMacrosBroughtByADependencyAreListedAsTransitive()
        {
            // Same graph, but Cargo.toml no longer declares serde_derive itself: serde's "derive" brings it.
            var full = Fixture("dependencies-full.json");
            var app = full.Packages.Single(p => p.Name == "app");
            app.Dependencies = app.Dependencies.Where(d => d.Name != "serde_derive").ToList();

            var procMacros = DependencyTreeBuilder.Build(app, full, Toolchain, null).Groups.Single(g => g.Category == DependencyCategory.ProcMacros);

            var derive = procMacros.Items.Single();
            Assert.AreEqual("serde_derive (1.0.229)", derive.Text);
            Assert.IsTrue(derive.IsTransitive);
            Assert.AreEqual(DependencyState.Resolved, derive.State);
        }

        [TestMethod]
        public void ATargetSpecificDependencyFilteredOutForTheHostIsInactive()
        {
            // cargo metadata --filter-platform drops the edges of other platforms' [target.'cfg(..)'] tables.
            var full = Fixture("dependencies-full.json");
            var app = full.Packages.Single(p => p.Name == "app");
            var root = full.Resolve!.Find(full.Resolve.Root!)!;
            root.Deps = root.Deps.Where(d => d.Name != "scopeguard").ToList();

            var scopeguard = Item(DependencyTreeBuilder.Build(app, full, Toolchain, null), "scopeguard");

            Assert.AreEqual(DependencyState.Inactive, scopeguard.State);
            StringAssert.Contains(scopeguard.ToolTip, "cfg(windows)");
        }

        [TestMethod]
        public void ToolchainNodeShowsVersionChannelAndHostAndListsTheSysrootCrates()
        {
            var model = Resolved();
            var toolchain = model.Groups.Single(g => g.Category == DependencyCategory.Toolchain).Items.Single();

            Assert.AreEqual("Rust 1.98.1 (stable, x86_64-pc-windows-msvc)", toolchain.Text);
            Assert.IsTrue(model.HasChildren(toolchain));
            CollectionAssert.AreEqual(new[] { "std", "core", "alloc", "proc_macro", "test" }, model.ChildrenOf(toolchain).Select(c => c.Text).ToArray());
        }

        [TestMethod]
        public void AnOptionalDependencyNoFeatureEnablesIsInactiveNotAnError()
        {
            var model = Resolved();

            var itoa = Item(model, "itoa");
            Assert.AreEqual(DependencyState.Inactive, itoa.State);
            Assert.IsNull(itoa.Package);
            Assert.IsFalse(model.Groups.Single(g => g.Category == DependencyCategory.Crates).HasProblem);
            Assert.AreEqual("NuGetNoColor", DependencyMonikerNames.Item(itoa));
        }

        [TestMethod]
        public void ResolvedCratesExpandIntoTheirTransitiveDependencies()
        {
            var model = Resolved();

            var json = Item(model, "json");
            Assert.IsTrue(model.HasChildren(json));
            var children = model.ChildrenOf(json);
            CollectionAssert.AreEqual(new[] { "itoa (1.0.18)", "memchr (2.8.3)", "serde (1.0.229)", "serde_core (1.0.229)", "zmij (1.0.23)" }, children.Select(c => c.Text).ToArray());
            Assert.IsTrue(children.All(c => c.IsTransitive && c.State == DependencyState.Resolved));

            // serde (derive) -> serde_derive is a proc-macro: its transitive node gets the proc-macro category.
            var serdeChildren = model.ChildrenOf(Item(model, "serde"));
            Assert.AreEqual(DependencyCategory.ProcMacros, serdeChildren.Single(c => c.Name == "serde_derive").Category);

            Assert.IsFalse(model.HasChildren(Item(model, "anyhow")));
        }

        [TestMethod]
        public void ActivatedFeaturesComeFromTheResolveGraph()
        {
            var serde = Item(Resolved(), "serde");

            CollectionAssert.IsSubsetOf(new[] { "derive", "std" }, serde.ActivatedFeatures.ToArray());
            CollectionAssert.AreEqual(new[] { "derive" }, serde.PrimaryDeclaration!.Features.ToArray());
        }

        [TestMethod]
        public void WithoutAResolveGraphDeclarationsArePendingThenUnresolvedOnError()
        {
            var noDeps = Fixture("dependencies-no-deps.json").Packages.Single();

            var pending = DependencyTreeBuilder.Build(noDeps, resolved: null, Toolchain, resolveError: null);
            // The fixture's path dependency points at the machine it was captured on, so it is missing here.
            Assert.IsTrue(pending.AllItems.Where(i => i.ItemKind == DependencyItemKind.Crate && i.Category != DependencyCategory.Projects).All(i => i.State == DependencyState.Pending));
            Assert.AreEqual(DependencyState.Unresolved, Item(pending, "corelib").State);
            Assert.AreEqual("serde (^1.0)", Item(pending, "serde").Text);
            Assert.AreEqual(DependencyCategory.Git, Item(pending, "ryu").Category);
            Assert.AreEqual(DependencyCategory.Projects, Item(pending, "corelib").Category);
            // Without the graph a proc-macro cannot be told apart from any other crate yet.
            Assert.AreEqual(DependencyCategory.Crates, Item(pending, "serde_derive").Category);

            var failed = DependencyTreeBuilder.Build(noDeps, resolved: null, toolchain: null, resolveError: "failed to get `serde` as a dependency");
            Assert.AreEqual(1, failed.Diagnostics.Count);
            StringAssert.Contains(failed.Diagnostics[0], "failed to get `serde`");
            Assert.AreEqual(DependencyState.Unresolved, Item(failed, "serde").State);
            Assert.AreEqual("NuGetNoColorWarning", DependencyMonikerNames.Item(Item(failed, "serde")));
            Assert.IsTrue(failed.HasProblem);
            Assert.IsFalse(failed.Groups.Any(g => g.Category == DependencyCategory.Toolchain));
        }

        [TestMethod]
        public void AMissingPathDependencyIsUnresolvedWithItsPathInTheToolTip()
        {
            var package = new CargoPackage
            {
                Name = "app",
                ManifestPath = @"C:\nowhere\app\Cargo.toml",
                Dependencies = new[] { new CargoDependency { Name = "gone", Req = "*", Path = @"C:\nowhere\gone-" + Guid.NewGuid().ToString("N") } },
            };

            var item = DependencyTreeBuilder.Build(package, null, null, null).AllItems.Single();

            Assert.AreEqual(DependencyState.Unresolved, item.State);
            StringAssert.Contains(item.ToolTip, @"C:\nowhere\gone-");
            Assert.AreEqual("ApplicationWarning", DependencyMonikerNames.Item(item));
        }

        [TestMethod]
        public void RegistryStatusMarksYankedAndOutdatedCrates()
        {
            var anyhow = Item(Resolved(), "anyhow");
            Assert.IsTrue(anyhow.IsCratesIo);

            anyhow.ApplyRegistryStatus(CrateVersionStatus.Compute("1.0.104", new[]
            {
                new CrateIndexVersion { Name = "anyhow", Vers = "1.0.104" },
                new CrateIndexVersion { Name = "anyhow", Vers = "1.0.105" },
                new CrateIndexVersion { Name = "anyhow", Vers = "2.0.0-rc.1" },
            }));
            Assert.AreEqual("1.0.105", anyhow.LatestVersion);
            Assert.IsTrue(anyhow.IsOutdated);
            StringAssert.Contains(anyhow.ToolTip, "1.0.105");

            anyhow.ApplyRegistryStatus(CrateVersionStatus.Compute("1.0.104", new[] { new CrateIndexVersion { Name = "anyhow", Vers = "1.0.104", Yanked = true } }));
            Assert.IsTrue(anyhow.IsYanked);
            Assert.AreEqual("NuGetNoColorWarning", DependencyMonikerNames.Item(anyhow));
        }

        [TestMethod]
        public void FrenchLabelsAreTheDotNetProjectSystemOnes()
        {
            DependenciesText.ForceFrench = true;

            Assert.AreEqual("Dépendances", DependenciesText.Dependencies);
            CollectionAssert.AreEqual(
                new[] { "Macros procédurales", "Chaîne d'outils", "Crates", "Projets", "Git" },
                Resolved().Groups.Select(g => g.Text).ToArray());
        }

        [TestMethod]
        public void EveryDependencyMonikerNameExistsInTheInstalledImageCatalog()
        {
            var catalog = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                @"Microsoft Visual Studio\18\Community\Common7\IDE\Microsoft.VisualStudio.ImageCatalog.dll");
            if (!File.Exists(catalog))
            {
                Assert.Inconclusive("Visual Studio 2026 Community is not installed at the expected path: " + catalog);
            }

            var knownMonikers = Assembly.LoadFrom(catalog).GetType("Microsoft.VisualStudio.Imaging.KnownMonikers", throwOnError: true)!;
            var model = Resolved();
            var names = model.Groups.Select(g => DependencyMonikerNames.Group(g.Category))
                .Concat(Enum.GetValues(typeof(DependencyCategory)).Cast<DependencyCategory>().Select(DependencyMonikerNames.Group))
                .Concat(model.AllItems.Select(DependencyMonikerNames.Item))
                .Concat(new[]
                {
                    DependencyMonikerNames.Root, DependencyMonikerNames.RootWarning, DependencyMonikerNames.RootError,
                    DependencyMonikerNames.Diagnostic, DependencyMonikerNames.UpdateOverlay,
                    "CodeInformationWarning", "ApplicationWarning", "NuGetNoColorWarning", "StatusWarning", "Library",
                })
                .Distinct();
            var missing = names.Where(name => knownMonikers.GetProperty(name, BindingFlags.Public | BindingFlags.Static) == null).ToList();

            Assert.AreEqual(0, missing.Count, "Unknown KnownMonikers: " + string.Join(", ", missing));
        }
    }
}
