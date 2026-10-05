using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Kubuno.Web.Logic.WebDesigner
{
    /// <summary>
    /// A web project that uses <c>.kbview</c> views, seen from Visual Studio (docs/WEB-VIEWS.md sections 4.2 and 5): where its
    /// root, its element registries and its view folders are - the C# twin of <c>@kubuno/views-compiler</c>'s
    /// <c>ViewProject</c> (<c>project.ts</c>), which the design surface must agree with to the path. A view belongs to a web
    /// project when, walking up from its folder, the first project marker found is a <c>kubuno.views.json</c> (unless it
    /// says <c>"target": "desktop"</c>), a <c>package.json</c> depending on <c>@kubuno/views-compiler</c>, or a JavaScript
    /// project (<c>*.esproj</c>); a <c>Cargo.toml</c> naming <c>kubuno-desktop</c> found first means a desktop view - the
    /// profile rule of <c>kubuno-views-ls</c> (docs/WEB-VIEWS.md section 5). Pure: the file system is reached through
    /// <see cref="IWebProjectFiles"/>.
    /// </summary>
    public sealed class WebViewProject
    {
        /// <summary>The project configuration file (VIEWS-SPEC section 1).</summary>
        public const string ConfigFileName = "kubuno.views.json";

        /// <summary>The compiler package whose presence in <c>package.json</c> makes a project a web views project.</summary>
        public const string CompilerPackage = "@kubuno/views-compiler";

        /// <summary>Folders never scanned for views (the same list as <c>project.ts</c>).</summary>
        private static readonly HashSet<string> SkipDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "node_modules", ".kubuno", ".git", "dist", "obj", "bin", "target", "coverage",
        };

        private readonly IWebProjectFiles _files;

        private WebViewProject(string root, string? hostRegistryPath, IReadOnlyList<string> registryPaths, IReadOnlyList<string> sources, string? designEntry, IWebProjectFiles files)
        {
            Root = root;
            HostRegistryPath = hostRegistryPath;
            RegistryPaths = registryPaths;
            Sources = sources;
            DesignEntry = designEntry;
            _files = files;
        }

        /// <summary>The project root (the folder of <c>kubuno.views.json</c> or <c>package.json</c>).</summary>
        public string Root { get; }

        /// <summary>The host element registry (<c>@kubuno/ui</c>'s <c>kbview-registry.web.json</c>), absolute; null when none was found.</summary>
        public string? HostRegistryPath { get; }

        /// <summary>The project's own registries (custom controls), absolute.</summary>
        public IReadOnlyList<string> RegistryPaths { get; }

        /// <summary>The folders scanned for views, absolute.</summary>
        public IReadOnlyList<string> Sources { get; }

        /// <summary>The design entry of <c>kubuno.views.json</c> (<c>design.entry</c>), project-relative, when set.</summary>
        public string? DesignEntry { get; }

        /// <summary>
        /// The web project <paramref name="viewPath"/> belongs to, or null (a desktop view, a view outside any project, an
        /// unreadable configuration). Never throws for a missing or malformed file.
        /// </summary>
        public static WebViewProject? Find(string? viewPath, IWebProjectFiles? files = null)
        {
            files ??= PhysicalWebProjectFiles.Instance;
            if (string.IsNullOrEmpty(viewPath))
            {
                return null;
            }

            string? dir;
            try
            {
                dir = Path.GetDirectoryName(Path.GetFullPath(viewPath));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return null;
            }

            while (!string.IsNullOrEmpty(dir))
            {
                var config = Path.Combine(dir, ConfigFileName);
                if (files.Exists(config))
                {
                    return FromConfig(dir!, files.ReadText(config), files);
                }

                var package = Path.Combine(dir, "package.json");
                if ((files.Exists(package) && DependsOnCompiler(files.ReadText(package))) ||
                    files.Files(dir!).Any(f => f.EndsWith(".esproj", StringComparison.OrdinalIgnoreCase)))
                {
                    return Create(dir!, null, Array.Empty<string>(), null, null, files);
                }

                var cargo = Path.Combine(dir, "Cargo.toml");
                if (files.Exists(cargo) && (files.ReadText(cargo) ?? string.Empty).IndexOf("kubuno-desktop", StringComparison.Ordinal) >= 0)
                {
                    return null;
                }

                var parent = Path.GetDirectoryName(dir);
                if (string.Equals(parent, dir, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                dir = parent;
            }

            return null;
        }

        /// <summary><paramref name="path"/> relative to <see cref="Root"/>, <c>/</c>-separated (<c>src/core/shell/menus/WaffleMenu.kbcontrol</c>).</summary>
        public string RelativePath(string path) => ToPosix(MakeRelative(Root, path));

        /// <summary>
        /// The code-behind of a view as the compiler sees it: the same-stem <c>.ts</c>, else <c>.tsx</c> (<c>codeBehindOf</c> of
        /// <c>project.ts</c>); null when the view has none.
        /// </summary>
        public string? CodeBehindOf(string viewPath)
        {
            var stem = StripViewExtension(viewPath);
            foreach (var extension in new[] { ".ts", ".tsx" })
            {
                if (_files.Exists(stem + extension))
                {
                    return stem + extension;
                }
            }

            return null;
        }

        /// <summary>The code-behind as imported from the view's folder (<c>./WaffleMenu</c>), or null.</summary>
        public string? CodeBehindModule(string viewPath) =>
            CodeBehindOf(viewPath) is { } code ? "./" + Path.GetFileNameWithoutExtension(code) : null;

        /// <summary>The class a view's code-behind exports: the file stem (<c>WaffleMenu</c>).</summary>
        public static string ClassNameOf(string viewPath) => Path.GetFileName(StripViewExtension(viewPath));

        /// <summary>The design data of a view (docs/WEB-VIEWS.md section 4.6): <c>&lt;view&gt;.design.json</c> beside it.</summary>
        public static string DesignDataPath(string viewPath) => viewPath + ".design.json";

        /// <summary>
        /// The project's user controls (one per <c>.kbcontrol</c> under <see cref="Sources"/>): name = file stem, module =
        /// its code-behind (else the view) without extension, project-root-relative with a leading <c>/</c> - exactly
        /// <c>userControlsOf</c> of <c>project.ts</c>. Sorted by path.
        /// </summary>
        public IReadOnlyList<WebUserControl> UserControls()
        {
            var views = new List<string>();
            foreach (var source in Sources)
            {
                Collect(source, views);
            }

            views.Sort(StringComparer.Ordinal);
            return views
                .Where(v => v.EndsWith(".kbcontrol", StringComparison.OrdinalIgnoreCase))
                .Select(v =>
                {
                    var module = CodeBehindOf(v) is { } code ? code.Substring(0, code.Length - Path.GetExtension(code).Length) : v;
                    return new WebUserControl(Path.GetFileName(StripViewExtension(v)), "/" + RelativePath(module));
                })
                .ToList();
        }

        private void Collect(string dir, List<string> views)
        {
            foreach (var file in _files.Files(dir))
            {
                var name = Path.GetFileName(file);
                if (name.EndsWith(".kbview", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".kbcontrol", StringComparison.OrdinalIgnoreCase))
                {
                    views.Add(file);
                }
            }

            foreach (var sub in _files.Directories(dir))
            {
                if (!SkipDirs.Contains(Path.GetFileName(sub)))
                {
                    Collect(sub, views);
                }
            }
        }

        private static WebViewProject? FromConfig(string root, string? text, IWebProjectFiles files)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return Create(root, null, Array.Empty<string>(), null, null, files);
            }

            try
            {
                using var document = JsonDocument.Parse(text!, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                var config = document.RootElement;
                if (config.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                if (config.TryGetProperty("target", out var target) && target.ValueKind == JsonValueKind.String && target.GetString() != "web")
                {
                    return null;
                }

                var host = config.TryGetProperty("hostRegistry", out var h) && h.ValueKind == JsonValueKind.String ? h.GetString() : null;
                var registries = Strings(config, "registries");
                var sources = config.TryGetProperty("sources", out _) ? Strings(config, "sources") : null;
                string? designEntry = null;
                if (config.TryGetProperty("design", out var design) && design.ValueKind == JsonValueKind.Object &&
                    design.TryGetProperty("entry", out var entry) && entry.ValueKind == JsonValueKind.String)
                {
                    designEntry = entry.GetString();
                }

                return Create(root, host, registries, sources, designEntry, files);
            }
            catch (JsonException)
            {
                // The web compiler's configuration, unreadable: still a web project, with the defaults.
                return Create(root, null, Array.Empty<string>(), null, null, files);
            }
        }

        private static WebViewProject Create(string root, string? hostOption, IReadOnlyList<string> registries, IReadOnlyList<string>? sources, string? designEntry, IWebProjectFiles files)
        {
            var host = !string.IsNullOrEmpty(hostOption) ? Path.GetFullPath(Path.Combine(root, hostOption!)) : FindHostRegistry(root, files);
            if (host is not null && !files.Exists(host))
            {
                host = null;
            }

            return new WebViewProject(
                root,
                host,
                registries.Select(r => Path.GetFullPath(Path.Combine(root, r))).ToList(),
                (sources is { Count: > 0 } ? sources : new[] { "src" }).Select(s => Path.GetFullPath(Path.Combine(root, s))).ToList(),
                designEntry,
                files);
        }

        /// <summary><c>node_modules/@kubuno/ui/kbview-registry.web.json</c>, searched upwards from <paramref name="root"/> (<c>findHostRegistry</c>).</summary>
        private static string? FindHostRegistry(string root, IWebProjectFiles files)
        {
            var dir = root;
            while (!string.IsNullOrEmpty(dir))
            {
                var candidate = Path.Combine(dir, "node_modules", "@kubuno", "ui", "kbview-registry.web.json");
                if (files.Exists(candidate))
                {
                    return candidate;
                }

                var parent = Path.GetDirectoryName(dir);
                if (string.Equals(parent, dir, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                dir = parent!;
            }

            return null;
        }

        private static bool DependsOnCompiler(string? packageJson)
        {
            if (string.IsNullOrWhiteSpace(packageJson))
            {
                return false;
            }

            try
            {
                using var document = JsonDocument.Parse(packageJson!);
                foreach (var section in new[] { "dependencies", "devDependencies", "peerDependencies" })
                {
                    if (document.RootElement.ValueKind == JsonValueKind.Object &&
                        document.RootElement.TryGetProperty(section, out var deps) && deps.ValueKind == JsonValueKind.Object &&
                        deps.TryGetProperty(CompilerPackage, out _))
                    {
                        return true;
                    }
                }
            }
            catch (JsonException)
            {
                // Not a package manifest we can read: not a views project.
            }

            return false;
        }

        private static IReadOnlyList<string> Strings(JsonElement config, string name) =>
            config.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array
                ? array.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList()
                : (IReadOnlyList<string>)Array.Empty<string>();

        private static string StripViewExtension(string viewPath)
        {
            foreach (var extension in new[] { ".kbview", ".kbcontrol" })
            {
                if (viewPath.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                {
                    return viewPath.Substring(0, viewPath.Length - extension.Length);
                }
            }

            return viewPath;
        }

        private static string MakeRelative(string root, string path)
        {
            var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(path);
            return full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) ? full.Substring(rootFull.Length) : full;
        }

        private static string ToPosix(string path) => path.Replace('\\', '/');
    }

    /// <summary>A user control of a web project: <c>&lt;WaffleMenu/&gt;</c> for <c>WaffleMenu.kbcontrol</c>, rendered by <see cref="Module"/>'s default export.</summary>
    public sealed class WebUserControl
    {
        public WebUserControl(string name, string module)
        {
            Name = name;
            Module = module;
        }

        public string Name { get; }

        /// <summary>Project-root-relative module, <c>/</c>-separated, with a leading <c>/</c> (<c>/src/core/shell/menus/WaffleMenu</c>).</summary>
        public string Module { get; }
    }

    /// <summary>The file system as <see cref="WebViewProject"/> reads it (tests give an in-memory one).</summary>
    public interface IWebProjectFiles
    {
        bool Exists(string path);

        /// <summary>The file's text, or null when it cannot be read.</summary>
        string? ReadText(string path);

        IEnumerable<string> Files(string directory);

        IEnumerable<string> Directories(string directory);
    }

    /// <summary>The real file system; unreadable entries are skipped, never thrown.</summary>
    public sealed class PhysicalWebProjectFiles : IWebProjectFiles
    {
        public static PhysicalWebProjectFiles Instance { get; } = new PhysicalWebProjectFiles();

        public bool Exists(string path) => File.Exists(path);

        public string? ReadText(string path)
        {
            try
            {
                return File.ReadAllText(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                return null;
            }
        }

        public IEnumerable<string> Files(string directory)
        {
            try
            {
                return Directory.Exists(directory) ? Directory.GetFiles(directory) : Array.Empty<string>();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return Array.Empty<string>();
            }
        }

        public IEnumerable<string> Directories(string directory)
        {
            try
            {
                return Directory.Exists(directory) ? Directory.GetDirectories(directory) : Array.Empty<string>();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return Array.Empty<string>();
            }
        }
    }
}
