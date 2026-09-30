using System;
using System.IO;
using System.Linq;
using Kubuno.Rust.Launch;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Rust.Launch.Tests
{
    [TestClass]
    public sealed class LaunchDescriptionBuilderTests
    {
        private const string WorkspaceRoot = @"Z:\projects\kubuno\desktop\windows";
        private const string TargetDir = @"C:\kubuno-build\desktop-target";
        private const string HostTriple = "x86_64-pc-windows-msvc";

        [TestMethod]
        public void Build_BinTarget_ProducesExpectedDescription()
        {
            var fakeSysroot = CreateFakeSysrootWithNatvis();
            try
            {
                var target = new LaunchTarget(
                    Package: "kubuno-desktop-shell",
                    Name: "kubuno",
                    Kind: LaunchTargetKind.Bin,
                    Profile: "dev",
                    TargetDir: TargetDir,
                    WorkspaceRoot: WorkspaceRoot,
                    PackageRoot: @"Z:\projects\kubuno\desktop\windows\kubuno-desktop-shell");

                var description = LaunchDescriptionBuilder.Build(target, fakeSysroot, HostTriple);

                Assert.AreEqual("kubuno", description.Name);
                Assert.AreEqual(@"C:\kubuno-build\desktop-target\debug\kubuno.exe", description.ExecutablePath);
                Assert.AreEqual(0, description.Arguments.Count);
                Assert.AreEqual(@"Z:\projects\kubuno\desktop\windows\kubuno-desktop-shell", description.WorkingDirectory);
                Assert.AreEqual("1", description.EnvironmentVariables["RUST_BACKTRACE"]);
                Assert.IsTrue(description.EnvironmentVariables["PATH"].StartsWith(@"C:\kubuno-build\desktop-target\debug;"));
                Assert.AreEqual(1, description.NatvisFiles.Count);
                Assert.AreEqual("native", description.Debugger);
            }
            finally
            {
                Directory.Delete(fakeSysroot, recursive: true);
            }
        }

        [TestMethod]
        public void Build_TestTarget_UsesSuppliedTestArgs()
        {
            var fakeSysroot = CreateFakeSysrootWithNatvis();
            try
            {
                var target = new LaunchTarget(
                    Package: "kubuno-desktop-shell",
                    Name: "sync_tests",
                    Kind: LaunchTargetKind.Test,
                    Profile: "dev",
                    TargetDir: TargetDir,
                    WorkspaceRoot: WorkspaceRoot,
                    TestBinaryPath: @"C:\kubuno-build\desktop-target\debug\deps\sync_tests-1a2b3c4d5e6f7890.exe");

                var testArgs = TestLaunchArgs.Build("module::tests::it_works");

                var description = LaunchDescriptionBuilder.Build(target, fakeSysroot, HostTriple, testArgs: testArgs);

                Assert.AreEqual(@"C:\kubuno-build\desktop-target\debug\deps\sync_tests-1a2b3c4d5e6f7890.exe", description.ExecutablePath);
                CollectionAssert.AreEqual(testArgs.ToArray(), description.Arguments.ToArray());
            }
            finally
            {
                Directory.Delete(fakeSysroot, recursive: true);
            }
        }

        [TestMethod]
        public void Build_FallsBackToWorkspaceRoot_WhenPackageRootNotSupplied()
        {
            var fakeSysroot = CreateFakeSysrootWithNatvis();
            try
            {
                var target = new LaunchTarget(
                    Package: "kubuno-desktop-shell",
                    Name: "kubuno",
                    Kind: LaunchTargetKind.Bin,
                    Profile: "dev",
                    TargetDir: TargetDir,
                    WorkspaceRoot: WorkspaceRoot);

                var description = LaunchDescriptionBuilder.Build(target, fakeSysroot, HostTriple);

                Assert.AreEqual(WorkspaceRoot, description.WorkingDirectory);
            }
            finally
            {
                Directory.Delete(fakeSysroot, recursive: true);
            }
        }

        [TestMethod]
        public void Build_WorkingDirectoryOverride_WinsOverPackageRoot()
        {
            var fakeSysroot = CreateFakeSysrootWithNatvis();
            try
            {
                var target = new LaunchTarget(
                    Package: "kubuno-desktop-shell",
                    Name: "kubuno",
                    Kind: LaunchTargetKind.Bin,
                    Profile: "dev",
                    TargetDir: TargetDir,
                    WorkspaceRoot: WorkspaceRoot,
                    PackageRoot: @"Z:\projects\kubuno\desktop\windows\kubuno-desktop-shell");

                var description = LaunchDescriptionBuilder.Build(
                    target, fakeSysroot, HostTriple, workingDirectoryOverride: @"C:\somewhere\else");

                Assert.AreEqual(@"C:\somewhere\else", description.WorkingDirectory);
            }
            finally
            {
                Directory.Delete(fakeSysroot, recursive: true);
            }
        }

        private static string CreateFakeSysrootWithNatvis()
        {
            var sysroot = Path.Combine(Path.GetTempPath(), "kubuno-launch-tests-" + Guid.NewGuid().ToString("N"));
            var etcDir = Path.Combine(sysroot, "lib", "rustlib", "etc");
            Directory.CreateDirectory(etcDir);
            File.WriteAllText(Path.Combine(etcDir, "libstd.natvis"), "<AutoVisualizer/>");
            return sysroot;
        }
    }
}
