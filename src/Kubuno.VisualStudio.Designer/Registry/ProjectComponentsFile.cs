using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kubuno.VisualStudio.Views.Logging;
using Newtonsoft.Json.Linq;

namespace Kubuno.VisualStudio.Designer.Registry
{
    /// <summary>
    /// The registry a project's design surface exports after a design build (docs/EVENTS.md EVT-7b,
    /// <c>--export-registry</c>, saved as <c>registry.json</c> next to it): its project controls marked <c>linked</c>
    /// are the ones compiled into the surface - what the Toolbox's "&lt;Project&gt; Composants" tab lists.
    /// </summary>
    public static class ProjectComponentsFile
    {
        /// <summary>The linked project controls of the export at <paramref name="registryPath"/> (none when unreadable).</summary>
        public static IReadOnlyList<ComponentMeta> ReadLinked(string registryPath)
        {
            try
            {
                var root = JToken.Parse(File.ReadAllText(registryPath));
                var components = root["components"] as JArray ?? new JArray();
                var project = new JArray(components.Where(c => (string?)c["origin"] == "project" && (bool?)c["linked"] == true));
                return ComponentRegistry.FromJson(project.ToString(Newtonsoft.Json.Formatting.None)).Components;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Newtonsoft.Json.JsonException or System.Text.Json.JsonException or ArgumentException)
            {
                KubunoViewsLogHost.Current.WriteLine("[designer] no registry of the design build at " + registryPath + ": " + ex.Message);
                return Array.Empty<ComponentMeta>();
            }
        }

        /// <summary>A control's key in the Toolbox choices ("Choisir des éléments…"): <c>crate::Name</c>.</summary>
        public static string ChoiceKey(ComponentMeta component) => (component.CrateName ?? string.Empty) + "::" + component.Name;
    }
}
