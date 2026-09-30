using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Rust.Cargo.Commands;
using Kubuno.Rust.Cargo.Diagnostics;
using Kubuno.Rust.Cargo.Processes;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;

namespace Kubuno.Rust.TestAdapter.Discovery
{
    /// <summary>
    /// VSTest test discoverer: for each source (a Cargo.toml manifest path - see Containers/ for
    /// why manifests, not built executables, are the "source"/container), builds the package's
    /// test binaries with `cargo test --no-run`, lists each binary's tests, and reports a
    /// <see cref="TestCase"/> per test.
    /// </summary>
    [FileExtension(KubunoTestAdapterConstants.ManifestFileExtension)]
    [DefaultExecutorUri(KubunoTestAdapterConstants.ExecutorUriString)]
    public sealed class KubunoTestDiscoverer : ITestDiscoverer
    {
        private readonly IProcessRunner _processRunner;
        private readonly IReadOnlyDictionary<string, string>? _environmentVariables;

        static KubunoTestDiscoverer() => AssemblyResolution.EnsureInstalled();

        public KubunoTestDiscoverer()
            : this(new ProcessRunner(), environmentVariables: null)
        {
        }

        /// <param name="processRunner">How to invoke `cargo`/the built test executables - injectable for tests.</param>
        /// <param name="environmentVariables">Extra environment (e.g. `CARGO_TARGET_DIR`) for both the `cargo test --no-run` build and the `--list` invocation.</param>
        public KubunoTestDiscoverer(IProcessRunner processRunner, IReadOnlyDictionary<string, string>? environmentVariables = null)
        {
            _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
            _environmentVariables = environmentVariables;
        }

        public void DiscoverTests(
            IEnumerable<string> sources,
            IDiscoveryContext discoveryContext,
            IMessageLogger logger,
            ITestCaseDiscoverySink discoverySink)
        {
            if (sources is null)
            {
                throw new ArgumentNullException(nameof(sources));
            }
            if (discoverySink is null)
            {
                throw new ArgumentNullException(nameof(discoverySink));
            }

            foreach (string source in sources)
            {
                // ITestDiscoverer.DiscoverTests is a synchronous VSTest contract (no
                // CancellationToken, no Task return): each manifest's build+list is awaited
                // in turn here rather than fired off in parallel, which also keeps concurrent
                // `cargo` invocations (they lock the target directory) out of the picture.
                DiscoverTestsInManifestAsync(source, logger, discoverySink).GetAwaiter().GetResult();
            }
        }

        private async Task DiscoverTestsInManifestAsync(string manifestPath, IMessageLogger? logger, ITestCaseDiscoverySink sink)
        {
            logger?.SendMessage(TestMessageLevel.Informational, $"Kubuno: building tests for '{manifestPath}' (cargo test --no-run)...");

            CargoTestBuildResult buildResult;
            try
            {
                buildResult = await CargoTestBuild
                    .RunAsync(_processRunner, manifestPath, _environmentVariables, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger?.SendMessage(TestMessageLevel.Error, $"Kubuno: 'cargo test --no-run' failed for '{manifestPath}': {ex.Message}");
                return;
            }

            foreach (CargoDiagnostic diagnostic in buildResult.Diagnostics)
            {
                if (diagnostic.Severity is CargoDiagnosticSeverity.Error or CargoDiagnosticSeverity.InternalCompilerError)
                {
                    logger?.SendMessage(TestMessageLevel.Error, FormatDiagnostic(diagnostic));
                }
            }

            if (!buildResult.Success)
            {
                logger?.SendMessage(TestMessageLevel.Warning, $"Kubuno: '{manifestPath}' did not build; no tests discovered from it this run.");
                return;
            }

            foreach (CargoTestBinary binary in buildResult.Binaries)
            {
                IReadOnlyList<string> listLines;
                try
                {
                    listLines = await RunListAsync(binary.ExecutablePath, binary.PackageRoot).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger?.SendMessage(TestMessageLevel.Warning, $"Kubuno: could not list tests in '{binary.ExecutablePath}': {ex.Message}");
                    continue;
                }

                IReadOnlyList<LibtestListEntry> entries = LibtestListParser.Parse(listLines);
                foreach (TestCase testCase in CargoTestCaseFactory.CreateTestCases(binary, entries))
                {
                    sink.SendTestCase(testCase);
                }
            }
        }

        private async Task<IReadOnlyList<string>> RunListAsync(string executablePath, string workingDirectory)
        {
            var commandLine = new CargoCommandLine(executablePath, new[] { "--list", "--format", "terse" });
            var request = new ProcessRunRequest(commandLine.FileName, commandLine.Arguments)
            {
                WorkingDirectory = workingDirectory,
                EnvironmentVariables = _environmentVariables,
            };
            ProcessRunResult result = await _processRunner.RunAsync(request, onOutput: null, CancellationToken.None).ConfigureAwait(false);
            return result.StandardOutputLines;
        }

        private static string FormatDiagnostic(CargoDiagnostic diagnostic)
        {
            return diagnostic.FilePath is null
                ? diagnostic.Message
                : $"{diagnostic.FilePath}({diagnostic.Line},{diagnostic.Column}): {diagnostic.Message}";
        }
    }
}
