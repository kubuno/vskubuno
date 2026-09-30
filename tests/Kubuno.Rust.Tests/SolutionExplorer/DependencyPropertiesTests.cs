using System;
using System.IO;
using System.Linq;
using Kubuno.Rust.Cargo.Metadata;
using Kubuno.Rust.Cargo.Toolchain;
using Kubuno.Rust.Logic.SolutionExplorer;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Rust.Tests.SolutionExplorer
{
    [TestClass]
    public class DependencyPropertiesTests
    {
        [TestCleanup]
        public void Reset() => DependenciesText.ForceFrench = null;

        private static DependencyTreeModel Model()
        {
            var full = CargoMetadataReader.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "dependencies-full.json")));
            var toolchain = RustcVersionInfo.Parse(new[] { "release: 1.98.1", "host: x86_64-pc-windows-msvc", "commit-hash: 48a229ce", "LLVM version: 22.1.8" })!;
            toolchain.Sysroot = @"C:\Users\dev\.rustup\toolchains\stable-x86_64-pc-windows-msvc";
            return DependencyTreeBuilder.Build(full.Packages.Single(p => p.Name == "app"), full, toolchain, null);
        }

        private static string Value(DependencyItem item, string key) => DependencyProperties.For(item).Single(p => p.Key == key).Value;

        [TestMethod]
        public void RegistryCrateRowsMatchTheProductOwnerList()
        {
            DependenciesText.ForceFrench = true;
            var serde = Model().AllItems.Single(i => i.Name == "serde");

            var rows = DependencyProperties.For(serde);
            var names = rows.Select(r => r.DisplayName).ToList();
            foreach (var expected in new[] { "Nom", "Version demandée", "Version résolue", "Source", "Chemin d'accès", "Fonctionnalités activées", "Facultative", "Type", "Cible", "Licence", "Dépôt", "Description" })
            {
                CollectionAssert.Contains(names, expected);
            }

            Assert.IsTrue(rows.All(r => r.Description.Length > 0), "every row has a description for the bottom pane");
            Assert.AreEqual("^1.0", Value(serde, "RequestedVersion"));
            Assert.AreEqual("1.0.229", Value(serde, "ResolvedVersion"));
            Assert.AreEqual("Registre (crates.io)", Value(serde, "Source"));
            Assert.AreEqual("MIT OR Apache-2.0", Value(serde, "License"));
            Assert.AreEqual("https://github.com/serde-rs/serde", Value(serde, "Repository"));
            Assert.AreEqual("Normale", Value(serde, "Kind"));
            Assert.AreEqual("False", Value(serde, "Optional"));
            StringAssert.Contains(Value(serde, "ActivatedFeatures"), "derive");
            StringAssert.EndsWith(Value(serde, "Path"), "serde-1.0.229");
            Assert.AreEqual("Propriétés de référence de crate", DependencyProperties.ClassName(serde));
        }

        [TestMethod]
        public void GitPathTargetAndKindRowsDescribeTheDeclaration()
        {
            DependenciesText.ForceFrench = false;
            var model = Model();

            var ryu = model.AllItems.Single(i => i.Name == "ryu");
            Assert.AreEqual("Git (https://github.com/dtolnay/ryu, tag=1.0.18) @ 12746aa88cc5", Value(ryu, "Source"));

            Assert.AreEqual("Path", Value(model.AllItems.Single(i => i.Name == "corelib"), "Source"));
            Assert.AreEqual("cfg(windows)", Value(model.AllItems.Single(i => i.Name == "scopeguard"), "Target"));
            Assert.AreEqual("Build script (build-dependencies)", Value(model.AllItems.Single(i => i.Name == "autocfg"), "Kind"));
            Assert.AreEqual("json", Value(model.AllItems.Single(i => i.Name == "serde_json"), "Rename"));
        }

        [TestMethod]
        public void TransitiveCratesHaveNoDeclarationRows()
        {
            var model = Model();
            var itoa = model.ChildrenOf(model.AllItems.Single(i => i.Name == "serde_json")).Single(c => c.Name == "itoa");

            var keys = DependencyProperties.For(itoa).Select(r => r.Key).ToList();
            CollectionAssert.DoesNotContain(keys, "RequestedFeatures");
            CollectionAssert.DoesNotContain(keys, "Target");
            Assert.AreEqual("True", Value(itoa, "Transitive"));
            Assert.AreEqual(string.Empty, Value(itoa, "RequestedVersion"));
        }

        [TestMethod]
        public void ToolchainRowsComeFromRustc()
        {
            DependenciesText.ForceFrench = true;
            var toolchain = Model().AllItems.Single(i => i.ItemKind == DependencyItemKind.Toolchain);

            Assert.AreEqual("1.98.1", Value(toolchain, "Version"));
            Assert.AreEqual("stable", Value(toolchain, "Channel"));
            Assert.AreEqual("x86_64-pc-windows-msvc", Value(toolchain, "Host"));
            Assert.AreEqual("22.1.8", Value(toolchain, "LlvmVersion"));
            StringAssert.EndsWith(Value(toolchain, "Path"), "stable-x86_64-pc-windows-msvc");
            Assert.AreEqual("Propriétés de la chaîne d'outils", DependencyProperties.ClassName(toolchain));
        }
    }
}
