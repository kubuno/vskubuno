using Kubuno.Rust.TestAdapter.Discovery;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;

namespace Kubuno.Rust.TestAdapter.Tests.Discovery
{
    public class CargoTestCaseFactoryTests
    {
        private static CargoTestBinary MakeBinary(string targetName, CargoTestBinaryKind kind, string sourcePath) =>
            new CargoTestBinary(
                targetName,
                kind,
                sourcePath,
                executablePath: @"C:\target\debug\deps\exe-abc123.exe",
                packageManifestPath: @"Z:\projects\kubuno\vskubuno\samples\hello-rust\Cargo.toml",
                packageRoot: @"Z:\projects\kubuno\vskubuno\samples\hello-rust",
                targetDirectory: @"C:\target");

        [Fact]
        public void FullyQualifiedName_is_target_name_then_the_libtest_path()
        {
            var binary = MakeBinary("hello_rust", CargoTestBinaryKind.Lib, sourcePath: @"C:\does-not-exist.rs");

            TestCase testCase = CargoTestCaseFactory.CreateTestCase(binary, "tests::greet_includes_the_name");

            Assert.Equal("hello_rust::tests::greet_includes_the_name", testCase.FullyQualifiedName);
            Assert.Equal("tests::greet_includes_the_name", testCase.DisplayName);
        }

        [Fact]
        public void Source_is_the_package_manifest_path_not_the_executable()
        {
            var binary = MakeBinary("hello_rust", CargoTestBinaryKind.Lib, sourcePath: @"C:\does-not-exist.rs");

            TestCase testCase = CargoTestCaseFactory.CreateTestCase(binary, "tests::greet_includes_the_name");

            Assert.Equal(binary.PackageManifestPath, testCase.Source);
        }

        [Fact]
        public void Carries_the_executable_path_working_directory_and_target_directory_as_properties()
        {
            var binary = MakeBinary("basic", CargoTestBinaryKind.Integration, sourcePath: @"C:\does-not-exist.rs");

            TestCase testCase = CargoTestCaseFactory.CreateTestCase(binary, "greet_includes_the_name");

            Assert.Equal(binary.ExecutablePath, testCase.GetPropertyValue(KubunoTestProperties.ExecutablePath, string.Empty));
            Assert.Equal("greet_includes_the_name", testCase.GetPropertyValue(KubunoTestProperties.LibtestName, string.Empty));
            Assert.Equal(binary.PackageRoot, testCase.GetPropertyValue(KubunoTestProperties.WorkingDirectory, string.Empty));
            Assert.Equal(binary.TargetDirectory, testCase.GetPropertyValue(KubunoTestProperties.TargetDirectory, string.Empty));
        }

        [Fact]
        public void Different_target_kinds_disambiguate_same_named_tests()
        {
            var lib = MakeBinary("hello_rust", CargoTestBinaryKind.Lib, sourcePath: @"C:\does-not-exist.rs");
            var bin = MakeBinary("hello-rust", CargoTestBinaryKind.Bin, sourcePath: @"C:\does-not-exist.rs");

            TestCase libCase = CargoTestCaseFactory.CreateTestCase(lib, "tests::it_works");
            TestCase binCase = CargoTestCaseFactory.CreateTestCase(bin, "tests::it_works");

            Assert.NotEqual(libCase.FullyQualifiedName, binCase.FullyQualifiedName);
        }

        [Fact]
        public void CreateTestCases_maps_every_listed_entry()
        {
            var binary = MakeBinary("fixture_crate", CargoTestBinaryKind.Lib, sourcePath: @"C:\does-not-exist.rs");
            var entries = LibtestListParser.Parse(TestFixtures.ReadAllLines("Discovery", "fixture-crate-list.terse.txt"));

            var testCases = CargoTestCaseFactory.CreateTestCases(binary, entries);

            Assert.Equal(3, testCases.Count);
            Assert.Contains(testCases, t => t.FullyQualifiedName == "fixture_crate::tests::it_passes");
            Assert.Contains(testCases, t => t.FullyQualifiedName == "fixture_crate::tests::it_fails");
            Assert.Contains(testCases, t => t.FullyQualifiedName == "fixture_crate::tests::it_is_ignored");
        }
    }
}
