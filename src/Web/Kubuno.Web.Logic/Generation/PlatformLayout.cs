using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Kubuno.Web.Logic.Generation
{
    /// <summary>
    /// The platform layout of a module repository (docs/WEB.md, "Solutions"; decided 2026-10-06, drive first): a repository
    /// shows at a glance its common, server, web, desktop and mobile code -
    /// <c>common/&lt;crate&gt;</c> (the sans-IO rules shared by every client), <c>server/</c> (the module's Rust server, ex
    /// repository root), <c>web/</c> (ex <c>frontend/</c>), <c>desktop/</c> (one Cargo workspace, members under
    /// <c>desktop/common</c>, <c>desktop/windows</c>, <c>desktop/linux</c>, <c>desktop/macos</c>) and <c>mobile/</c>
    /// (<c>mobile/common</c>, <c>mobile/android</c>, <c>mobile/ios</c>), with <c>module.toml</c> at the root. Its solution
    /// gets one folder per part, in <see cref="FolderOrder"/>. Pure path rules: no project is read here.
    /// </summary>
    public static class PlatformLayout
    {
        public const string Common = "Common";

        public const string Server = "Server";

        public const string Web = "Web";

        public const string Desktop = "Desktop";

        public const string Mobile = "Mobile";

        /// <summary>The solution folders of the layout, in solution order (a nested folder after its parent).</summary>
        public static IReadOnlyList<string> FolderOrder { get; } = new[]
        {
            Common,
            Server,
            Web,
            Desktop,
            Desktop + "/Common",
            Desktop + "/Windows",
            Desktop + "/Linux",
            Desktop + "/macOS",
            Mobile,
            Mobile + "/Common",
            Mobile + "/Android",
            Mobile + "/iOS",
        };

        /// <summary>The folders of <c>desktop/</c> that hold desktop crates, in solution order.</summary>
        public static IReadOnlyList<string> DesktopPlatforms { get; } = new[] { "common", "windows", "linux", "macos" };

        /// <summary>The folders of <c>mobile/</c> that hold mobile projects, in solution order.</summary>
        public static IReadOnlyList<string> MobilePlatforms { get; } = new[] { "common", "android", "ios" };

        /// <summary>A platform folder's solution folder name (<c>macos</c> → <c>macOS</c>, <c>ios</c> → <c>iOS</c>).</summary>
        private static readonly IReadOnlyDictionary<string, string> PlatformNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["common"] = "Common",
            ["windows"] = "Windows",
            ["linux"] = "Linux",
            ["macos"] = "macOS",
            ["android"] = "Android",
            ["ios"] = "iOS",
        };

        /// <summary>Folders never scanned for a project: build outputs, dependencies, vendored crates.</summary>
        private static readonly HashSet<string> SkippedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "target", "obj", "bin", "node_modules", "dist", "vendor", "suppa", "TestResults",
        };

        private static readonly Regex WorkspaceTable = new Regex(@"^\s*\[workspace\]\s*(#.*)?$", RegexOptions.Multiline | RegexOptions.CultureInvariant);

        /// <summary>
        /// Whether <paramref name="root"/> (a module repository) uses the platform layout, wholly or partly (the transition):
        /// <c>server/Cargo.toml</c>, <c>web/package.json</c>, <c>desktop/Cargo.toml</c>, <c>common/Cargo.toml</c> or a
        /// <c>common/&lt;crate&gt;/Cargo.toml</c>. A repository with none of them keeps the former solution layout.
        /// </summary>
        public static bool IsPlatformLayout(string root)
        {
            if (File.Exists(Path.Combine(root, "server", "Cargo.toml"))
                || File.Exists(Path.Combine(root, "web", "package.json"))
                || File.Exists(Path.Combine(root, "desktop", "Cargo.toml"))
                || File.Exists(Path.Combine(root, "common", "Cargo.toml")))
            {
                return true;
            }

            return ChildDirectories(Path.Combine(root, "common")).Any(directory => File.Exists(Path.Combine(directory, "Cargo.toml")));
        }

        /// <summary>
        /// The solution folder (no slashes around it: <c>Desktop/Windows</c>) of a project in <paramref name="directory"/>:
        /// <c>common/</c> → Common; <c>server/</c> and, during the transition, a package still at the root → Server;
        /// <c>web/</c> (or a <c>frontend/</c> not moved yet) → Web; <c>desktop/&lt;platform&gt;/</c> → Desktop/&lt;Platform&gt;
        /// (Desktop for anything else under <c>desktop/</c>); <c>mobile/&lt;platform&gt;/</c> → Mobile/&lt;Platform&gt;.
        /// </summary>
        public static string FolderOf(string root, string directory)
        {
            var segments = RelativeSegments(root, directory);
            if (segments.Count == 0)
            {
                return Server;
            }

            var first = segments[0].ToLowerInvariant();
            switch (first)
            {
                case "common":
                    return Common;
                case "server":
                    return Server;
                case "web":
                case "frontend":
                case "packages":
                    return Web;
                case "desktop":
                    return segments.Count > 1 && DesktopPlatforms.Contains(segments[1].ToLowerInvariant()) ? Desktop + "/" + PlatformNames[segments[1]] : Desktop;
                case "mobile":
                    return segments.Count > 1 && MobilePlatforms.Contains(segments[1].ToLowerInvariant()) ? Mobile + "/" + PlatformNames[segments[1]] : Mobile;
                default:
                    return Server;
            }
        }

        /// <summary>The position of a folder of <see cref="FolderOrder"/> (an unknown folder goes last).</summary>
        public static int Rank(string folder)
        {
            var trimmed = (folder ?? string.Empty).Trim('/');
            for (var index = 0; index < FolderOrder.Count; index++)
            {
                if (string.Equals(FolderOrder[index], trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }
            }

            return FolderOrder.Count;
        }

        /// <summary>
        /// Whether a project folder may be listed: inside the repository (never a path outside it), and not in a build
        /// output, a dependency folder, a vendored crate or a hidden folder.
        /// </summary>
        public static bool IsListed(string root, string directory)
        {
            var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return RelativeSegments(root, directory).All(segment => !SkippedFolders.Contains(segment) && !segment.StartsWith(".", StringComparison.Ordinal));
        }

        /// <summary>The sub-folders of <paramref name="directory"/> that may hold projects (by name; none when it does not exist).</summary>
        public static IReadOnlyList<string> ChildDirectories(string directory)
        {
            if (!Directory.Exists(directory))
            {
                return Array.Empty<string>();
            }

            try
            {
                return Directory.GetDirectories(directory)
                    .Where(child => !SkippedFolders.Contains(Path.GetFileName(child)) && !Path.GetFileName(child).StartsWith(".", StringComparison.Ordinal))
                    .OrderBy(child => child, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return Array.Empty<string>();
            }
        }

        /// <summary>Whether the <c>Cargo.toml</c> of <paramref name="directory"/> declares a <c>[workspace]</c> table.</summary>
        public static bool IsWorkspace(string directory)
        {
            var manifest = Path.Combine(directory, "Cargo.toml");
            try
            {
                return File.Exists(manifest) && WorkspaceTable.IsMatch(File.ReadAllText(manifest));
            }
            catch (IOException)
            {
                return false;
            }
        }

        private static IReadOnlyList<string> RelativeSegments(string root, string directory)
        {
            var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            var full = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
            if (full.Length <= normalizedRoot.Length)
            {
                return Array.Empty<string>();
            }

            return full.Substring(normalizedRoot.Length).Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
        }
    }
}
