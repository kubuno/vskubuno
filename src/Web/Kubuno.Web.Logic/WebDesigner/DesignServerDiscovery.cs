using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Kubuno.Web.Logic.WebDesigner
{
    /// <summary>
    /// How Visual Studio finds a web project's running dev server (docs/WEB-VIEWS.md section 4.2, WV-9b): the
    /// <c>@kubuno/views-compiler</c> Vite plugin writes <c>&lt;root&gt;/.kubuno/design-server.json</c> when the dev server
    /// listens (<c>{version, urls, designPath, pid, root}</c>) and deletes it when it closes. The designer probes the page
    /// at each candidate address, local ones first; a stale file (dev server killed) simply fails the probe. Pure.
    /// </summary>
    public static class DesignServerDiscovery
    {
        /// <summary>The discovery file, relative to the project root.</summary>
        public const string FileRelativePath = ".kubuno/design-server.json";

        /// <summary>The path the plugin serves the design page at when the file does not say.</summary>
        public const string DefaultDesignPath = "/__kubuno_design__/";

        /// <summary>The discovery file of a project root.</summary>
        public static string FilePath(string projectRoot) => Path.Combine(projectRoot, ".kubuno", "design-server.json");

        /// <summary>
        /// The design page addresses a discovery file names, local (<c>localhost</c>, <c>127.0.0.1</c>, <c>[::1]</c>) first,
        /// each ending with the design path (<c>http://localhost:5173/__kubuno_design__/</c>); empty for a missing, malformed
        /// or unknown-version file. Only <c>http</c>/<c>https</c> addresses are kept.
        /// </summary>
        public static IReadOnlyList<Uri> CandidateUrls(string? discoveryJson)
        {
            if (string.IsNullOrWhiteSpace(discoveryJson))
            {
                return Array.Empty<Uri>();
            }

            try
            {
                using var document = JsonDocument.Parse(discoveryJson!);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object ||
                    (root.TryGetProperty("version", out var version) && (version.ValueKind != JsonValueKind.Number || version.GetInt32() != 1)))
                {
                    return Array.Empty<Uri>();
                }

                var designPath = root.TryGetProperty("designPath", out var p) && p.ValueKind == JsonValueKind.String && p.GetString() is { Length: > 0 } path
                    ? path
                    : DefaultDesignPath;
                if (!designPath.StartsWith("/", StringComparison.Ordinal))
                {
                    designPath = "/" + designPath;
                }

                if (!designPath.EndsWith("/", StringComparison.Ordinal))
                {
                    designPath += "/";
                }

                var urls = new List<Uri>();
                if (root.TryGetProperty("urls", out var list) && list.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in list.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String && Uri.TryCreate(item.GetString(), UriKind.Absolute, out var url) &&
                            (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps))
                        {
                            var page = new Uri(new Uri(url.GetLeftPart(UriPartial.Authority)), designPath);
                            if (!urls.Contains(page))
                            {
                                urls.Add(page);
                            }
                        }
                    }
                }

                return urls.OrderBy(u => IsLocal(u) ? 0 : 1).ToList();
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or UriFormatException)
            {
                return Array.Empty<Uri>();
            }
        }

        /// <summary>The dev server's process id the file records, when it does (a dead process means a stale file).</summary>
        public static int? ProcessId(string? discoveryJson)
        {
            if (string.IsNullOrWhiteSpace(discoveryJson))
            {
                return null;
            }

            try
            {
                using var document = JsonDocument.Parse(discoveryJson!);
                return document.RootElement.ValueKind == JsonValueKind.Object &&
                       document.RootElement.TryGetProperty("pid", out var pid) && pid.ValueKind == JsonValueKind.Number && pid.TryGetInt32(out var value)
                    ? value
                    : (int?)null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>The project root the file records (<c>root</c>: Vite's root, often the real path of a linked folder), or null.</summary>
        public static string? RootOf(string? discoveryJson)
        {
            if (string.IsNullOrWhiteSpace(discoveryJson))
            {
                return null;
            }

            try
            {
                using var document = JsonDocument.Parse(discoveryJson!);
                return document.RootElement.ValueKind == JsonValueKind.Object &&
                       document.RootElement.TryGetProperty("root", out var root) && root.ValueKind == JsonValueKind.String
                    ? root.GetString()
                    : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// Whether the server a discovery file announces serves <paramref name="projectRoot"/>: true when the file records no
        /// root, a root that is not a local Windows path (written on another machine: nothing to compare), or the same folder
        /// whatever its spelling (<paramref name="same"/>: <see cref="RealPaths.Same"/> - <c>C:\kubuno-build\x</c> through a
        /// junction and <c>E:\kubuno-build\x</c> are one folder). False for a file copied from another checkout.
        /// </summary>
        public static bool ServesRoot(string? discoveryJson, string projectRoot, Func<string, string, bool>? same = null)
        {
            var root = RootOf(discoveryJson);
            if (string.IsNullOrWhiteSpace(root) || !IsWindowsAbsolute(root!))
            {
                return true;
            }

            return (same ?? RealPaths.Same)(root!, projectRoot);
        }

        /// <summary>
        /// Whether a dev server's root is a link spelling (a junction, symbolic link or <c>subst</c> drive: its real path
        /// differs beyond case): Vite then cannot resolve the project's extensionless module URLs (<c>/src/x/View</c> answers
        /// the SPA's <c>index.html</c>, measured), so the design page loads no code-behind.
        /// </summary>
        public static bool StartedFromLink(string root, Func<string, string> canonical)
        {
            if (string.IsNullOrWhiteSpace(root) || !IsWindowsAbsolute(root))
            {
                return false;
            }

            var real = canonical(root);
            return !string.Equals(real.TrimEnd('\\', '/'), root.Replace('/', '\\').TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsWindowsAbsolute(string path) =>
            (path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/')) || path.StartsWith(@"\\", StringComparison.Ordinal);

        /// <summary>Whether <paramref name="url"/> names this machine.</summary>
        public static bool IsLocal(Uri url) =>
            url.IsLoopback || string.Equals(url.Host, "localhost", StringComparison.OrdinalIgnoreCase);

        /// <summary>The URL of the project information the design page and the designer read (<c>project.json</c> beside the page).</summary>
        public static Uri ProjectInfoUrl(Uri designPage) => new Uri(designPage, "project.json");
    }
}
