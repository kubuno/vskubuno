using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Kubuno.VisualStudio.Core.ProjectProperties;
using Microsoft.Build.Framework.XamlTypes;
using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.Properties;

namespace Kubuno.VisualStudio.RustProjectSystem.ProjectProperties
{
    /// <summary>
    /// A fixed list of values computed when a page asks for it; any other typed value is accepted as is (custom
    /// strings of the list editors, a CargoBin not declared in Cargo.toml yet...).
    /// </summary>
    internal sealed class ListEnumValuesGenerator : IDynamicEnumValuesGenerator
    {
        private readonly ICollection<IEnumValue> _values;

        public ListEnumValuesGenerator(IEnumerable<(string Name, string DisplayName)> values)
        {
            _values = values.Select(v => (IEnumValue)new PageEnumValue(new EnumValue { Name = v.Name, DisplayName = v.DisplayName })).ToList();
        }

#pragma warning disable CS0618 // AllowCustomValues is obsolete but still part of the interface.
        public bool AllowCustomValues => true;
#pragma warning restore CS0618

        public Task<ICollection<IEnumValue>> GetListedValuesAsync() => Task.FromResult(_values);

        public Task<IEnumValue?> TryCreateEnumValueAsync(string userSuppliedValue) =>
            Task.FromResult<IEnumValue?>(new PageEnumValue(new EnumValue { Name = userSuppliedValue, DisplayName = userSuppliedValue }));
    }

    /// <summary>Base for the <c>.rsproj</c> enum providers: builds a <see cref="RustPropertyContext"/> of the configured project.</summary>
    internal abstract class RustEnumValuesProviderBase : IDynamicEnumValuesProvider
    {
        protected RustEnumValuesProviderBase(ConfiguredProject configuredProject)
        {
            ConfiguredProject = configuredProject;
        }

        protected ConfiguredProject ConfiguredProject { get; }

        public async Task<IDynamicEnumValuesGenerator> GetProviderAsync(IList<NameValuePair>? options) =>
            new ListEnumValuesGenerator(await GetValuesAsync().ConfigureAwait(false));

        protected abstract Task<IEnumerable<(string Name, string DisplayName)>> GetValuesAsync();

        protected async Task<RustPropertyContext> CreateContextAsync()
        {
            IProjectProperties properties = ConfiguredProject.Services.ProjectPropertiesProvider!.GetCommonProperties();
            string manifest = await properties.GetEvaluatedPropertyValueAsync("CargoManifestPath").ConfigureAwait(false);
            if (string.IsNullOrEmpty(manifest))
            {
                manifest = Path.Combine(Path.GetDirectoryName(ConfiguredProject.UnconfiguredProject.FullPath) ?? string.Empty, "Cargo.toml");
            }
            string bin = await properties.GetEvaluatedPropertyValueAsync("CargoBin").ConfigureAwait(false);
            string configuration = ConfiguredProject.ProjectConfiguration.Dimensions.TryGetValue("Configuration", out string? c) ? c : "Debug";
            return new RustPropertyContext(manifest, configuration, bin, CachedDiskPropertyFileReader.Instance);
        }
    }

    /// <summary>The package's binaries (Application page: default binary, binary to build and debug).</summary>
    [ExportDynamicEnumValuesProvider("KubunoRustBinaryTargets")]
    [AppliesTo(RustProjectCapabilities.RustProjectSystem)]
    internal sealed class RustBinaryTargetsEnumProvider : RustEnumValuesProviderBase
    {
        [ImportingConstructor]
        public RustBinaryTargetsEnumProvider(ConfiguredProject configuredProject) : base(configuredProject)
        {
        }

        protected override async Task<IEnumerable<(string Name, string DisplayName)>> GetValuesAsync()
        {
            RustPropertyContext context = await CreateContextAsync().ConfigureAwait(false);
            return new[] { (string.Empty, string.Empty) }.Concat(RustManifestProperties.GetBinaryTargets(context).Select(b => (b, b)));
        }
    }

    /// <summary>The package's features (Build page).</summary>
    [ExportDynamicEnumValuesProvider("KubunoRustFeatures")]
    [AppliesTo(RustProjectCapabilities.RustProjectSystem)]
    internal sealed class RustFeaturesEnumProvider : RustEnumValuesProviderBase
    {
        [ImportingConstructor]
        public RustFeaturesEnumProvider(ConfiguredProject configuredProject) : base(configuredProject)
        {
        }

        protected override async Task<IEnumerable<(string Name, string DisplayName)>> GetValuesAsync()
        {
            RustPropertyContext context = await CreateContextAsync().ConfigureAwait(false);
            return RustManifestProperties.GetFeatures(context).Select(f => (f, f));
        }
    }

    /// <summary>Cargo's library crate types (Application page).</summary>
    [ExportDynamicEnumValuesProvider("KubunoRustLibraryCrateTypes")]
    [AppliesTo(RustProjectCapabilities.RustProjectSystem)]
    internal sealed class RustLibraryCrateTypesEnumProvider : RustEnumValuesProviderBase
    {
        [ImportingConstructor]
        public RustLibraryCrateTypesEnumProvider(ConfiguredProject configuredProject) : base(configuredProject)
        {
        }

        protected override Task<IEnumerable<(string Name, string DisplayName)>> GetValuesAsync() =>
            Task.FromResult(new[] { "lib", "rlib", "dylib", "cdylib", "staticlib", "proc-macro" }.Select(t => (t, t)));
    }

    /// <summary>crates.io category slugs (Package page).</summary>
    [ExportDynamicEnumValuesProvider("KubunoRustCratesIoCategories")]
    [AppliesTo(RustProjectCapabilities.RustProjectSystem)]
    internal sealed class RustCratesIoCategoriesEnumProvider : RustEnumValuesProviderBase
    {
        [ImportingConstructor]
        public RustCratesIoCategoriesEnumProvider(ConfiguredProject configuredProject) : base(configuredProject)
        {
        }

        protected override Task<IEnumerable<(string Name, string DisplayName)>> GetValuesAsync() =>
            Task.FromResult(CratesIoCategories.All.Select(c => (c, c)));
    }

    /// <summary>No listed values: the free-text list editors (authors, keywords, cfg flags, patterns).</summary>
    [ExportDynamicEnumValuesProvider("KubunoRustEmpty")]
    [AppliesTo(RustProjectCapabilities.RustProjectSystem)]
    internal sealed class RustEmptyEnumProvider : RustEnumValuesProviderBase
    {
        [ImportingConstructor]
        public RustEmptyEnumProvider(ConfiguredProject configuredProject) : base(configuredProject)
        {
        }

        protected override Task<IEnumerable<(string Name, string DisplayName)>> GetValuesAsync() =>
            Task.FromResult(Enumerable.Empty<(string, string)>());
    }

    /// <summary>
    /// Target triples (Application page, "Target platform"): the host platform, then the targets whose standard
    /// library rustup has installed (<c>rustup target list --installed</c>, cached two minutes); "Install other
    /// targets..." adds more.
    /// </summary>
    [ExportDynamicEnumValuesProvider("KubunoRustTargetTriples")]
    [AppliesTo(RustProjectCapabilities.RustProjectSystem)]
    internal sealed class RustTargetTriplesEnumProvider : RustEnumValuesProviderBase
    {
        private static readonly object CacheLock = new object();
        private static (DateTime Time, IReadOnlyList<string> Targets)? _cache;

        [ImportingConstructor]
        public RustTargetTriplesEnumProvider(ConfiguredProject configuredProject) : base(configuredProject)
        {
        }

        /// <summary>Forgets the cached list (after targets were installed or removed).</summary>
        public static void Invalidate()
        {
            lock (CacheLock)
            {
                _cache = null;
            }
        }

        protected override async Task<IEnumerable<(string Name, string DisplayName)>> GetValuesAsync()
        {
            IReadOnlyList<string> installed = await Task.Run(GetInstalledTargets).ConfigureAwait(false);
            var values = new List<(string, string)> { (string.Empty, $"{TargetTriples.Host} ({PropertiesText.HostPlatform})") };
            values.AddRange(installed.Where(t => t != TargetTriples.Host).Select(t => (t, t)));
            return values;
        }

        private static IReadOnlyList<string> GetInstalledTargets()
        {
            lock (CacheLock)
            {
                if (_cache is { } cached && DateTime.UtcNow - cached.Time < TimeSpan.FromMinutes(2))
                {
                    return cached.Targets;
                }
            }

            var targets = RustupTargets.ListInstalled();
            lock (CacheLock)
            {
                _cache = (DateTime.UtcNow, targets);
            }
            return targets;
        }
    }

    /// <summary>Runs <c>rustup target</c> (list/add/remove). Never on the UI thread.</summary>
    internal static class RustupTargets
    {
        public static IReadOnlyList<string> ListInstalled() =>
            Run("target list --installed", out string output) == 0 ? SplitLines(output) : Array.Empty<string>();

        /// <summary>Every known target with its installed state, or null when rustup is missing.</summary>
        public static IReadOnlyList<(string Target, bool Installed)>? ListAll()
        {
            if (Run("target list", out string output) != 0)
            {
                return null;
            }
            return SplitLines(output)
                .Select(line => line.EndsWith(" (installed)", StringComparison.Ordinal)
                    ? (line.Substring(0, line.Length - " (installed)".Length).Trim(), true)
                    : (line.Trim(), false))
                .ToList();
        }

        /// <summary><c>rustup target add|remove &lt;targets&gt;</c>; returns the exit code and the combined output.</summary>
        public static int Change(bool add, IEnumerable<string> targets, out string output) =>
            Run($"target {(add ? "add" : "remove")} {string.Join(" ", targets)}", out output);

        private static IReadOnlyList<string> SplitLines(string text) =>
            text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

        private static int Run(string arguments, out string output)
        {
            try
            {
                var startInfo = new ProcessStartInfo("rustup", arguments)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using Process process = Process.Start(startInfo)!;
                Task<string> stdout = process.StandardOutput.ReadToEndAsync();
                Task<string> stderr = process.StandardError.ReadToEndAsync();
                process.WaitForExit();
                output = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
                return process.ExitCode;
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                output = ex.Message;
                return -1;
            }
        }
    }
}
