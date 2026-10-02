using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using Kubuno.Views.Designer.Registry.Serialization;

namespace Kubuno.Views.Designer.Registry
{
    /// <summary>
    /// The whole <c>kubuno/registry</c> response (docs/DESIGNER.md §5), loaded from a JSON string -
    /// today always a checked-in test fixture (tests/Kubuno.Desktop.Tests/Designer/Fixtures/
    /// registry.sample.json); once DSG-1/DSG-3 land, the real caller feeds this the language client's
    /// live JSON-RPC response instead (§5: "Query it live from kubuno-views-ls via kubuno/registry, not
    /// a JSON file generated at VSIX build time"), with no change needed here - this type only knows how
    /// to parse the shape, not where the JSON comes from.
    /// </summary>
    public sealed class ComponentRegistry
    {
        private readonly Dictionary<string, ComponentMeta> _byName;

        private ComponentRegistry(IReadOnlyList<ComponentMeta> components)
        {
            Components = components;
            _byName = new Dictionary<string, ComponentMeta>(StringComparer.Ordinal);

            // Families keep first-seen order, mirroring registry::families::ALL_FAMILIES's own fixed
            // grouping order (§5) rather than sorting alphabetically, so a "Core" family stays first if
            // the export lists it first.
            var families = new Dictionary<string, List<ComponentMeta>>(StringComparer.Ordinal);
            var familyOrder = new List<string>();

            foreach (var component in components)
            {
                _byName[component.Name] = component;

                if (!families.TryGetValue(component.Family, out var list))
                {
                    list = new List<ComponentMeta>();
                    families[component.Family] = list;
                    familyOrder.Add(component.Family);
                }

                list.Add(component);
            }

            Families = new ReadOnlyDictionary<string, IReadOnlyList<ComponentMeta>>(
                familyOrder.ToDictionary(f => f, f => (IReadOnlyList<ComponentMeta>)families[f], StringComparer.Ordinal));
            FamilyNames = familyOrder;
        }

        public IReadOnlyList<ComponentMeta> Components { get; }

        /// <summary>Family name, in first-seen order - see the constructor's own comment.</summary>
        public IReadOnlyList<string> FamilyNames { get; }

        public IReadOnlyDictionary<string, IReadOnlyList<ComponentMeta>> Families { get; }

        public ComponentMeta? Find(string name) => _byName.TryGetValue(name, out var component) ? component : null;

        /// <summary>The export's <c>version</c> (a hash of its content), when known.</summary>
        public string? Version { get; set; }

        /// <summary>The export entries of the project's own controls (EVT-7b) as a JSON array, sent to the design surface (<c>projectComponents</c>).</summary>
        public string ProjectComponentsJson { get; set; } = "[]";

        /// <summary>The project's own controls (EVT-7b).</summary>
        public IEnumerable<ComponentMeta> ProjectComponents => Components.Where(c => c.IsProject);

        public static ComponentRegistry FromJson(string json)
        {
            if (json is null)
            {
                throw new ArgumentNullException(nameof(json));
            }

            var components = JsonSerializer.Deserialize<List<ComponentMeta>>(json, RegistryJsonOptions.Default)
                ?? new List<ComponentMeta>();
            return new ComponentRegistry(components);
        }

        /// <summary>An empty registry - used while waiting for the real <c>kubuno/registry</c> round trip, or in tests that don't care about component data.</summary>
        public static ComponentRegistry Empty { get; } = new ComponentRegistry(Array.Empty<ComponentMeta>());
    }
}
