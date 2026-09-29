using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Kubuno.VisualStudio.Core.ProjectProperties;
using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.Properties;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;

namespace Kubuno.VisualStudio.RustProjectSystem.ProjectProperties
{
    /// <summary>
    /// Project-level properties of one configured <c>.rsproj</c> for the <c>KubunoRust</c> persistence
    /// (<see cref="RustProjectPropertiesProvider"/>). Values stored outside MSBuild come from the pure
    /// <see cref="RustManifestProperties"/> model; a handful of MSBuild properties get a value conversion for their
    /// list editors; every other name falls through to the project file.
    ///
    /// Invalid input is never written: the typed value is kept as a per-property "rejected" overlay whose evaluated
    /// form carries <see cref="RustManifestProperties.InvalidMarker"/>, so the page's validation regex shows its
    /// inline message under the field until a valid value is entered.
    /// </summary>
    internal sealed class RustProjectProperties : IProjectProperties
    {
        /// <summary>Properties whose value differs per configuration (all others must be identical across configurations).</summary>
        private static readonly HashSet<string> ConfigurationDependent = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CargoProfileName", "ProfileOptLevel", "ProfileDebug", "ProfileIncremental", "ProfileLto", "ProfileCodegenUnits",
            "ProfilePanic", "ProfileOverflowChecks", "ProfileDebugAssertions", "ProfileStrip",
            "CargoFeatures", "RustCfg", "CargoDefaultFeatures",
        };

        /// <summary>Names only shown by the pages (descriptions and links) - no value.</summary>
        private static readonly HashSet<string> Placeholders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "RustResourcesPageDescription", "CreateOrOpenKbres", "GoToWin32Resources", "RustSettingsPageDescription", "RustSettingsDesignNote",
        };

        private static readonly ConcurrentDictionary<string, string> RejectedValues = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The Cargo.toml and CargoBin of each project, as last read (writes must not read MSBuild, see <see cref="WriteAsync"/>).</summary>
        private static readonly ConcurrentDictionary<string, (string Manifest, string Bin)> KnownContexts = new ConcurrentDictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The configurations of each project whose values the editor has read.</summary>
        private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> KnownConfigurations = new ConcurrentDictionary<string, ConcurrentDictionary<string, byte>>(StringComparer.OrdinalIgnoreCase);

        private readonly IProjectProperties _msbuild;
        private readonly IProjectPropertiesProvider _userFileProvider;
        private readonly ConfiguredProject _configuredProject;
        private readonly IProjectThreadingService _threading;

        public RustProjectProperties(IProjectProperties msbuild, IProjectPropertiesProvider userFileProvider, ConfiguredProject configuredProject, IProjectThreadingService threading)
        {
            _msbuild = msbuild;
            _userFileProvider = userFileProvider;
            _configuredProject = configuredProject;
            _threading = threading;
        }

        public IProjectPropertiesContext Context => _msbuild.Context;

        public string FileFullPath => _msbuild.FileFullPath;

        public PropertyKind PropertyKind => _msbuild.PropertyKind;

        private string Configuration =>
            _configuredProject.ProjectConfiguration.Dimensions.TryGetValue("Configuration", out string? configuration) ? configuration : "Debug";

        private IProjectProperties UserFile => _userFileProvider.GetCommonProperties();

        public async Task<IEnumerable<string>> GetPropertyNamesAsync() =>
            (await _msbuild.GetPropertyNamesAsync().ConfigureAwait(false)).Concat(RustManifestProperties.Names).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        public Task<IEnumerable<string>> GetDirectPropertyNamesAsync() => _msbuild.GetDirectPropertyNamesAsync();

        public async Task<string> GetEvaluatedPropertyValueAsync(string propertyName)
        {
            try
            {
                string value = await GetEvaluatedCoreAsync(propertyName).ConfigureAwait(false);
                PropertiesLog.Write($"get {propertyName} [{Configuration}] = {value}");
                return value;
            }
            catch (Exception ex)
            {
                PropertiesLog.Write($"get {propertyName} failed: {ex}");
                throw;
            }
        }

        private async Task<string> GetEvaluatedCoreAsync(string propertyName)
        {
            if (RejectedValues.TryGetValue(RejectedKey(propertyName), out string? rejected))
            {
                return rejected + RustManifestProperties.InvalidMarker;
            }
            return (await GetValueAsync(propertyName).ConfigureAwait(false)).Evaluated;
        }

        public async Task<string?> GetUnevaluatedPropertyValueAsync(string propertyName)
        {
            try
            {
                return await GetUnevaluatedCoreAsync(propertyName).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                PropertiesLog.Write($"get unevaluated {propertyName} failed: {ex}");
                throw;
            }
        }

        private async Task<string?> GetUnevaluatedCoreAsync(string propertyName)
        {
            if (RejectedValues.TryGetValue(RejectedKey(propertyName), out string? rejected))
            {
                return rejected;
            }
            return (await GetValueAsync(propertyName).ConfigureAwait(false)).Unevaluated;
        }

        public Task<bool> IsValueInheritedAsync(string propertyName) =>
            IsOwned(propertyName) ? Task.FromResult(false) : _msbuild.IsValueInheritedAsync(propertyName);

        public Task DeleteDirectPropertiesAsync() => _msbuild.DeleteDirectPropertiesAsync();

        public async Task SetPropertyValueAsync(string propertyName, string unevaluatedPropertyValue, IReadOnlyDictionary<string, string>? dimensionalConditions = null)
        {
            PropertiesLog.Write($"set {propertyName} [{Configuration}] = {unevaluatedPropertyValue} (dimensions: {FormatDimensions(dimensionalConditions)})");
            switch (propertyName)
            {
                case "CargoFeatures":
                case "RustCfg":
                    await _msbuild.SetPropertyValueAsync(propertyName, string.Join(";", PropertyListEncoding.DecodeStrings(unevaluatedPropertyValue)), dimensionalConditions).ConfigureAwait(false);
                    return;
                case "CargoDefaultFeatures":
                    await _msbuild.SetPropertyValueAsync("CargoNoDefaultFeatures", string.Equals(unevaluatedPropertyValue, "false", StringComparison.OrdinalIgnoreCase) ? "true" : "false", dimensionalConditions).ConfigureAwait(false);
                    return;
                case "RustDebuggerEnvironmentVariables":
                    await UserFile.SetPropertyValueAsync("RustDebuggerEnvironment", PropertyListEncoding.PairsToEnvironmentLines(unevaluatedPropertyValue)).ConfigureAwait(false);
                    return;
            }

            if (Placeholders.Contains(propertyName) || IsComputed(propertyName))
            {
                return;
            }
            if (!RustManifestProperties.Handles(propertyName))
            {
                await _msbuild.SetPropertyValueAsync(propertyName, unevaluatedPropertyValue, dimensionalConditions).ConfigureAwait(false);
                return;
            }

            await WriteAsync(propertyName, dimensionalConditions, context => RustManifestProperties.Set(propertyName, unevaluatedPropertyValue, context), unevaluatedPropertyValue).ConfigureAwait(false);
        }

        public async Task DeletePropertyAsync(string propertyName, IReadOnlyDictionary<string, string>? dimensionalConditions = null)
        {
            PropertiesLog.Write($"delete {propertyName} [{Configuration}] (dimensions: {FormatDimensions(dimensionalConditions)})");
            switch (propertyName)
            {
                case "CargoFeatures":
                case "RustCfg":
                    await _msbuild.DeletePropertyAsync(propertyName, dimensionalConditions).ConfigureAwait(false);
                    return;
                case "CargoDefaultFeatures":
                    await _msbuild.DeletePropertyAsync("CargoNoDefaultFeatures", dimensionalConditions).ConfigureAwait(false);
                    return;
                case "RustDebuggerEnvironmentVariables":
                    await UserFile.DeletePropertyAsync("RustDebuggerEnvironment").ConfigureAwait(false);
                    return;
            }

            if (Placeholders.Contains(propertyName) || IsComputed(propertyName))
            {
                return;
            }
            if (!RustManifestProperties.Handles(propertyName))
            {
                await _msbuild.DeletePropertyAsync(propertyName, dimensionalConditions).ConfigureAwait(false);
                return;
            }

            await WriteAsync(propertyName, dimensionalConditions, context => RustManifestProperties.Reset(propertyName, context), rejectedValue: null).ConfigureAwait(false);
        }

        // ---------------------------------------------------------------------------------------------

        private static bool IsComputed(string name) =>
            name == "DependenciesSummary" || name == "TargetOperatingSystem";

        private static bool IsOwned(string name) =>
            RustManifestProperties.Handles(name) || Placeholders.Contains(name) || IsComputed(name) ||
            name == "CargoDefaultFeatures" || name == "RustDebuggerEnvironmentVariables";

        private string RejectedKey(string propertyName) =>
            FileFullPath + "|" + propertyName + (ConfigurationDependent.Contains(propertyName) ? "|" + Configuration : string.Empty);

        private async Task<PropertyValue> GetValueAsync(string propertyName)
        {
            switch (propertyName)
            {
                case "CargoFeatures":
                case "RustCfg":
                    return PropertyValue.Of(PropertyListEncoding.EncodeStrings(PropertyListEncoding.SplitMSBuildList(await _msbuild.GetEvaluatedPropertyValueAsync(propertyName).ConfigureAwait(false))));
                case "CargoDefaultFeatures":
                    string noDefault = await _msbuild.GetEvaluatedPropertyValueAsync("CargoNoDefaultFeatures").ConfigureAwait(false);
                    return PropertyValue.Of(string.Equals(noDefault, "true", StringComparison.OrdinalIgnoreCase) ? "false" : "true");
                case "RustDebuggerEnvironmentVariables":
                    return PropertyValue.Of(PropertyListEncoding.EnvironmentLinesToPairs(await UserFile.GetUnevaluatedPropertyValueAsync("RustDebuggerEnvironment").ConfigureAwait(false)));
                case "TargetOperatingSystem":
                    return PropertyValue.Of(TargetTriples.Describe(await _msbuild.GetEvaluatedPropertyValueAsync("CargoTargetTriple").ConfigureAwait(false)));
            }

            if (Placeholders.Contains(propertyName))
            {
                return PropertyValue.Empty;
            }
            if (propertyName == "DependenciesSummary")
            {
                RustPropertyContext? dependencies = await CreateContextAsync(CachedDiskPropertyFileReader.Instance, Configuration).ConfigureAwait(false);
                return PropertyValue.Of(dependencies is null ? string.Empty : PropertiesText.DependencySummary(RustManifestProperties.GetDependencyCounts(dependencies)));
            }
            if (!RustManifestProperties.Handles(propertyName))
            {
                string evaluated = await _msbuild.GetEvaluatedPropertyValueAsync(propertyName).ConfigureAwait(false);
                string? unevaluated = await _msbuild.GetUnevaluatedPropertyValueAsync(propertyName).ConfigureAwait(false);
                return new PropertyValue(unevaluated ?? string.Empty, evaluated);
            }

            RustPropertyContext? context = await CreateContextAsync(CachedDiskPropertyFileReader.Instance, Configuration).ConfigureAwait(false);
            return context is null ? PropertyValue.Empty : RustManifestProperties.Get(propertyName, context);
        }

        private async Task<RustPropertyContext?> CreateContextAsync(IPropertyFileReader reader, string configuration)
        {
            string manifestPath = await _msbuild.GetEvaluatedPropertyValueAsync("CargoManifestPath").ConfigureAwait(false);
            if (string.IsNullOrEmpty(manifestPath))
            {
                manifestPath = Path.Combine(Path.GetDirectoryName(FileFullPath) ?? string.Empty, "Cargo.toml");
            }
            string bin = await _msbuild.GetEvaluatedPropertyValueAsync("CargoBin").ConfigureAwait(false);
            KnownContexts[FileFullPath] = (manifestPath, bin);
            KnownConfigurations.GetOrAdd(FileFullPath, _ => new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase)).TryAdd(Configuration, 0);
            return new RustPropertyContext(manifestPath, configuration, bin, reader);
        }

        /// <summary>
        /// Queues a write (see <see cref="PropertyWriteQueue"/>). CPS calls <c>SetPropertyValueAsync</c> inside a
        /// project write lock, sometimes once per configuration in parallel forks of it: reading an MSBuild
        /// property from there ("dangerous read lock request from a fork of a write lock", seen live) or touching
        /// text buffers on the UI thread under the lock is not allowed. So everything the write needs from MSBuild
        /// comes from the values the editor has just read (<see cref="KnownContexts"/>), and the file edits run
        /// right after, outside the lock, one write at a time. The target profile of a configuration-dependent
        /// property comes from the dimensional conditions (CPS may ask ONE configured project to set another
        /// configuration's value); empty conditions mean every configuration.
        /// </summary>
        private Task WriteAsync(string propertyName, IReadOnlyDictionary<string, string>? dimensionalConditions, Func<RustPropertyContext, PropertyWrite> write, string? rejectedValue)
        {
            var configurations = new List<string> { Configuration };
            if (ConfigurationDependent.Contains(propertyName) && dimensionalConditions != null)
            {
                if (dimensionalConditions.TryGetValue("Configuration", out string? target) && !string.IsNullOrEmpty(target))
                {
                    configurations = new List<string> { target };
                }
                else if (KnownConfigurations.TryGetValue(FileFullPath, out ConcurrentDictionary<string, byte>? known) && known.Count > 0)
                {
                    configurations = known.Keys.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList();
                }
            }

            (string manifestPath, string bin) = KnownContexts.TryGetValue(FileFullPath, out var knownContext)
                ? knownContext
                : (Path.Combine(Path.GetDirectoryName(FileFullPath) ?? string.Empty, "Cargo.toml"), string.Empty);
            string rejectedKey = RejectedKey(propertyName);

            PropertyWriteQueue.Enqueue(() => ApplyWriteAsync(propertyName, configurations, manifestPath, bin, write, rejectedValue, rejectedKey));
            return Task.CompletedTask;
        }

        private async Task ApplyWriteAsync(string propertyName, List<string> configurations, string manifestPath, string bin, Func<RustPropertyContext, PropertyWrite> write, string? rejectedValue, string rejectedKey)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            string? oldPackageName = propertyName == "PackageName"
                ? RustManifestProperties.Get("PackageName", new RustPropertyContext(manifestPath, Configuration, bin, new BufferAwarePropertyFileReader())).Evaluated
                : null;

            bool wrote = false;
            foreach (string configuration in configurations)
            {
                var context = new RustPropertyContext(manifestPath, configuration, bin, new BufferAwarePropertyFileReader());
                PropertyWrite result = write(context);
                if (!result.IsValid)
                {
                    if (rejectedValue != null)
                    {
                        RejectedValues[rejectedKey] = rejectedValue;
                    }
                    Trace(propertyName, result.Error!);
                    // Refresh the editor so it reads the overlay back and shows the validation message.
                    await ProjectReevaluation.RequestAsync(_configuredProject);
                    return;
                }
                try
                {
                    PropertyTextBufferWriter.Apply(result.Edits);
                    wrote |= result.Edits.Count > 0;
                }
                catch (Exception ex) when (!Microsoft.VisualStudio.ErrorHandler.IsCriticalException(ex))
                {
                    PropertiesLog.Write($"write {propertyName} failed: {ex}");
                    ShowError(PropertiesText.PropertyWriteFailed(propertyName, ex.Message));
                    return;
                }
            }
            bool hadRejectedValue = RejectedValues.TryRemove(rejectedKey, out _);

            string? newPackageName = propertyName == "PackageName" && wrote
                ? RustManifestProperties.Get("PackageName", new RustPropertyContext(manifestPath, Configuration, bin, new BufferAwarePropertyFileReader())).Evaluated
                : null;

            await TaskScheduler.Default;
            if (!string.IsNullOrEmpty(oldPackageName) && !string.IsNullOrEmpty(newPackageName) && oldPackageName != newPackageName)
            {
                // Keep `cargo -p <name>` working: CargoPackage follows the rename when it named the old package.
                string cargoPackage = await _msbuild.GetEvaluatedPropertyValueAsync("CargoPackage");
                if (string.Equals(cargoPackage, oldPackageName, StringComparison.Ordinal))
                {
                    await _msbuild.SetPropertyValueAsync("CargoPackage", newPackageName!);
                }
            }
            if (wrote || hadRejectedValue)
            {
                await ProjectReevaluation.RequestAsync(_configuredProject);
            }
        }


        private static string FormatDimensions(IReadOnlyDictionary<string, string>? dimensions) =>
            dimensions is null ? "null" : string.Join(",", dimensions.Select(d => d.Key + "=" + d.Value));

        private static void Trace(string propertyName, string message) =>
            PropertiesLog.Write($"property '{propertyName}' rejected: {message}");

        private static void ShowError(string message)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            VsShellUtilities.ShowMessageBox(ServiceProvider.GlobalProvider, message, "Kubuno", OLEMSGICON.OLEMSGICON_WARNING, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }
    }
}
