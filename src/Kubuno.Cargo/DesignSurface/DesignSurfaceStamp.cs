using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Kubuno.Cargo.DesignSurface
{
    /// <summary>
    /// <c>surface.json</c>, written last into a design surface folder (so its presence means "complete"):
    /// what the surface was compiled from. A design build whose inputs are unchanged (same files, same
    /// sizes and write times) is reused without running cargo or rustc.
    /// </summary>
    public sealed class DesignSurfaceStamp
    {
        public const string FileName = "surface.json";
        public const int CurrentVersion = 1;

        public int Version { get; set; } = CurrentVersion;

        /// <summary>The folder name under <c>kubuno-design\&lt;profile&gt;</c>.</summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>SHA-256 (upper-case hex) of the <c>kubuno_ui.dll</c> the surface was linked against, also embedded in the exe.</summary>
        public string UiDllSha256 { get; set; } = string.Empty;

        /// <summary>The project's <c>kubuno_ui.dll</c> it was copied from.</summary>
        public string UiDllSource { get; set; } = string.Empty;

        public string Rustc { get; set; } = string.Empty;

        public List<DesignSurfaceStampInput> Inputs { get; set; } = new List<DesignSurfaceStampInput>();

        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions { WriteIndented = true };

        public string ToJson() => JsonSerializer.Serialize(this, Options);

        public static DesignSurfaceStamp? TryParse(string json)
        {
            try
            {
                var stamp = JsonSerializer.Deserialize<DesignSurfaceStamp>(json);
                return stamp is { Version: CurrentVersion } && stamp.Key.Length > 0 && stamp.UiDllSha256.Length > 0 ? stamp : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>Whether every recorded input still has the recorded size and write time.</summary>
        public bool InputsUnchanged(Func<string, (long Length, long LastWriteUtcTicks)?> describe) =>
            Inputs.Count > 0 && Inputs.All(input => describe(input.Path) is { } now && now.Length == input.Length && now.LastWriteUtcTicks == input.LastWriteUtcTicks);

        public static (long Length, long LastWriteUtcTicks)? DescribeFile(string path)
        {
            try
            {
                var info = new FileInfo(path);
                return info.Exists ? (info.Length, info.LastWriteTimeUtc.Ticks) : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                return null;
            }
        }
    }

    /// <summary>One input file of a design build.</summary>
    public sealed class DesignSurfaceStampInput
    {
        public string Path { get; set; } = string.Empty;

        public long Length { get; set; }

        public long LastWriteUtcTicks { get; set; }
    }
}
