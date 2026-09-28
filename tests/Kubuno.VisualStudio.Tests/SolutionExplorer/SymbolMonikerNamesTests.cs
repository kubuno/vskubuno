using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Kubuno.Cargo.Metadata;
using Kubuno.VisualStudio.Core.SolutionExplorer;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.VisualStudio.Tests.SolutionExplorer
{
    [TestClass]
    public class SymbolMonikerNamesTests
    {
        [TestMethod]
        [DataRow(SolutionSymbolKind.Struct, SymbolVisibility.Public, "StructurePublic")]
        [DataRow(SolutionSymbolKind.Struct, SymbolVisibility.Internal, "StructureInternal")]
        [DataRow(SolutionSymbolKind.Enum, SymbolVisibility.Protected, "EnumerationProtected")]
        [DataRow(SolutionSymbolKind.Trait, SymbolVisibility.Private, "InterfacePrivate")]
        [DataRow(SolutionSymbolKind.Method, SymbolVisibility.Private, "MethodPrivate")]
        [DataRow(SolutionSymbolKind.Function, SymbolVisibility.Public, "MethodPublic")]
        [DataRow(SolutionSymbolKind.Field, SymbolVisibility.Internal, "FieldInternal")]
        [DataRow(SolutionSymbolKind.Static, SymbolVisibility.Private, "ConstantPrivate")]
        [DataRow(SolutionSymbolKind.Variant, SymbolVisibility.Public, "EnumerationItemPublic")]
        [DataRow(SolutionSymbolKind.TypeAlias, SymbolVisibility.Public, "TypeDefinitionPublic")]
        [DataRow(SolutionSymbolKind.Macro, SymbolVisibility.Private, "MacroPrivate")]
        [DataRow(SolutionSymbolKind.Module, SymbolVisibility.Public, "ModulePublic")]
        [DataRow(SolutionSymbolKind.TraitImpl, SymbolVisibility.Public, "ImplementInterface")]
        public void RustKindsUseRoslynLikeFamiliesAndAccessibilityVariants(SolutionSymbolKind kind, SymbolVisibility visibility, string expected)
        {
            Assert.AreEqual(expected, SymbolMonikerNames.ForRust(kind, visibility));
        }

        [TestMethod]
        [DataRow("Button", "Button")]
        [DataRow("CheckBox", "CheckBoxChecked")]
        [DataRow("TextField", "TextBox")]
        [DataRow("Stack", "StackPanel")]
        [DataRow("Card", "Panel")]
        [DataRow("ListView", "ListView")]
        [DataRow("MyFancyButton", "Button")]
        [DataRow("Whatever", "UserControl")]
        public void ViewElementsUseAControlGlyphPerFamily(string tag, string expected)
        {
            Assert.AreEqual(expected, SymbolMonikerNames.ForViewElement(tag));
        }

        [TestMethod]
        public void ANamedViewElementIsIconedByItsTagNotItsName()
        {
            var element = new SolutionSymbol("hello", SolutionSymbolKind.ViewElement, SymbolVisibility.Public, "Button", 0, 0);
            Assert.AreEqual("Button", SymbolMonikerNames.For(element));
            Assert.AreEqual("hello (Button)", element.DisplayText);
        }

        /// <summary>Every name must exist in the real image catalog of the installed Visual Studio.</summary>
        [TestMethod]
        public void EveryMonikerNameExistsInTheInstalledImageCatalog()
        {
            var catalog = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                @"Microsoft Visual Studio\18\Community\Common7\IDE\Microsoft.VisualStudio.ImageCatalog.dll");
            if (!File.Exists(catalog))
            {
                Assert.Inconclusive("Visual Studio 2026 Community is not installed at the expected path: " + catalog);
            }

            var knownMonikers = Assembly.LoadFrom(catalog).GetType("Microsoft.VisualStudio.Imaging.KnownMonikers", throwOnError: true)!;
            var missing = SymbolMonikerNames.AllNames().Distinct()
                .Where(name => knownMonikers.GetProperty(name, BindingFlags.Public | BindingFlags.Static) == null)
                .ToList();

            Assert.AreEqual(0, missing.Count, "Unknown KnownMonikers: " + string.Join(", ", missing));
        }

        [TestMethod]
        public void DependenciesAreGroupedByTableSortedAndDeduplicated()
        {
            var package = new CargoPackage
            {
                Name = "app",
                Dependencies = new[]
                {
                    new CargoDependency { Name = "serde", Req = "^1.0" },
                    new CargoDependency { Name = "tempfile", Req = "^3", Kind = "dev" },
                    new CargoDependency { Name = "cc", Req = "^1", Kind = "build" },
                    new CargoDependency { Name = "anyhow", Req = "^1" },
                    new CargoDependency { Name = "windows", Req = "^0.62", Target = "cfg(windows)" },
                    new CargoDependency { Name = "windows", Req = "^0.62", Target = "cfg(target_os = \"windows\")" },
                    new CargoDependency { Name = "core-lib", Req = "*", Path = @"C:\ws\core-lib" },
                },
            };

            var groups = CargoDependencyGroups.Group(package);

            CollectionAssert.AreEqual(
                new[] { CargoDependencyGroupKind.Normal, CargoDependencyGroupKind.Dev, CargoDependencyGroupKind.Build },
                groups.Select(g => g.Kind).ToArray());
            CollectionAssert.AreEqual(
                new[] { "anyhow (^1)", "core-lib (path)", "serde (^1.0)", "windows (^0.62)" },
                groups[0].Dependencies.Select(CargoDependencyGroups.DisplayText).ToArray());
            Assert.AreEqual("Dev-dependencies", groups[1].DisplayText);
        }

        [TestMethod]
        public void EmptyGroupsAreOmittedAndRenamesShowTheRealCrate()
        {
            var package = new CargoPackage
            {
                Dependencies = new[] { new CargoDependency { Name = "serde_json", Rename = "json", Req = "^1", Optional = true } },
            };

            var group = CargoDependencyGroups.Group(package).Single();
            Assert.AreEqual("Crates", group.DisplayText);
            Assert.AreEqual("json (serde_json ^1), optional", CargoDependencyGroups.DisplayText(group.Dependencies[0]));
        }

        [TestMethod]
        public void FindPackagePrefersTheCargoPackageNameThenTheManifest()
        {
            var metadata = new CargoMetadata
            {
                Packages = new[]
                {
                    new CargoPackage { Name = "a", ManifestPath = @"C:\ws\a\Cargo.toml" },
                    new CargoPackage { Name = "b", ManifestPath = @"C:\ws\b\Cargo.toml" },
                },
            };

            Assert.AreEqual("b", CargoDependencyGroups.FindPackage(metadata, @"C:\ws\a\Cargo.toml", "b")!.Name);
            Assert.AreEqual("a", CargoDependencyGroups.FindPackage(metadata, @"c:\ws\a\Cargo.toml", null)!.Name);
            Assert.IsNull(CargoDependencyGroups.FindPackage(metadata, @"C:\elsewhere\Cargo.toml", null));
        }
    }
}
