using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Kubuno.Web.Logic.DevCore
{
    /// <summary>
    /// The browser F5 opens on the dev core (docs/WEB.md, "F5"). Product owner's rule (2026-10-02): Google Chrome by
    /// default, Microsoft Edge only as a fallback when Chrome is not installed, then the system's default browser. The
    /// <c>KubunoBrowser</c> property (<c>chrome</c>, <c>edge</c>, <c>default</c>) chooses another one on purpose.
    /// </summary>
    public static class DevBrowser
    {
        /// <summary>The installed executables of a browser, in the usual Windows locations.</summary>
        public static IReadOnlyList<string> Candidates(string browser, Func<string, string?> environment)
        {
            if (environment is null)
            {
                throw new ArgumentNullException(nameof(environment));
            }

            string[] relative;
            string[] roots;
            switch ((browser ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "chrome":
                    relative = new[] { @"Google\Chrome\Application\chrome.exe" };
                    roots = new[] { "ProgramFiles", "ProgramFiles(x86)", "LOCALAPPDATA" };
                    break;
                case "edge":
                    relative = new[] { @"Microsoft\Edge\Application\msedge.exe" };
                    roots = new[] { "ProgramFiles(x86)", "ProgramFiles" };
                    break;
                default:
                    return Array.Empty<string>();
            }

            return roots.Select(environment)
                .Where(root => !string.IsNullOrWhiteSpace(root))
                .SelectMany(root => relative.Select(path => Path.Combine(root!, path)))
                .ToList();
        }

        /// <summary>
        /// The browser executable to start for <paramref name="preference"/> (empty means Chrome), or null for the
        /// system's default browser (<c>default</c>, or none of the preferred browsers installed).
        /// </summary>
        public static string? Resolve(string? preference, Func<string, string?> environment, Func<string, bool> exists)
        {
            if (exists is null)
            {
                throw new ArgumentNullException(nameof(exists));
            }

            var wanted = string.IsNullOrWhiteSpace(preference) ? "chrome" : preference!.Trim().ToLowerInvariant();
            if (wanted == "default")
            {
                return null;
            }

            // The wanted browser first; Chrome, then Edge as the fallbacks of any choice.
            foreach (var browser in new[] { wanted, "chrome", "edge" }.Distinct())
            {
                var found = Candidates(browser, environment).FirstOrDefault(exists);
                if (found is not null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
