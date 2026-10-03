using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Kubuno.Rust.Cargo.Naming
{
    /// <summary>
    /// The names of the Visual Studio projects and solutions of the Kubuno repositories (docs/RSPROJ.md, "Project and
    /// solution names"). One rule everywhere: a project is <c>Kubuno.&lt;Product&gt;.&lt;Component&gt;</c> and the Rust crate it
    /// builds is <c>kubuno-&lt;product&gt;-&lt;component&gt;</c> (<c>kubuno-desktop-ui</c> is <c>Kubuno.Desktop.UI</c>); a solution is
    /// its repository (<c>Kubuno.Core.slnx</c>, <c>Kubuno.Desktop.slnx</c>, <c>Kubuno.&lt;Module&gt;.slnx</c>). The project file is
    /// named after the project (<c>Kubuno.Desktop.UI.rsproj</c>); its <c>&lt;CargoPackage&gt;</c> keeps the crate name. A crate
    /// outside this naming (a template, a sample, a third-party workspace) keeps its package name as the project name.
    /// Shared by "Generate Visual Studio Projects" (Rust layer) and "Generate Solution" (web layer): netstandard2.0.
    /// </summary>
    public static class ProjectNaming
    {
        /// <summary>The solution of the core repository.</summary>
        public const string CoreSolutionFileName = "Kubuno.Core.slnx";

        /// <summary>The solution of the desktop repository (its two Cargo workspaces, <c>windows/</c> and <c>common/</c>).</summary>
        public const string DesktopSolutionFileName = "Kubuno.Desktop.slnx";

        /// <summary>The run package of the core repository.</summary>
        public const string CoreRunPackage = "kubuno-core";

        /// <summary>Segments written as acronyms (<c>ui</c> → <c>UI</c>, <c>ls</c> → <c>LS</c>); any other gets an upper-case first letter.</summary>
        private static readonly Dictionary<string, string> Acronyms = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ui"] = "UI",
            ["ls"] = "LS",
        };

        /// <summary>One segment of a name: <c>ui</c> → <c>UI</c>, <c>views</c> → <c>Views</c>, <c>p2pnas</c> → <c>P2pnas</c>.</summary>
        public static string Pascal(string segment)
        {
            if (string.IsNullOrEmpty(segment))
            {
                return string.Empty;
            }

            if (Acronyms.TryGetValue(segment, out var acronym))
            {
                return acronym;
            }

            return char.ToUpperInvariant(segment[0]) + segment.Substring(1);
        }

        /// <summary>The segments of a crate, module or npm name (split on <c>-</c> and <c>_</c>).</summary>
        public static IReadOnlyList<string> Segments(string name) =>
            (name ?? string.Empty).Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries);

        /// <summary>The product part of a module id, joined: <c>p2pnas</c> → <c>P2pnas</c>, <c>p2p-nas</c> → <c>P2pNas</c>.</summary>
        public static string Product(string id) => string.Concat(Segments(id).Select(Pascal));

        /// <summary>
        /// The project of a crate in a generic Kubuno repository (the desktop one): <c>kubuno-desktop-ui</c> →
        /// <c>Kubuno.Desktop.UI</c>, <c>kubuno-desktop</c> → <c>Kubuno.Desktop</c>, <c>kubuno-drive-desktop-app-controls</c> →
        /// <c>Kubuno.Drive.Desktop.App.Controls</c>. A crate not named <c>kubuno-…</c> keeps its package name.
        /// </summary>
        public static string ForCrate(string packageName)
        {
            var segments = Segments(packageName);
            if (segments.Count < 2 || !string.Equals(segments[0], "kubuno", StringComparison.Ordinal))
            {
                return packageName;
            }

            return "Kubuno." + string.Join(".", segments.Skip(1).Select(Pascal));
        }

        /// <summary>
        /// The project of a crate of the core repository: the run package (<c>kubuno-core</c>) is <c>Kubuno.Core.Server</c>,
        /// <c>kubuno-db</c> is <c>Kubuno.Core.Db</c>, any other crate <c>Kubuno.Core.&lt;Rest&gt;</c>.
        /// </summary>
        public static string ForCoreCrate(string packageName, bool isRun)
        {
            if (isRun || string.Equals(packageName, CoreRunPackage, StringComparison.Ordinal))
            {
                return "Kubuno.Core.Server";
            }

            var segments = Segments(packageName).ToList();
            if (segments.Count > 0 && segments[0] == "kubuno")
            {
                segments.RemoveAt(0);
            }

            if (segments.Count > 0 && segments[0] == "core")
            {
                segments.RemoveAt(0);
            }

            return segments.Count == 0 ? "Kubuno.Core" : "Kubuno.Core." + string.Join(".", segments.Select(Pascal));
        }

        /// <summary>
        /// The project of a crate of a module repository (<paramref name="moduleId"/> from <c>module.toml</c>): the run package
        /// is <c>Kubuno.&lt;Id&gt;.Server</c>; another crate drops <c>kubuno-</c> then <c>&lt;id&gt;-</c> (<c>kubuno-forms-core</c> →
        /// <c>Kubuno.Forms.Core</c>, <c>p2pnas-core</c> → <c>Kubuno.P2pnas.Core</c>, <c>sync-engine</c> → <c>Kubuno.&lt;Id&gt;.Sync.Engine</c>).
        /// </summary>
        public static string ForModuleCrate(string moduleId, string packageName, bool isRun)
        {
            var product = "Kubuno." + Product(moduleId);
            if (isRun)
            {
                return product + ".Server";
            }

            var rest = (packageName ?? string.Empty).Replace('_', '-');
            if (rest.StartsWith("kubuno-", StringComparison.Ordinal))
            {
                rest = rest.Substring("kubuno-".Length);
            }

            var idPrefix = (moduleId ?? string.Empty).Replace('_', '-') + "-";
            if (rest.StartsWith(idPrefix, StringComparison.Ordinal))
            {
                rest = rest.Substring(idPrefix.Length);
            }
            else if (string.Equals(rest, (moduleId ?? string.Empty).Replace('_', '-'), StringComparison.Ordinal))
            {
                // A second package named after the module itself (not its run package).
                return product;
            }

            var segments = Segments(rest);
            return segments.Count == 0 ? product : product + "." + string.Join(".", segments.Select(Pascal));
        }

        /// <summary>The core's host app (<c>core/frontend</c>).</summary>
        public static string CoreFrontend => "Kubuno.Core.Frontend";

        /// <summary>A module's web frontend: <c>Kubuno.&lt;Id&gt;.Web</c>.</summary>
        public static string ForModuleFrontend(string moduleId) => "Kubuno." + Product(moduleId) + ".Web";

        /// <summary>
        /// An npm package project. In the core, <c>@kubuno/x-y</c> is <c>Kubuno.Web.X.Y</c> (<c>@kubuno/ui</c> →
        /// <c>Kubuno.Web.UI</c>, <c>@kubuno/views-compiler</c> → <c>Kubuno.Web.Views.Compiler</c>); in a module (a non-null
        /// <paramref name="moduleId"/>) it is <c>Kubuno.&lt;Id&gt;.Web.&lt;X&gt;</c>. The npm package names themselves never change.
        /// </summary>
        public static string ForNpmPackage(string? npmName, string folderName, string? moduleId)
        {
            var shortName = npmName ?? folderName;
            var slash = shortName.LastIndexOf('/');
            if (slash >= 0)
            {
                shortName = shortName.Substring(slash + 1);
            }

            var segments = Segments(shortName).Select(Pascal).ToList();
            if (moduleId is not null)
            {
                var id = Segments(moduleId).Select(Pascal).ToList();
                if (segments.Count > id.Count && segments.Take(id.Count).SequenceEqual(id, StringComparer.Ordinal))
                {
                    segments = segments.Skip(id.Count).ToList();
                }

                return "Kubuno." + Product(moduleId) + ".Web" + (segments.Count == 0 ? string.Empty : "." + string.Join(".", segments));
            }

            return "Kubuno.Web" + (segments.Count == 0 ? string.Empty : "." + string.Join(".", segments));
        }

        /// <summary>The solution of a module repository: <c>Kubuno.&lt;Id&gt;.slnx</c>.</summary>
        public static string ModuleSolutionFileName(string moduleId) => "Kubuno." + Product(moduleId) + ".slnx";

        /// <summary>
        /// Whether <paramref name="repositoryRoot"/> is the desktop repository: its framework under
        /// <c>windows/src/crates</c> (<c>kubuno-desktop-ui</c>, or <c>kubuno-ui</c> in a checkout older than the 2026-10 rename).
        /// </summary>
        public static bool IsDesktopRepository(string repositoryRoot, Func<string, bool>? fileExists = null)
        {
            fileExists ??= File.Exists;
            var crates = Path.Combine(repositoryRoot, "windows", "src", "crates");
            return fileExists(Path.Combine(crates, "kubuno-desktop-ui", "Cargo.toml"))
                || fileExists(Path.Combine(crates, "kubuno-ui", "Cargo.toml"));
        }

        /// <summary>
        /// Whether <paramref name="workspaceRoot"/> is the desktop repository's <c>windows</c> Cargo workspace (its framework in
        /// <c>src/crates</c>, either naming).
        /// </summary>
        public static bool IsDesktopWindowsWorkspace(string workspaceRoot, Func<string, bool>? fileExists = null)
        {
            fileExists ??= File.Exists;
            var crates = Path.Combine(workspaceRoot, "src", "crates");
            return fileExists(Path.Combine(crates, "kubuno-desktop-ui", "Cargo.toml"))
                || fileExists(Path.Combine(crates, "kubuno-ui", "Cargo.toml"));
        }

        /// <summary>The module id of a <c>module.toml</c> (<c>id = "…"</c>), or null.</summary>
        public static string? ModuleId(string moduleTomlText)
        {
            var id = Regex.Match(moduleTomlText ?? string.Empty, "^\\s*id\\s*=\\s*\"(?<id>[^\"]+)\"", RegexOptions.Multiline).Groups["id"].Value;
            return id.Length > 0 ? id : null;
        }

        /// <summary>The git repository holding <paramref name="directory"/> (the nearest folder with a <c>.git</c> entry), or null.</summary>
        public static string? RepositoryRoot(string directory, Func<string, bool>? directoryExists = null, Func<string, bool>? fileExists = null)
        {
            directoryExists ??= Directory.Exists;
            fileExists ??= File.Exists;
            for (var dir = directory; !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir))
            {
                var git = Path.Combine(dir, ".git");
                if (directoryExists(git) || fileExists(git))
                {
                    return dir;
                }
            }

            return null;
        }
    }
}
