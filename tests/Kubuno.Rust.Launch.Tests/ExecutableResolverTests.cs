using System;
using Kubuno.Rust.Launch;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Rust.Launch.Tests
{
    [TestClass]
    public sealed class ExecutableResolverTests
    {
        private const string WorkspaceRoot = @"Z:\projects\kubuno\desktop\windows";
        private const string TargetDir = @"C:\kubuno-build\desktop-target";

        [TestMethod]
        public void Resolve_BinTarget_UsesPathConvention()
        {
            var target = new LaunchTarget(
                Package: "kubuno-desktop-shell",
                Name: "kubuno",
                Kind: LaunchTargetKind.Bin,
                Profile: "dev",
                TargetDir: TargetDir,
                WorkspaceRoot: WorkspaceRoot);

            var path = ExecutableResolver.Resolve(target);

            Assert.AreEqual(@"C:\kubuno-build\desktop-target\debug\kubuno.exe", path);
        }

        [TestMethod]
        public void Resolve_ExampleTarget_AddsExamplesSubdirectory()
        {
            var target = new LaunchTarget(
                Package: "kubuno-desktop-shell",
                Name: "hello",
                Kind: LaunchTargetKind.Example,
                Profile: "release",
                TargetDir: TargetDir,
                WorkspaceRoot: WorkspaceRoot);

            var path = ExecutableResolver.Resolve(target);

            Assert.AreEqual(@"C:\kubuno-build\desktop-target\release\examples\hello.exe", path);
        }

        [TestMethod]
        public void Resolve_HonoursTargetTripleSubdirectory()
        {
            var target = new LaunchTarget(
                Package: "kubuno-desktop-shell",
                Name: "kubuno",
                Kind: LaunchTargetKind.Bin,
                Profile: "dev",
                TargetDir: TargetDir,
                WorkspaceRoot: WorkspaceRoot,
                TargetTriple: "x86_64-pc-windows-msvc");

            var path = ExecutableResolver.Resolve(target);

            Assert.AreEqual(@"C:\kubuno-build\desktop-target\x86_64-pc-windows-msvc\debug\kubuno.exe", path);
        }

        [TestMethod]
        public void Resolve_PrefersCargoReportedExecutablePath_OverPathConvention()
        {
            var target = new LaunchTarget(
                Package: "kubuno-desktop-shell",
                Name: "kubuno",
                Kind: LaunchTargetKind.Bin,
                Profile: "dev",
                TargetDir: TargetDir,
                WorkspaceRoot: WorkspaceRoot,
                CargoExecutablePath: @"C:\kubuno-build\desktop-target\debug\kubuno.exe.actually-different");

            var path = ExecutableResolver.Resolve(target);

            Assert.AreEqual(@"C:\kubuno-build\desktop-target\debug\kubuno.exe.actually-different", path);
        }

        [TestMethod]
        public void Resolve_TestTarget_UsesSuppliedTestBinaryPath()
        {
            var target = new LaunchTarget(
                Package: "kubuno-desktop-shell",
                Name: "sync_tests",
                Kind: LaunchTargetKind.Test,
                Profile: "dev",
                TargetDir: TargetDir,
                WorkspaceRoot: WorkspaceRoot,
                TestBinaryPath: @"C:\kubuno-build\desktop-target\debug\deps\sync_tests-1a2b3c4d5e6f7890.exe");

            var path = ExecutableResolver.Resolve(target);

            Assert.AreEqual(@"C:\kubuno-build\desktop-target\debug\deps\sync_tests-1a2b3c4d5e6f7890.exe", path);
        }

        [TestMethod]
        public void Resolve_TestTarget_ThrowsWithoutTestBinaryPath()
        {
            var target = new LaunchTarget(
                Package: "kubuno-desktop-shell",
                Name: "sync_tests",
                Kind: LaunchTargetKind.Test,
                Profile: "dev",
                TargetDir: TargetDir,
                WorkspaceRoot: WorkspaceRoot);

            Assert.ThrowsExactly<InvalidOperationException>(() => ExecutableResolver.Resolve(target));
        }

        [TestMethod]
        public void Resolve_ThrowsOnNullTarget()
        {
            Assert.ThrowsExactly<ArgumentNullException>(() => ExecutableResolver.Resolve(null!));
        }
    }
}
