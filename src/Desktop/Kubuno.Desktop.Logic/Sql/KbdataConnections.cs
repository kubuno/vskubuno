using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kubuno.Cargo.Toml;

namespace Kubuno.VisualStudio.Core.Sql
{
    /// <summary>A connection a <c>.kbdata</c> file names (its top-level <c>connection</c> and <c>provider</c> keys).</summary>
    public sealed class KbdataConnection
    {
        public KbdataConnection(string connection, string? provider, string filePath)
        {
            Connection = connection;
            Provider = provider;
            FilePath = filePath;
        }

        public string Connection { get; }

        public string? Provider { get; }

        public string FilePath { get; }
    }

    /// <summary>Which connections a Rust crate's SQL talks to: the ones its <c>src\**\*.kbdata</c> data sources name.</summary>
    public static class KbdataConnections
    {
        /// <summary>The directory of the nearest <c>Cargo.toml</c> above <paramref name="filePath"/>, or null.</summary>
        public static string? FindCrateDirectory(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return null;
            }

            try
            {
                for (var directory = Path.GetDirectoryName(Path.GetFullPath(filePath)); !string.IsNullOrEmpty(directory); directory = Path.GetDirectoryName(directory))
                {
                    if (File.Exists(Path.Combine(directory, "Cargo.toml")))
                    {
                        return directory;
                    }
                }
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return null;
            }

            return null;
        }

        /// <summary>The connections of the crate's data sources, distinct by name (first file wins), in path order.</summary>
        public static IReadOnlyList<KbdataConnection> Read(string crateDirectory)
        {
            var source = Path.Combine(crateDirectory, "src");
            var result = new List<KbdataConnection>();
            IEnumerable<string> files;
            try
            {
                files = Directory.Exists(source)
                    ? Directory.EnumerateFiles(source, "*.kbdata", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList()
                    : Enumerable.Empty<string>();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return result;
            }

            foreach (var file in files)
            {
                string text;
                try
                {
                    text = File.ReadAllText(file);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                var connection = Parse(text, file);
                if (connection != null && !result.Any(c => string.Equals(c.Connection, connection.Connection, StringComparison.OrdinalIgnoreCase)))
                {
                    result.Add(connection);
                }
            }

            return result;
        }

        /// <summary>The <c>connection</c>/<c>provider</c> of a <c>.kbdata</c> text, or null (no connection, not TOML).</summary>
        public static KbdataConnection? Parse(string tomlText, string filePath)
        {
            try
            {
                var document = TomlDocument.Parse(tomlText);
                var connection = document.GetValue("connection")?.AsString();
                if (string.IsNullOrWhiteSpace(connection))
                {
                    return null;
                }

                return new KbdataConnection(connection!.Trim(), document.GetValue("provider")?.AsString(), filePath);
            }
            catch (TomlParseException)
            {
                return null;
            }
        }
    }
}
