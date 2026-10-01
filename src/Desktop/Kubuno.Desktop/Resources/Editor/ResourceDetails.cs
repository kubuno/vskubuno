using System.Collections.Generic;
using System.Globalization;
using Kubuno.Desktop.Logic.Resources;

namespace Kubuno.Desktop.Resources.Editor
{
    /// <summary>The facts the editor shows about a binary entry (tooltip, details line).</summary>
    public sealed class ResourceDetails
    {
        private ResourceDetails(string kind, string format, long? size, bool embedded, string path, string comment)
        {
            Kind = kind;
            Format = format;
            Size = size;
            Embedded = embedded;
            Path = path;
            Comment = comment;
        }

        public string Kind { get; }

        public string Format { get; }

        /// <summary>Bytes, or null when the linked file cannot be read.</summary>
        public long? Size { get; }

        public bool Embedded { get; }

        public string Path { get; }

        public string Comment { get; }

        public static ResourceDetails Of(ResourceSetModel model, ResourceEntry entry)
        {
            var bytes = model.BytesOf(entry.Name);
            return new ResourceDetails(
                ResourceText.KindName(entry.Kind),
                entry.EffectiveFormat,
                bytes?.LongLength,
                entry.Persistence == ResourcePersistence.Embedded,
                entry.Persistence == ResourcePersistence.Linked ? entry.Path : string.Empty,
                entry.Comment ?? string.Empty);
        }

        public static string FormatSize(long bytes)
        {
            if (bytes < 1024)
            {
                return bytes.ToString(CultureInfo.CurrentCulture) + " B";
            }

            return bytes < 1024 * 1024
                ? (bytes / 1024.0).ToString("0.#", CultureInfo.CurrentCulture) + " KB"
                : (bytes / (1024.0 * 1024.0)).ToString("0.#", CultureInfo.CurrentCulture) + " MB";
        }

        /// <summary>Multi-line text: one fact per line (a tooltip).</summary>
        public string ToText(string name)
        {
            var lines = new List<string>
            {
                name,
                ResourceText.DetailKind + ": " + Kind + (Format.Length > 0 ? " (" + Format + ")" : string.Empty),
                ResourceText.DetailSize + ": " + (Size is { } s ? FormatSize(s) : ResourceText.DetailMissingFile),
                ResourceText.DetailPersistence + ": " + ResourceText.PersistenceName(Embedded),
            };
            if (Path.Length > 0)
            {
                lines.Add(ResourceText.DetailPath + ": " + Path);
            }

            if (Comment.Length > 0)
            {
                lines.Add(ResourceText.DetailComment + ": " + Comment);
            }

            return string.Join("\n", lines);
        }
    }
}
