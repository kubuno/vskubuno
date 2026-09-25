using System;
using System.Collections.Generic;

namespace Kubuno.Cargo.Commands
{
    /// <summary>
    /// Fluent builder for a Cargo invocation (build/check/test/clean/run). Building never
    /// touches the file system or spawns anything — <see cref="ToCommandLine"/> only computes
    /// the file name and argument string; running it is <see cref="Processes.IProcessRunner"/>'s job.
    /// </summary>
    public sealed class CargoCommand
    {
        private readonly List<string> _features = new List<string>();
        private readonly List<string> _extraArgs = new List<string>();
        private readonly List<string> _runArgs = new List<string>();

        private CargoCommand(CargoCommandKind kind)
        {
            Kind = kind;
        }

        public CargoCommandKind Kind { get; }

        public static CargoCommand Build() => new CargoCommand(CargoCommandKind.Build);

        public static CargoCommand Check() => new CargoCommand(CargoCommandKind.Check);

        public static CargoCommand Test() => new CargoCommand(CargoCommandKind.Test);

        public static CargoCommand Clean() => new CargoCommand(CargoCommandKind.Clean);

        public static CargoCommand Run() => new CargoCommand(CargoCommandKind.Run);

        /// <summary>Passed as <c>--manifest-path</c> when set (path to a specific Cargo.toml).</summary>
        public string? ManifestPath { get; set; }

        /// <summary>
        /// "debug" (the default, emits no flag), "release" (emits <c>--release</c>), or any
        /// other name, which is passed through as a custom <c>--profile &lt;name&gt;</c>.
        /// </summary>
        public string? Profile { get; set; }

        /// <summary>Passed as <c>-p &lt;package&gt;</c> when set.</summary>
        public string? Package { get; set; }

        /// <summary>Emits <c>--workspace</c> when true.</summary>
        public bool Workspace { get; set; }

        /// <summary>Which target(s) to build/run/test, e.g. <c>--bin foo</c>.</summary>
        public CargoTargetSelector? TargetSelector { get; set; }

        public bool AllFeatures { get; set; }

        public bool NoDefaultFeatures { get; set; }

        /// <summary>e.g. "json" or "json-diagnostic-rendered-ansi", emitted as <c>--message-format &lt;value&gt;</c>.</summary>
        public string? MessageFormat { get; set; }

        public IReadOnlyList<string> Features => _features;

        public IReadOnlyList<string> ExtraArgs => _extraArgs;

        /// <summary>Arguments forwarded to the built binary after "--" (only meaningful for <c>Run</c>).</summary>
        public IReadOnlyList<string> RunArgs => _runArgs;

        public CargoCommand WithManifestPath(string manifestPath)
        {
            ManifestPath = manifestPath;
            return this;
        }

        public CargoCommand WithProfile(string profile)
        {
            Profile = profile;
            return this;
        }

        public CargoCommand WithPackage(string package)
        {
            Package = package;
            return this;
        }

        public CargoCommand WithWorkspace(bool workspace = true)
        {
            Workspace = workspace;
            return this;
        }

        public CargoCommand WithTarget(CargoTargetSelector selector)
        {
            TargetSelector = selector;
            return this;
        }

        public CargoCommand WithFeature(string feature)
        {
            _features.Add(feature);
            return this;
        }

        public CargoCommand WithFeatures(IEnumerable<string> features)
        {
            _features.AddRange(features);
            return this;
        }

        public CargoCommand WithAllFeatures(bool allFeatures = true)
        {
            AllFeatures = allFeatures;
            return this;
        }

        public CargoCommand WithNoDefaultFeatures(bool noDefaultFeatures = true)
        {
            NoDefaultFeatures = noDefaultFeatures;
            return this;
        }

        public CargoCommand WithMessageFormat(string messageFormat)
        {
            MessageFormat = messageFormat;
            return this;
        }

        public CargoCommand WithExtraArgs(params string[] args)
        {
            _extraArgs.AddRange(args);
            return this;
        }

        /// <summary>Only meaningful for <see cref="CargoCommandKind.Run"/>: appended after "--".</summary>
        public CargoCommand WithRunArgs(params string[] args)
        {
            _runArgs.AddRange(args);
            return this;
        }

        public CargoCommandLine ToCommandLine()
        {
            var args = new List<string> { SubcommandName() };

            if (ManifestPath is not null)
            {
                args.Add("--manifest-path");
                args.Add(ManifestPath);
            }

            if (Package is not null)
            {
                args.Add("-p");
                args.Add(Package);
            }

            if (Workspace)
            {
                args.Add("--workspace");
            }

            TargetSelector?.AppendTo(args);

            AppendProfile(args);

            if (_features.Count > 0)
            {
                args.Add("--features");
                args.Add(string.Join(",", _features));
            }

            if (AllFeatures)
            {
                args.Add("--all-features");
            }

            if (NoDefaultFeatures)
            {
                args.Add("--no-default-features");
            }

            if (MessageFormat is not null)
            {
                args.Add("--message-format");
                args.Add(MessageFormat);
            }

            args.AddRange(_extraArgs);

            if (Kind == CargoCommandKind.Run && _runArgs.Count > 0)
            {
                args.Add("--");
                args.AddRange(_runArgs);
            }

            return new CargoCommandLine("cargo", args);
        }

        private void AppendProfile(List<string> args)
        {
            if (string.IsNullOrEmpty(Profile) || string.Equals(Profile, "debug", StringComparison.Ordinal))
            {
                return;
            }

            if (string.Equals(Profile, "release", StringComparison.Ordinal))
            {
                args.Add("--release");
                return;
            }

            args.Add("--profile");
            args.Add(Profile!);
        }

        private string SubcommandName()
        {
            switch (Kind)
            {
                case CargoCommandKind.Build:
                    return "build";
                case CargoCommandKind.Check:
                    return "check";
                case CargoCommandKind.Test:
                    return "test";
                case CargoCommandKind.Clean:
                    return "clean";
                case CargoCommandKind.Run:
                    return "run";
                default:
                    throw new ArgumentOutOfRangeException(nameof(Kind), Kind, "Unknown Cargo command kind.");
            }
        }
    }
}
