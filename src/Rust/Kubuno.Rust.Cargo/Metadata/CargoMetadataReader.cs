using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Rust.Cargo.Commands;
using Kubuno.Rust.Cargo.Internal;
using Kubuno.Rust.Cargo.Processes;

namespace Kubuno.Rust.Cargo.Metadata
{
    /// <summary>Runs <c>cargo metadata --format-version 1</c> (by default with <c>--no-deps</c>) and parses its output.</summary>
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
        public Task<CargoMetadata> ReadAsync(
            string workingDirectory,
            string? manifestPath = null,
            IReadOnlyDictionary<string, string>? environmentVariables = null,
            CancellationToken cancellationToken = default)
            => ReadAsync(workingDirectory, manifestPath, CargoMetadataReadOptions.NoDependencies, environmentVariables, cancellationToken);

        /// <summary>
        /// Same, with the resolve graph when <see cref="CargoMetadataReadOptions.IncludeDependencies"/> is set
        /// (<see cref="CargoMetadata.Resolve"/>; cargo may then need the registry index, unless
        /// <see cref="CargoMetadataReadOptions.Offline"/> is set too).
        /// </summary>
        public async Task<CargoMetadata> ReadAsync(
            string workingDirectory,
            string? manifestPath,
            CargoMetadataReadOptions options,
            IReadOnlyDictionary<string, string>? environmentVariables = null,
            CancellationToken cancellationToken = default,
            string? filterPlatform = null)
        {
            if (workingDirectory is null)
            {
                throw new ArgumentNullException(nameof(workingDirectory));
            }

            var args = new List<string> { "metadata", "--format-version", "1" };
            if ((options & CargoMetadataReadOptions.IncludeDependencies) == 0)
            {
                args.Add("--no-deps");
            }

            if ((options & CargoMetadataReadOptions.Offline) != 0)
            {
                args.Add("--offline");
            }

            if (filterPlatform is not null)
            {
                // Only the dependencies built for this target triple (cfg(...) tables resolved by cargo).
                args.Add("--filter-platform");
                args.Add(filterPlatform);
            }
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
            return Parse(string.Join("\n", result.StandardOutputLines));
        }

        /// <summary>Parses the JSON <c>cargo metadata --format-version 1</c> printed.</summary>
        /// <exception cref="CargoMetadataException">The text is not valid metadata.</exception>
        public static CargoMetadata Parse(string json)
        {
            CargoMetadata? metadata;
            try
            {
                metadata = JsonSerializer.Deserialize<CargoMetadata>(json, CargoJsonOptions.Default);
            }
            catch (JsonException ex)
            {
                throw new CargoMetadataException(0, $"Could not parse `cargo metadata` output: {ex.Message}");
            }

            return metadata ?? throw new CargoMetadataException(0, "`cargo metadata` produced no output.");
        }
    }
}
