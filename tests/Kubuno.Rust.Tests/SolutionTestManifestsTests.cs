using System.Linq;
using Kubuno.Rust.Logic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Rust.Tests
{
    [TestClass]
    public class SolutionTestManifestsTests
    {
        [TestMethod]
        public void EachPackageProjectOffersItsOwnManifest()
        {
            var manifests = SolutionTestManifests.Select(new[]
            {
                new RsprojTestProject(@"C:\a\Cargo.toml", @"C:\a", workspaceBuild: false),
                new RsprojTestProject(@"C:\b\Cargo.toml", @"C:\b", workspaceBuild: false),
            });

            CollectionAssert.AreEqual(new[] { @"C:\a\Cargo.toml", @"C:\b\Cargo.toml" }, manifests.ToList());
        }

        [TestMethod]
        public void AWorkspaceBuiltAsAWholeIsTestedAsAWholeOnce()
        {
            var manifests = SolutionTestManifests.Select(new[]
            {
                new RsprojTestProject(@"Z:\ws\src\app\Cargo.toml", @"Z:\ws", workspaceBuild: true),
                new RsprojTestProject(@"Z:\ws\src\ui\Cargo.toml", @"Z:\ws\", workspaceBuild: true),
                // A package project of the same workspace would build its tests a second time.
                new RsprojTestProject(@"Z:\ws\src\tool\Cargo.toml", @"Z:\ws", workspaceBuild: false),
                new RsprojTestProject(@"C:\other\Cargo.toml", @"C:\other", workspaceBuild: false),
            });

            CollectionAssert.AreEqual(new[] { @"Z:\ws\Cargo.toml", @"C:\other\Cargo.toml" }, manifests.ToList());
        }

        [TestMethod]
        public void AWorkspaceBuildWithoutAKnownRootFallsBackToThePackage()
        {
            var manifests = SolutionTestManifests.Select(new[] { new RsprojTestProject(@"C:\a\Cargo.toml", null, workspaceBuild: true) });

            CollectionAssert.AreEqual(new[] { @"C:\a\Cargo.toml" }, manifests.ToList());
        }
    }
}
