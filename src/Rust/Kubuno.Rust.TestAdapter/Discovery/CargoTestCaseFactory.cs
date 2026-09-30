using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;

namespace Kubuno.TestAdapter.Discovery
{
    /// <summary>Maps a <see cref="CargoTestBinary"/> and its listed tests to VSTest <see cref="TestCase"/>s.</summary>
    public static class CargoTestCaseFactory
    {
        /// <param name="binary">The test binary the entries came from.</param>
        /// <param name="entries">Parsed `--list --format terse` output (see <see cref="LibtestListParser"/>). Benchmarks are skipped: `cargo test`'s harness runs benchmarks as plain tests (no timing), so nothing about them belongs in a "benchmark" concept here - the underlying libtest entry still runs correctly as a TestCase.</param>
        public static IReadOnlyList<TestCase> CreateTestCases(CargoTestBinary binary, IReadOnlyList<LibtestListEntry> entries)
        {
            if (binary is null)
            {
                throw new ArgumentNullException(nameof(binary));
            }
            if (entries is null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            var testCases = new List<TestCase>(entries.Count);
            foreach (LibtestListEntry entry in entries)
            {
                testCases.Add(CreateTestCase(binary, entry.Name));
            }
            return testCases;
        }

        public static TestCase CreateTestCase(CargoTestBinary binary, string libtestName)
        {
            // "crate::module::test": target name (the actual crate/bin/integration-test
            // identifier - already unique within a package, and already what a developer reading
            // `cargo test` output associates the test with) plus libtest's own "module::test"
            // path, exactly as instructed.
            string fullyQualifiedName = $"{binary.TargetName}::{libtestName}";

            var testCase = new TestCase(
                fullyQualifiedName,
                new Uri(KubunoTestAdapterConstants.ExecutorUriString),
                binary.PackageManifestPath)
            {
                DisplayName = libtestName,
            };

            TestSourceLocation? location = TestSourceLocator.TryLocate(binary.SourcePath, libtestName);
            if (location is { } resolved)
            {
                testCase.CodeFilePath = resolved.FilePath;
                testCase.LineNumber = resolved.LineNumber;
            }

            testCase.SetPropertyValue(KubunoTestProperties.ExecutablePath, binary.ExecutablePath);
            testCase.SetPropertyValue(KubunoTestProperties.LibtestName, libtestName);
            testCase.SetPropertyValue(KubunoTestProperties.WorkingDirectory, binary.PackageRoot);
            testCase.SetPropertyValue(KubunoTestProperties.TargetDirectory, binary.TargetDirectory);

            return testCase;
        }
    }
}
