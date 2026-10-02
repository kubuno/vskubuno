using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Kubuno.Shared.Mcp.Bridge.Discovery
{
    /// <summary>
    /// Reads and writes the per-instance discovery files under
    /// <c>%LOCALAPPDATA%\Kubuno\vs-mcp\&lt;pid&gt;.json</c>. Current-user only (no ACL widening,
    /// no network exposure - see docs/MCP.md "Security"): <c>LocalApplicationData</c> is already
    /// scoped to the signed-in user by Windows.
    /// </summary>
    public static class BridgeDiscoveryFile
    {
        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
        };

        public static string DiscoveryDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kubuno", "vs-mcp");

        public static string GetFilePath(int pid) => Path.Combine(DiscoveryDirectory, $"{pid}.json");

        /// <summary>
        /// Writes (or overwrites) this instance's discovery file. Not attempting cross-process
        /// atomicity here: a reader that races a write and gets a torn/partial file simply skips
        /// it in <see cref="TryRead"/> and retries on its next poll - acceptable for a
        /// best-effort, local discovery mechanism with no correctness-critical readers.
        /// </summary>
        public static void Write(BridgeDiscoveryInfo info)
        {
            if (info is null) throw new ArgumentNullException(nameof(info));

            Directory.CreateDirectory(DiscoveryDirectory);
            string json = JsonSerializer.Serialize(info, SerializerOptions);
            File.WriteAllText(GetFilePath(info.Pid), json);
        }

        /// <summary>Deletes this instance's discovery file, if present. Never throws (best-effort cleanup on bridge shutdown).</summary>
        public static void Delete(int pid)
        {
            try
            {
                File.Delete(GetFilePath(pid));
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        /// <summary>Parses one discovery file; returns <see langword="false"/> on any read/parse failure instead of throwing.</summary>
        public static bool TryRead(string filePath, out BridgeDiscoveryInfo? info)
        {
            try
            {
                string json = File.ReadAllText(filePath);
                info = JsonSerializer.Deserialize<BridgeDiscoveryInfo>(json);
                return info is not null && info.Pid > 0 && !string.IsNullOrEmpty(info.PipeName);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (JsonException)
            {
            }

            info = null;
            return false;
        }

        /// <summary>Every well-formed discovery file currently on disk. Does not check process liveness - callers decide that policy.</summary>
        public static IEnumerable<BridgeDiscoveryInfo> EnumerateAll()
        {
            if (!Directory.Exists(DiscoveryDirectory))
            {
                yield break;
            }

            foreach (string path in Directory.EnumerateFiles(DiscoveryDirectory, "*.json"))
            {
                if (TryRead(path, out BridgeDiscoveryInfo? info) && info is not null)
                {
                    yield return info;
                }
            }
        }
    }
}
