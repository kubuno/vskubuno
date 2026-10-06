using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Kubuno.Rust.Cargo.Naming
{
    /// <summary>
    /// Where the parts of the core repository live, in either of its layouts (docs/WEB.md, "Solutions"):
    /// <list type="bullet">
    /// <item>the platform layout (2026-10): the server in <c>server/</c> (<c>server/Cargo.toml</c> is both the
    /// <c>kubuno-core</c> package and its own workspace), the shared server crates in <c>common/</c>, the host app and the npm
    /// packages in <c>web/</c> (<c>frontend/</c> until it moves), Kubuno Desktop in <c>desktop/</c> (one Cargo workspace
    /// over <c>desktop/common</c>, <c>windows</c>, <c>linux</c>, <c>macos</c>), <c>mobile/</c>;</item>
    /// <item>the former layout: a root workspace with <c>crates/kubuno-core</c> and the host app in <c>frontend/</c>.</item>
    /// </list>
    /// The one place that tells a core checkout apart from any other folder. netstandard2.0.
    /// </summary>
    public static class CoreRepository
    {
        private static readonly Regex PackageName = new Regex(@"^\s*name\s*=\s*""kubuno-core""", RegexOptions.Multiline | RegexOptions.CultureInvariant);

        /// <summary>Whether <paramref name="root"/> is a core checkout (either layout).</summary>
        public static bool IsCore(string? root, Func<string, bool>? fileExists = null, Func<string, string>? readFile = null) =>
            ServerManifest(root, fileExists, readFile) is not null;

        /// <summary>Whether <paramref name="root"/> is a core checkout in the platform layout (the server in <c>server/</c>).</summary>
        public static bool IsPlatformLayout(string? root, Func<string, bool>? fileExists = null, Func<string, string>? readFile = null)
        {
            var manifest = ServerManifest(root, fileExists, readFile);
            return manifest is not null && string.Equals(Path.GetFileName(Path.GetDirectoryName(manifest)), "server", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The <c>Cargo.toml</c> of the <c>kubuno-core</c> package: <c>server/Cargo.toml</c> (platform layout) or
        /// <c>crates/kubuno-core/Cargo.toml</c> (former layout); null when <paramref name="root"/> is not a core checkout.
        /// </summary>
        public static string? ServerManifest(string? root, Func<string, bool>? fileExists = null, Func<string, string>? readFile = null)
        {
            if (string.IsNullOrEmpty(root))
            {
                return null;
            }

            fileExists ??= File.Exists;
            readFile ??= File.ReadAllText;
            var server = Path.Combine(root, "server", "Cargo.toml");
            try
            {
                if (fileExists(server) && PackageName.IsMatch(readFile(server)))
                {
                    return server;
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                // Unreadable: not recognized by this rule.
            }

            var former = Path.Combine(root, "crates", "kubuno-core", "Cargo.toml");
            return fileExists(former) ? former : null;
        }

        /// <summary>The folder of the <c>kubuno-core</c> package (<c>server/</c>, or <c>crates/kubuno-core</c>), or null.</summary>
        public static string? ServerDirectory(string? root) => ServerManifest(root) is { } manifest ? Path.GetDirectoryName(manifest) : null;

        /// <summary>
        /// The Cargo workspace building the server: <c>server/</c> in the platform layout, the repository root before. Its
        /// <c>target</c> folder holds the built <c>kubuno-core.exe</c> (when <c>CARGO_TARGET_DIR</c> does not move it).
        /// </summary>
        public static string? ServerWorkspace(string? root)
        {
            var manifest = ServerManifest(root);
            if (manifest is null)
            {
                return null;
            }

            return IsPlatformLayout(root) ? Path.GetDirectoryName(manifest) : root;
        }

        /// <summary>The host app: <c>web/</c> when it holds a <c>package.json</c> (platform layout), else <c>frontend/</c>.</summary>
        public static string WebDirectory(string root, Func<string, bool>? fileExists = null)
        {
            fileExists ??= File.Exists;
            var web = Path.Combine(root, "web");
            return fileExists(Path.Combine(web, "package.json")) ? web : Path.Combine(root, "frontend");
        }

        /// <summary>The <c>@kubuno/*</c> npm packages: <c>web/packages</c>, else <c>frontend/packages</c>.</summary>
        public static string PackagesDirectory(string root, Func<string, bool>? fileExists = null) => Path.Combine(WebDirectory(root, fileExists), "packages");

        /// <summary>The themes shipped with the core: <c>web/themes</c> when it exists, else <c>themes/</c>.</summary>
        public static string ThemesDirectory(string root)
        {
            var web = Path.Combine(root, "web", "themes");
            return Directory.Exists(web) ? web : Path.Combine(root, "themes");
        }

        /// <summary>
        /// The core checkout holding <paramref name="directory"/>: the folder itself when it is one, else the nearest parent
        /// that is (a <c>server/</c> workspace root, a crate folder); null when there is none.
        /// </summary>
        public static string? RepositoryOf(string? directory)
        {
            for (var dir = directory; !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir))
            {
                if (IsCore(dir))
                {
                    return dir;
                }
            }

            return null;
        }

        /// <summary>
        /// Kubuno Desktop inside the core (<c>desktop/Cargo.toml</c>, the former kubuno/desktop repository merged on
        /// 2026-10-06), or null.
        /// </summary>
        public static string? DesktopWorkspace(string? root, Func<string, bool>? fileExists = null)
        {
            if (string.IsNullOrEmpty(root))
            {
                return null;
            }

            fileExists ??= File.Exists;
            var desktop = Path.Combine(root, "desktop");
            return fileExists(Path.Combine(desktop, "Cargo.toml")) ? desktop : null;
        }
    }
}
