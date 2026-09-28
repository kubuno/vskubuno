using System;
using System.Linq;
using System.Xml.Linq;

namespace Kubuno.VisualStudio.Core.ProjectGeneration
{
    /// <summary>
    /// Merges a <c>&lt;packageSources&gt;</c> entry into a NuGet.Config's text (docs/RSPROJ.md
    /// work package 5's SDK-distribution question - see its own remarks: a generated
    /// <c>.rsproj</c>'s <c>Sdk="Kubuno.Rust.Sdk/…"</c> needs NuGet to actually find that package on
    /// a machine that never ran this repository's own <c>pack-sdk.ps1</c>/<c>samples/NuGet.config</c>
    /// dance). Pure text/XML manipulation - the same "surgical edit, preserve everything else"
    /// discipline as <see cref="RsprojSolutionGenerator"/> - rather than a dependency on the NuGet
    /// SDK's own (large, version-fragile) client libraries, mirroring exactly what
    /// <c>dotnet nuget add source</c> does to the file.
    /// </summary>
    public static class NuGetLocalFeedRegistration
    {
        /// <param name="existingConfigXml"><see langword="null"/> when no NuGet.Config exists yet at the target path.</param>
        /// <param name="sourceName">The packageSources <c>key</c> - also used to detect an already-registered entry (by key, not by path, so a developer's own rename sticks).</param>
        /// <param name="sourcePath">Absolute path of the local feed folder (containing the packed <c>Kubuno.Rust.Sdk.*.nupkg</c>).</param>
        /// <returns>The merged XML text, and whether it differs from <paramref name="existingConfigXml"/>.</returns>
        public static (string Content, bool Changed) Plan(string? existingConfigXml, string sourceName, string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourceName))
            {
                throw new ArgumentException("Source name must not be empty.", nameof(sourceName));
            }

            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                throw new ArgumentException("Source path must not be empty.", nameof(sourcePath));
            }

            var document = string.IsNullOrWhiteSpace(existingConfigXml)
                ? new XDocument(new XElement("configuration"))
                : XDocument.Parse(existingConfigXml!);

            var root = document.Root;
            if (root is null || root.Name.LocalName != "configuration")
            {
                // Not a NuGet.Config we understand - do not touch a file this pure function cannot
                // safely interpret; the caller decides what to do (e.g. leave it alone and log it).
                return (existingConfigXml ?? string.Empty, false);
            }

            var packageSources = root.Element("packageSources");
            if (packageSources is null)
            {
                packageSources = new XElement("packageSources");
                root.AddFirst(packageSources);
            }

            var existingEntry = packageSources.Elements("add")
                .FirstOrDefault(e => string.Equals((string?)e.Attribute("key"), sourceName, StringComparison.OrdinalIgnoreCase));

            if (existingEntry is not null)
            {
                if (string.Equals((string?)existingEntry.Attribute("value"), sourcePath, StringComparison.OrdinalIgnoreCase))
                {
                    // Already registered, pointing at the same place - nothing to do.
                    return (existingConfigXml ?? document.ToString(), false);
                }

                // The extension was reinstalled at a different location - repoint it.
                existingEntry.SetAttributeValue("value", sourcePath);
            }
            else
            {
                packageSources.Add(new XElement("add", new XAttribute("key", sourceName), new XAttribute("value", sourcePath)));
            }

            return (document.ToString(), true);
        }
    }
}
