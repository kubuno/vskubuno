using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.ProjectSystem;

namespace Kubuno.Rust.ProjectSystem.ProjectProperties
{
    /// <summary>
    /// Reports the configuration dimensions of a <c>.rsproj</c> (Configuration: Debug/Release, Platform: x64,
    /// declared as <c>ProjectConfiguration</c> items by Kubuno.Rust.Sdk) to the Project Query API. The Project
    /// Properties editor builds its configuration matrix from <see cref="IProjectConfigurationDimensionsProvider"/>
    /// exports (<c>ConfigurationDimensionDefinitionQueryDataProvider</c>, read from the installed CPS): CPS's own
    /// provider only applies to projects that infer their configurations from usage, and the .NET one only to .NET
    /// projects, so without this one the matrix is empty and every per-configuration property (the Build page)
    /// fails to load ("Expected 1 values ... but got 2").
    /// </summary>
    [Export(typeof(IProjectConfigurationDimensionsProvider))]
    [AppliesTo(RustProjectCapabilities.RustProjectSystem)]
    [Order(100)]
    internal sealed class RustConfigurationDimensionsProvider : IProjectConfigurationDimensionsProvider
    {
        public async Task<IEnumerable<KeyValuePair<string, IEnumerable<string>>>> GetProjectConfigurationDimensionsAsync(UnconfiguredProject project)
        {
            var configurations = new List<string>();
            var platforms = new List<string>();
            IProjectConfigurationsService? service = project.Services.ProjectConfigurationsService;
            if (service != null)
            {
                foreach (ProjectConfiguration configuration in await service.GetKnownProjectConfigurationsAsync().ConfigureAwait(false))
                {
                    AddDimension(configuration, "Configuration", configurations);
                    AddDimension(configuration, "Platform", platforms);
                }
            }
            if (configurations.Count == 0)
            {
                configurations.AddRange(new[] { "Debug", "Release" });
            }
            if (platforms.Count == 0)
            {
                platforms.Add("x64");
            }
            return new[]
            {
                new KeyValuePair<string, IEnumerable<string>>("Configuration", configurations),
                new KeyValuePair<string, IEnumerable<string>>("Platform", platforms),
            };
        }

        public Task<IEnumerable<KeyValuePair<string, string>>> GetDefaultValuesForDimensionsAsync(UnconfiguredProject project) =>
            Task.FromResult<IEnumerable<KeyValuePair<string, string>>>(new[]
            {
                new KeyValuePair<string, string>("Configuration", "Debug"),
                new KeyValuePair<string, string>("Platform", "x64"),
            });

        private static void AddDimension(ProjectConfiguration configuration, string dimension, List<string> values)
        {
            if (configuration.Dimensions.TryGetValue(dimension, out string? value) && value != null && !values.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                values.Add(value);
            }
        }
    }
}
