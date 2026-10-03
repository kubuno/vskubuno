using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Rust.Cargo.Commands;
using Kubuno.Rust.Cargo.Diagnostics;
using Kubuno.Rust.Cargo.Processes;

namespace Kubuno.Rust.TestAdapter.Discovery
{
    /// <summary>Outcome of building a package's test binaries.</summary>
    public sealed class CargoTestBuildResult
    {
        public CargoTestBuildResult(bool success, IReadOnlyList<CargoTestBinary> binaries, IReadOnlyList<CargoDiagnostic> diagnostics)
        {
            Success = success;
            Binaries = binaries;
            Diagnostics = diagnostics;
        }

        /// <summary>False when `cargo test --no-run` itself failed (a compile error - see <see cref="Diagnostics"/>).</summary>
        public bool Success { get; }

        public IReadOnlyList<CargoTestBinary> Binaries { get; }

        /// <summary>Compiler diagnostics (errors/warnings) emitted while building, for <see cref="Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging.IMessageLogger"/> reporting.</summary>
        public IReadOnlyList<CargoDiagnostic> Diagnostics { get; }
    }

    /// <summary>
    /// Runs `cargo test --no-run --message-format=json` for one package and extracts the test
    /// binaries it produced.
    ///
    /// Deliberately does NOT reuse <see cref="CargoMessageParser"/>'s "compiler-artifact" model
    /// (<see cref="CargoArtifact"/>) for that extraction, even though it reuses it for
    /// diagnostics/build-finished below: <see cref="CargoArtifact"/> doesn't expose the
    /// `profile.test` field Cargo's JSON reports, and that field is the only reliable way to
    /// tell a target's *test-harness* artifact apart from its *ordinary* build artifact - both
    /// appear as separate "compiler-artifact" events for the same `[[bin]]` target during
    /// `cargo test --no-run` (verified against real output: a `[[bin]]` target with
    /// `path = "src/main.rs"` produces one artifact at
    /// `&lt;target-dir&gt;/debug/deps/&lt;name&gt;-&lt;hash&gt;.exe` with `profile.test = true` -
    /// the actual libtest harness to run - and a second, ordinary one at
    /// `&lt;target-dir&gt;/debug/&lt;name&gt;.exe` with `profile.test = false` - the normal binary,
    /// which does not understand `--list`/`--exact` and must not be picked). Kubuno.Rust.Cargo is
    /// owned by another agent and out of scope here (read-only), so this parses that one missing
    /// field itself with a small, purpose-built raw DTO instead.
    /// </summary>
    public static class CargoTestBuild
    {
        public static async Task<CargoTestBuildResult> RunAsync(
            IProcessRunner processRunner,
            string manifestPath,
            IReadOnlyDictionary<string, string>? environmentVariables,
            CancellationToken cancellationToken)
        {
            if (processRunner is null)
            {
                throw new ArgumentNullException(nameof(processRunner));
            }
            if (string.IsNullOrEmpty(manifestPath))
            {
                throw new ArgumentException("Manifest path must not be null or empty.", nameof(manifestPath));
            }

            string packageRoot = Path.GetDirectoryName(Path.GetFullPath(manifestPath))
                ?? throw new InvalidOperationException($"Could not determine the directory of '{manifestPath}'.");

            TestBuildIsolation.Plan? isolation = await TestBuildIsolation.PlanAsync(processRunner, manifestPath, packageRoot, environmentVariables, cancellationToken).ConfigureAwait(false);
            if (isolation is null)
            {
                return await RunOnceAsync(processRunner, manifestPath, packageRoot, Array.Empty<string>(), environmentVariables, cancellationToken).ConfigureAwait(false);
            }

            // The workspace without the isolated packages, then each of them in a target directory of its own
            // (TestBuildIsolation's remarks: a shared dylib would otherwise be built twice into the same file).
            var excludes = isolation.Packages.SelectMany(package => new[] { "--exclude", package }).Prepend("--workspace").ToArray();
            var results = new List<CargoTestBuildResult>
            {
                await RunOnceAsync(processRunner, manifestPath, packageRoot, excludes, WithTargetDirectory(environmentVariables, isolation.WorkspaceTestTargetDirectory), cancellationToken).ConfigureAwait(false),
            };
            foreach (string package in isolation.Packages)
            {
                results.Add(await RunOnceAsync(processRunner, manifestPath, packageRoot, new[] { "-p", package }, WithTargetDirectory(environmentVariables, isolation.TargetDirectoryFor(package)), cancellationToken).ConfigureAwait(false));
            }

            return new CargoTestBuildResult(
                results.All(result => result.Success),
                results.SelectMany(result => result.Binaries).ToList(),
                results.SelectMany(result => result.Diagnostics).ToList());
        }

        private static IReadOnlyDictionary<string, string> WithTargetDirectory(IReadOnlyDictionary<string, string>? environmentVariables, string targetDirectory)
        {
            var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (environmentVariables is not null)
            {
                foreach (var pair in environmentVariables)
                {
                    environment[pair.Key] = pair.Value;
                }
            }

            environment["CARGO_TARGET_DIR"] = targetDirectory;
            return environment;
        }

        private static async Task<CargoTestBuildResult> RunOnceAsync(
            IProcessRunner processRunner,
            string manifestPath,
            string packageRoot,
            IReadOnlyList<string> selection,
            IReadOnlyDictionary<string, string>? environmentVariables,
            CancellationToken cancellationToken)
        {
            CargoCommandLine commandLine = CargoCommand.Test()
                .WithManifestPath(manifestPath)
                .WithMessageFormat("json")
                // --no-fail-fast (cargo test's --keep-going): a crate that does not compile must not hide the tests of all the others.
                .WithExtraArgs(new[] { "--no-run", "--no-fail-fast" }.Concat(selection).ToArray())
                .ToCommandLine();

            var request = new ProcessRunRequest(commandLine.FileName, commandLine.Arguments)
            {
                WorkingDirectory = packageRoot,
                EnvironmentVariables = environmentVariables,
            };

            ProcessRunResult result = await processRunner
                .RunAsync(request, onOutput: null, cancellationToken)
                .ConfigureAwait(false);

            // Two passes over the same lines: CargoMessageParser for what it already models well
            // (diagnostics, build-finished), a small local parse for the one field it doesn't
            // (profile.test - see the class remarks above).
            var diagnostics = new List<CargoDiagnostic>();
            bool buildFinishedSuccess = result.Succeeded;
            foreach (CargoBuildEvent buildEvent in CargoMessageParser.ParseLines(result.StandardOutputLines, packageRoot))
            {
                switch (buildEvent)
                {
                    case CargoDiagnosticEvent diagnosticEvent:
                        diagnostics.Add(diagnosticEvent.Diagnostic);
                        break;
                    case CargoBuildFinishedEvent finishedEvent:
                        buildFinishedSuccess = finishedEvent.Success;
                        break;
                }
            }

            var binaries = new List<CargoTestBinary>();
            foreach (string line in result.StandardOutputLines)
            {
                CargoTestBinary? binary = TryParseTestArtifact(line, manifestPath, packageRoot);
                if (binary is not null)
                {
                    binaries.Add(binary);
                }
            }

            return new CargoTestBuildResult(buildFinishedSuccess, binaries, diagnostics);
        }

        private static CargoTestBinary? TryParseTestArtifact(string line, string manifestPath, string packageRoot)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return null;
            }

            RawTestArtifact? raw;
            try
            {
                raw = JsonSerializer.Deserialize<RawTestArtifact>(line, JsonOptions);
            }
            catch (JsonException)
            {
                // Not a JSON line at all (e.g. a stray nightly-feature notice on stdout): ignore,
                // matching CargoMessageParser's own tolerance for the same situation.
                return null;
            }

            if (raw is null
                || !string.Equals(raw.Reason, "compiler-artifact", StringComparison.Ordinal)
                || raw.Profile is null
                || !raw.Profile.Test
                || string.IsNullOrEmpty(raw.Executable)
                || raw.Target is null)
            {
                return null;
            }

            CargoTestBinaryKind? kind = ClassifyKind(raw.Target.Kind);
            if (kind is null)
            {
                // An unrecognized/unsupported target kind (e.g. a doctest pseudo-artifact, which
                // Cargo does not emit here since doctests are never listed by `--no-run`, or some
                // future target kind): skip rather than guess.
                return null;
            }

            string executablePath = raw.Executable!;
            string targetDirectory = ResolveTargetDirectory(executablePath);

            // A workspace-root container builds every member's tests: each runs from its own package's folder, as under
            // `cargo test` (tests that open files by relative path depend on it).
            string ownPackageRoot = !string.IsNullOrEmpty(raw.ManifestPath) ? Path.GetDirectoryName(raw.ManifestPath) ?? packageRoot : packageRoot;

            return new CargoTestBinary(
                raw.Target.Name,
                kind.Value,
                raw.Target.SrcPath,
                executablePath,
                Path.GetFullPath(manifestPath),
                ownPackageRoot,
                targetDirectory);
        }

        private static CargoTestBinaryKind? ClassifyKind(IReadOnlyList<string> rawKind)
        {
            // A library's unit tests, whatever its crate type: `crate-type = ["dylib"]` (the Kubuno desktop workspace's
            // kubuno-desktop-ui) reports "dylib" instead of "lib", a proc-macro crate "proc-macro".
            if (rawKind.Contains("lib") || rawKind.Contains("rlib") || rawKind.Contains("dylib") || rawKind.Contains("cdylib")
                || rawKind.Contains("staticlib") || rawKind.Contains("proc-macro"))
            {
                return CargoTestBinaryKind.Lib;
            }
            if (rawKind.Contains("test"))
            {
                return CargoTestBinaryKind.Integration;
            }
            if (rawKind.Contains("bin"))
            {
                return CargoTestBinaryKind.Bin;
            }
            if (rawKind.Contains("bench"))
            {
                return CargoTestBinaryKind.Bench;
            }
            return null;
        }

        /// <summary>
        /// Test-harness executables always land at `&lt;target-dir&gt;/[&lt;triple&gt;/]&lt;profile-dir&gt;/deps/&lt;file&gt;`
        /// (see Kubuno.Rust.Launch's `CargoLayout`/`ExecutableResolver` doc comments for the general
        /// shape) - walking up three directories from the executable recovers `&lt;target-dir&gt;`
        /// without needing a separate `cargo metadata` call. Host-triple builds only: a
        /// cross-compiled `--target &lt;triple&gt;` executable would need four levels, which this
        /// does not attempt to detect - a known limitation, see INTEGRATION.md.
        /// </summary>
        private static string ResolveTargetDirectory(string executablePath)
        {
            string depsDirectory = Path.GetDirectoryName(Path.GetFullPath(executablePath))
                ?? throw new InvalidOperationException($"Could not determine the directory of '{executablePath}'.");
            string profileDirectory = Path.GetDirectoryName(depsDirectory) ?? depsDirectory;
            return Path.GetDirectoryName(profileDirectory) ?? profileDirectory;
        }

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };

        // Raw, 1:1 shape of just the fields needed from a `cargo test --no-run
        // --message-format=json` "compiler-artifact" line - see the class remarks for why this
        // exists alongside CargoMessageParser instead of extending its model.
        private sealed class RawTestArtifact
        {
            public string? Reason { get; set; }

            public RawTarget? Target { get; set; }

            public RawProfile? Profile { get; set; }

            public string? Executable { get; set; }

            [JsonPropertyName("manifest_path")]
            public string? ManifestPath { get; set; }
        }

        private sealed class RawTarget
        {
            public string Name { get; set; } = string.Empty;

            public IReadOnlyList<string> Kind { get; set; } = Array.Empty<string>();

            [JsonPropertyName("src_path")]
            public string SrcPath { get; set; } = string.Empty;
        }

        private sealed class RawProfile
        {
            public bool Test { get; set; }
        }
    }
}
