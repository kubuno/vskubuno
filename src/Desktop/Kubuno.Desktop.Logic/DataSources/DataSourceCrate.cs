using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Kubuno.Desktop.Logic.DataSources
{
    /// <summary>
    /// The Rust crate the Data Sources window works on: its manifest directory, package name, its data sources
    /// (<c>src/**/*.kbdata</c>) and - for a Kubuno server module (a <c>module.toml</c> next to the manifest) - the module's own
    /// database schema, which the wizard enforces (Kubuno rule: one schema per module). Pure file-system logic.
    /// </summary>
    public sealed class DataSourceCrate
    {
        private DataSourceCrate(string manifestDirectory, string packageName, string? moduleSchema)
        {
            ManifestDirectory = manifestDirectory;
            PackageName = packageName;
            ModuleSchema = moduleSchema;
        }

        public string ManifestDirectory { get; }

        public string ManifestPath => Path.Combine(ManifestDirectory, "Cargo.toml");

        public string PackageName { get; }

        /// <summary>The Kubuno module's schema (its <c>module.toml</c> <c>id</c>, e.g. <c>calendar</c>), null for any other crate.</summary>
        public string? ModuleSchema { get; }

        /// <summary><c>src/data</c> of the crate.</summary>
        public string DataDirectory => Path.Combine(ManifestDirectory, "src", "data");

        /// <summary>The crate's <c>.kbdata</c> files under <c>src</c> (sorted; <c>target</c> and hidden folders are skipped).</summary>
        public IReadOnlyList<string> KbdataFiles()
        {
            string src = Path.Combine(ManifestDirectory, "src");
            if (!Directory.Exists(src))
            {
                return Array.Empty<string>();
            }

            try
            {
                return Directory.EnumerateFiles(src, "*.kbdata", SearchOption.AllDirectories)
                    .Where(f => !f.Substring(src.Length).Split(Path.DirectorySeparatorChar).Any(part => part.StartsWith(".", StringComparison.Ordinal)))
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return Array.Empty<string>();
            }
        }

        /// <summary>The crate whose <c>Cargo.toml</c> (with a <c>[package]</c>) is <paramref name="path"/> or its nearest ancestor; null when none.</summary>
        public static DataSourceCrate? Find(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            string? directory;
            try
            {
                string full = Path.GetFullPath(path);
                directory = Directory.Exists(full) ? full : Path.GetDirectoryName(full);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is NotSupportedException || exception is PathTooLongException)
            {
                return null;
            }

            for (; !string.IsNullOrEmpty(directory); directory = Path.GetDirectoryName(directory))
            {
                string manifest = Path.Combine(directory, "Cargo.toml");
                if (!File.Exists(manifest))
                {
                    continue;
                }

                string text;
                try
                {
                    text = File.ReadAllText(manifest);
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                {
                    return null;
                }

                if (DataSourceCodeWriter.PackageName(text) is { } package)
                {
                    return new DataSourceCrate(directory!, package, ReadModuleSchema(directory!));
                }
            }

            return null;
        }

        /// <summary>The <c>id</c> of the <c>module.toml</c> in <paramref name="directory"/> (a Kubuno server module), null when there is none.</summary>
        public static string? ReadModuleSchema(string directory)
        {
            string path = Path.Combine(directory, "module.toml");
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                var match = Regex.Match(File.ReadAllText(path), @"^\s*id\s*=\s*""([A-Za-z_][A-Za-z0-9_]*)""", RegexOptions.Multiline);
                return match.Success ? match.Groups[1].Value : null;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
