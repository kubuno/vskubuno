using System.Globalization;
using System.Linq;
using Kubuno.Rust.Cargo.Metadata;
using Kubuno.Rust.Logic.SolutionExplorer;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Rust.Tests.SolutionExplorer
{
    [TestClass]
    public class CrateInstallPlannerTests
    {
        private const string Manifest = @"C:\app\Cargo.toml";

        private static string[] Args(System.Collections.Generic.IReadOnlyList<Kubuno.Rust.Cargo.Commands.CargoCommand> commands) =>
            commands.Select(c => c.ToCommandLine().Arguments).ToArray();

        private static DependencyItem Installed(params CargoDependency[] declarations) =>
            new DependencyItem(declarations[0].Name, DependencyItemKind.Crate, DependencyCategory.Crates) { Rename = declarations[0].Rename, Declarations = declarations };

        [TestMethod]
        public void InstallAddsVersionFeaturesDefaultsAndKind()
        {
            CollectionAssert.AreEqual(
                new[] { @"add --manifest-path C:\app\Cargo.toml tokio@1.40.0 --features rt,macros --no-default-features --dev" },
                Args(CrateInstallPlanner.Install(Manifest, null, "tokio", "1.40.0", new[] { "rt", "macros" }, defaultFeatures: false, DependencyKinds.Dev)));
            CollectionAssert.AreEqual(
                new[] { @"add --manifest-path C:\app\Cargo.toml -p app anyhow" },
                Args(CrateInstallPlanner.Install(Manifest, "app", "anyhow", null, new string[0], defaultFeatures: true, DependencyKinds.Normal)));
        }

        [TestMethod]
        public void UpdatingTheVersionOrAddingFeaturesIsASingleCargoAdd()
        {
            var serde = Installed(new CargoDependency { Name = "serde", Req = "^1.0", Features = new[] { "derive" } });

            CollectionAssert.AreEqual(
                new[] { @"add --manifest-path C:\app\Cargo.toml serde@1.0.230 --features rc" },
                Args(CrateInstallPlanner.Update(Manifest, null, serde, "1.0.230", new[] { "derive", "rc" }, defaultFeatures: true)));
        }

        [TestMethod]
        public void DroppingAFeatureRemovesThenReAddsWithEverySetting()
        {
            var json = Installed(new CargoDependency { Name = "serde_json", Rename = "json", Req = "^1", Features = new[] { "std", "raw_value" }, Optional = true, UsesDefaultFeatures = false, Target = "cfg(windows)" });

            CollectionAssert.AreEqual(
                new[]
                {
                    @"remove --manifest-path C:\app\Cargo.toml json --target cfg(windows)",
                    @"add --manifest-path C:\app\Cargo.toml serde_json@1.0.151 --rename json --features std --optional --no-default-features --target cfg(windows)",
                },
                Args(CrateInstallPlanner.Update(Manifest, null, json, "1.0.151", new[] { "std" }, defaultFeatures: false)));
        }

        [TestMethod]
        public void ADependencyDeclaredInTwoTablesIsChangedInBoth()
        {
            var both = Installed(
                new CargoDependency { Name = "log", Req = "^0.4" },
                new CargoDependency { Name = "log", Req = "^0.4", Kind = "build" });

            CollectionAssert.AreEqual(
                new[] { @"add --manifest-path C:\app\Cargo.toml log@0.4.22", @"add --manifest-path C:\app\Cargo.toml log@0.4.22 --build" },
                Args(CrateInstallPlanner.Update(Manifest, null, both, "0.4.22", new string[0], defaultFeatures: true)));
            CollectionAssert.AreEqual(
                new[] { @"remove --manifest-path C:\app\Cargo.toml log", @"remove --manifest-path C:\app\Cargo.toml log --build" },
                Args(CrateInstallPlanner.Uninstall(Manifest, null, "log", both.Declarations)));
        }

        [TestMethod]
        public void DownloadCountsAreShortened()
        {
            var invariant = CultureInfo.InvariantCulture;
            Assert.AreEqual("1.5G", CrateInstallPlanner.FormatCount(1_452_926_172, invariant));
            Assert.AreEqual("326.3M", CrateInstallPlanner.FormatCount(326_285_284, invariant));
            Assert.AreEqual("12.5K", CrateInstallPlanner.FormatCount(12_500, invariant));
            Assert.AreEqual("812", CrateInstallPlanner.FormatCount(812, invariant));
            Assert.AreEqual("1,5G", CrateInstallPlanner.FormatCount(1_452_926_172, CultureInfo.GetCultureInfo("fr-FR")));
        }
    }
}
