using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Cargo.Commands;
using Kubuno.Cargo.Internal;
using Kubuno.Cargo.Processes;

namespace Kubuno.Cargo.Metadata
{
    /// <summary>Runs <c>cargo metadata --format-version 1 --no-deps</c> and parses its output.</summary>
    public sealed class CargoMetadataReader
    {
        private readonly IProcessRunner _processRunner;

        public CargoMetadataReader(IProcessRunner processRunner)
        {
            _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        }

        /// <param name="workingDirectory">
        /// Directory to run "cargo metadata" from; normally the workspace (or package) root.
        /// </param>
        /// <param name="manifestPath">Optional explicit path to a Cargo.toml, passed as <c>--manifest-path</c>.</param>
        /// <param name="environmentVariables">
        /// Extra/overriding environment variables for the child process — set
        /// <c>CARGO_TARGET_DIR</c> here to steer the reported <see cref="CargoMetadata.TargetDirectory"/>
        /// without touching the current process's own environment. When omitted, Cargo resolves
        /// it the normal way (inheriting whatever <c>CARGO_TARGET_DIR</c> the host process has set).
        /// </param>
        public async Task<CargoMetadata> ReadAsync(
            string workingDirectory,
            string? manifestPath = null,
            IReadOnlyDictionary<string, string>? environmentVariables = null,
            CancellationToken cancellationToken = default)
        {
            if (workingDirectory is null)
            {
                throw new ArgumentNullException(nameof(workingDirectory));
            }

            var args = new List<string> { "metadata", "--format-version", "1", "--no-deps" };
            if (manifestPath is not null)
            {
                args.Add("--manifest-path");
                args.Add(manifestPath);
            }

            var commandLine = new CargoCommandLine("cargo", args);
            var request = new ProcessRunRequest(commandLine.FileName, commandLine.Arguments)
            {
                WorkingDirectory = workingDirectory,
                EnvironmentVariables = environmentVariables,
            };

            ProcessRunResult result = await _processRunner
                .RunAsync(request, onOutput: null, cancellationToken)
                .ConfigureAwait(false);

            if (result.ExitCode != 0)
            {
                throw new CargoMetadataException(result.ExitCode, string.Join(Environment.NewLine, result.StandardErrorLines));
            }

            // `cargo metadata` prints a single line of compact JSON to stdout.
            string json = string.Join("\n", result.StandardOutputLines);

            CargoMetadata? metadata;
            try
            {
                metadata = JsonSerializer.Deserialize<CargoMetadata>(json, CargoJsonOptions.Default);
            }
            catch (JsonException ex)
            {
                throw new CargoMetadataException(result.ExitCode, $"Could not parse `cargo metadata` output: {ex.Message}");
            }

            return metadata ?? throw new CargoMetadataException(result.ExitCode, "`cargo metadata` produced no output.");
        }
    }
}
