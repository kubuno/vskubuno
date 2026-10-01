using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kubuno.Rust.Cargo.Toml;
using Kubuno.Web.Logic.Modules;

namespace Kubuno.Web.Logic.Generation
{
    /// <summary>What a Kubuno repository is, for the solution generator.</summary>
    public enum WebRepositoryKind
    {
        /// <summary>Kubuno Core Web: <c>crates/kubuno-core</c> + the host frontend + the <c>@kubuno/*</c> packages.</summary>
        Core,

        /// <summary>A module: <c>module.toml</c> at the root.</summary>
        Module,
    }

    /// <summary>The kind of a JavaScript/TypeScript project of a repository.</summary>
    public enum FrontendKind
    {
        /// <summary>An application bundle: the core's host app, a module's frontend (Vite).</summary>
        App,

        /// <summary>A publishable npm package (<c>packages/&lt;name&gt;/package.json</c>): <c>@kubuno/ui</c>, sdk, drive.</summary>
        Package,
    }

    /// <summary>
    /// One JavaScript/TypeScript project of a repository (docs/WEB.md, "Solutions"): where its <c>.esproj</c> goes, its
    /// npm name and, for a core package whose sources live in the host app's tree (<c>@kubuno/ui</c> is built from
    /// <c>frontend/src/ui</c>, the package folder being the build/publish wrapper), the source folders it shows.
    /// </summary>
    public sealed class FrontendProject
    {
        public FrontendProject(FrontendKind kind, string directory, string projectName, string? npmName, string? packageId, IReadOnlyList<string> linkedSources, string? nodeModulesDirectory)
        {
            Kind = kind;
            Directory = directory;
            ProjectName = projectName;
            NpmName = npmName;
            PackageId = packageId;
            LinkedSources = linkedSources;
            NodeModulesDirectory = nodeModulesDirectory;
        }

        public FrontendKind Kind { get; }

        /// <summary>The folder holding its package.json (and the generated .esproj).</summary>
        public string Directory { get; }

        /// <summary>The .esproj name without extension (<c>kubuno-frontend</c>, <c>kubuno-ui</c>, <c>calendar-frontend</c>).</summary>
        public string ProjectName { get; }

        public string? NpmName { get; }

        /// <summary>For a core package built from the host's sources: <c>ui</c>, <c>sdk</c> or <c>drive</c> (core/frontend/packages/build.sh).</summary>
        public string? PackageId { get; }

        /// <summary>Source folders shown in the project although they live elsewhere (absolute paths).</summary>
        public IReadOnlyList<string> LinkedSources { get; }

        /// <summary>The folder whose <c>node_modules</c> the project uses when it is not its own (a package of the host).</summary>
        public string? NodeModulesDirectory { get; }
    }

    /// <summary>
    /// A Kubuno repository as the web solution generator sees it (docs/WEB.md, "Solutions"): its kind, every Cargo package
    /// (the root package, the <c>[workspace] members</c>, crates in git submodules), the package that runs, and every
    /// JavaScript project (apps and packages). Read from the manifests themselves - no <c>cargo metadata</c>, no npm.
    /// </summary>
    public sealed class WebRepository
    {
        /// <summary>The core packages whose sources are folders of the host app (core/frontend/packages/build.sh).</summary>
        private static readonly IReadOnlyDictionary<string, string[]> CorePackageSources = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["ui"] = new[] { "ui" },
            ["sdk"] = new[] { "sdk" },
            ["drive"] = new[] { "drive" },
        };

        private WebRepository(string root, WebRepositoryKind kind, string name, IReadOnlyList<CargoMember> members, string? runPackage, string? runBin, IReadOnlyList<FrontendProject> frontends, ModuleManifest? module, IReadOnlyList<string> submodules)
        {
            Root = root;
            Kind = kind;
            Name = name;
            Members = members;
            RunPackage = runPackage;
            RunBin = runBin;
            Frontends = frontends;
            Module = module;
            Submodules = submodules;
        }

        public string Root { get; }

        public WebRepositoryKind Kind { get; }

        /// <summary>The repository folder's name (<c>core</c>, <c>calendar</c>).</summary>
        public string Name { get; }

        public IReadOnlyList<CargoMember> Members { get; }

        /// <summary>The package started by F5: <c>kubuno-core</c>, or the module's package.</summary>
        public string? RunPackage { get; }

        /// <summary>Its <c>[[bin]]</c> when the package has several (the core also builds the <c>kubuno</c> CLI).</summary>
        public string? RunBin { get; }

        /// <summary>Every JavaScript/TypeScript project: the app(s) first, then the packages.</summary>
        public IReadOnlyList<FrontendProject> Frontends { get; }

        /// <summary>The main app (<c>frontend/</c>), or null for a backend-only module.</summary>
        public FrontendProject? App => Frontends.FirstOrDefault(frontend => frontend.Kind == FrontendKind.App);

        /// <summary>Compatibility: the main app's folder.</summary>
        public string? FrontendDirectory => App?.Directory;

        public ModuleManifest? Module { get; }

        /// <summary>The git submodules' paths (relative, from <c>.gitmodules</c>).</summary>
        public IReadOnlyList<string> Submodules { get; }

        /// <summary>The module id, or <c>core</c>.</summary>
        public string Id => Module?.Id ?? "core";

        /// <summary>Recognizes <paramref name="directory"/> (or returns null with the reason in <paramref name="reason"/>).</summary>
        public static WebRepository? Detect(string directory, out string? reason)
        {
            reason = null;
            var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
            if (!File.Exists(Path.Combine(root, "Cargo.toml")))
            {
                reason = "no Cargo.toml in '" + root + "'.";
                return null;
            }

            var submodules = ReadSubmodules(root);
            var members = ReadMembers(root, submodules);
            var name = Path.GetFileName(root);

            if (File.Exists(Path.Combine(root, "crates", "kubuno-core", "Cargo.toml")))
            {
                var core = members.FirstOrDefault(member => member.PackageName == "kubuno-core");
                var bin = core is not null && core.Bins.Contains("kubuno-core") && core.Bins.Count > 1 ? "kubuno-core" : null;
                return new WebRepository(root, WebRepositoryKind.Core, name, members, core?.PackageName, bin, ReadFrontends(root, "kubuno", isCore: true), null, submodules);
            }

            var moduleToml = Path.Combine(root, "module.toml");
            if (File.Exists(moduleToml))
            {
                var manifest = ModuleManifest.Load(moduleToml);
                // The package building the entry point: the bin named after it, else the root package.
                var run = members.FirstOrDefault(member => member.Bins.Contains(manifest.Entrypoint, StringComparer.OrdinalIgnoreCase))
                    ?? members.FirstOrDefault(member => member.Directory == root);
                var bin = run is not null && run.Bins.Count > 1 ? manifest.Entrypoint : null;
                if (run is not null && run.Bins.Count == 1 && !string.Equals(run.Bins[0], run.PackageName, StringComparison.Ordinal))
                {
                    bin = run.Bins[0];
                }

                return new WebRepository(root, WebRepositoryKind.Module, name, members, run?.PackageName, bin, ReadFrontends(root, manifest.Id, isCore: false), manifest, submodules);
            }

            reason = "'" + root + "' is neither Kubuno Core Web (crates/kubuno-core) nor a module (module.toml).";
            return null;
        }

        /// <summary>The root package (when <c>[package]</c> is there), every <c>[workspace] members</c> entry (globs included) and the crates of git submodules.</summary>
        public static IReadOnlyList<CargoMember> ReadMembers(string root, IReadOnlyList<string>? submodules = null)
        {
            var result = new List<CargoMember>();
            void Add(CargoMember? member)
            {
                if (member is not null && result.All(existing => !string.Equals(existing.Directory, member.Directory, StringComparison.OrdinalIgnoreCase)))
                {
                    result.Add(member);
                }
            }

            var document = TomlDocument.Parse(File.ReadAllText(Path.Combine(root, "Cargo.toml")));
            Add(CargoMember.TryRead(root));
            var members = document.GetValue("workspace", "members")?.AsArray();
            if (members is not null)
            {
                foreach (var value in members)
                {
                    var relative = value.AsString();
                    if (string.IsNullOrWhiteSpace(relative))
                    {
                        continue;
                    }

                    foreach (var directory in ExpandMember(root, relative!))
                    {
                        Add(CargoMember.TryRead(directory));
                    }
                }
            }

            foreach (var submodule in submodules ?? Array.Empty<string>())
            {
                Add(CargoMember.TryRead(Path.GetFullPath(Path.Combine(root, submodule.Replace('/', Path.DirectorySeparatorChar)))));
            }

            return result;
        }

        /// <summary>The paths of <c>.gitmodules</c> (git submodules), relative to the repository.</summary>
        public static IReadOnlyList<string> ReadSubmodules(string root)
        {
            var path = Path.Combine(root, ".gitmodules");
            if (!File.Exists(path))
            {
                return Array.Empty<string>();
            }

            return Regex.Matches(File.ReadAllText(path), @"^\s*path\s*=\s*(?<path>.+?)\s*$", RegexOptions.Multiline)
                .Cast<Match>()
                .Select(match => match.Groups["path"].Value)
                .ToList();
        }

        /// <summary>
        /// The JavaScript projects: the app of <c>frontend/</c> (or of the root), the packages of
        /// <c>frontend/packages/*</c> and <c>packages/*</c>, and the frontends of git submodules.
        /// </summary>
        private static IReadOnlyList<FrontendProject> ReadFrontends(string root, string id, bool isCore)
        {
            var result = new List<FrontendProject>();
            var frontend = Path.Combine(root, "frontend");
            if (File.Exists(Path.Combine(frontend, "package.json")))
            {
                result.Add(new FrontendProject(FrontendKind.App, frontend, id + "-frontend", NpmName(frontend), null, Array.Empty<string>(), null));
            }
            else if (File.Exists(Path.Combine(root, "package.json")))
            {
                result.Add(new FrontendProject(FrontendKind.App, root, id + "-frontend", NpmName(root), null, Array.Empty<string>(), null));
            }

            foreach (var packages in new[] { Path.Combine(frontend, "packages"), Path.Combine(root, "packages") })
            {
                if (!System.IO.Directory.Exists(packages))
                {
                    continue;
                }

                // The core packages in dependency order (ui, then sdk, then drive), the others by name.
                var order = new[] { "ui", "sdk", "drive" };
                foreach (var package in System.IO.Directory.GetDirectories(packages)
                    .OrderBy(path => Array.IndexOf(order, Path.GetFileName(path).ToLowerInvariant()) is var rank && rank >= 0 ? rank : order.Length)
                    .ThenBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    if (!File.Exists(Path.Combine(package, "package.json")))
                    {
                        continue;
                    }

                    var shortName = Path.GetFileName(package);
                    var npmName = NpmName(package);
                    var linked = new List<string>();
                    string? packageId = null;
                    string? nodeModules = null;
                    if (isCore && packages.StartsWith(frontend, StringComparison.OrdinalIgnoreCase) && CorePackageSources.TryGetValue(shortName, out var folders))
                    {
                        packageId = shortName;
                        nodeModules = frontend;
                        linked.AddRange(folders.Select(folder => Path.Combine(frontend, "src", folder)).Where(System.IO.Directory.Exists));
                    }

                    var projectName = npmName is not null && npmName.StartsWith("@", StringComparison.Ordinal)
                        ? npmName.Substring(1).Replace('/', '-')
                        : (isCore ? "kubuno-" : id + "-") + shortName;
                    result.Add(new FrontendProject(FrontendKind.Package, package, projectName, npmName, packageId, linked, nodeModules));
                }
            }

            return result;
        }

        private static string? NpmName(string directory)
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "package.json")));
                return document.RootElement.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() : null;
            }
            catch (Exception exception) when (exception is JsonException || exception is IOException)
            {
                return null;
            }
        }

        /// <summary>A member entry: a folder, or a <c>dir/*</c> glob (the only glob form Cargo workspaces use in practice).</summary>
        private static IEnumerable<string> ExpandMember(string root, string relative)
        {
            var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            if (relative.EndsWith("/*", StringComparison.Ordinal) || relative.EndsWith("\\*", StringComparison.Ordinal))
            {
                var parent = path.Substring(0, path.Length - 2);
                return System.IO.Directory.Exists(parent) ? System.IO.Directory.GetDirectories(parent).OrderBy(d => d, StringComparer.OrdinalIgnoreCase) : Enumerable.Empty<string>();
            }

            return relative.IndexOfAny(new[] { '*', '?' }) >= 0 ? Enumerable.Empty<string>() : new[] { Path.GetFullPath(path) };
        }
    }

    /// <summary>A Cargo package of the repository.</summary>
    public sealed class CargoMember
    {
        private CargoMember(string directory, string packageName, IReadOnlyList<string> bins)
        {
            Directory = directory;
            PackageName = packageName;
            Bins = bins;
        }

        public string Directory { get; }

        public string PackageName { get; }

        /// <summary>Its executables (<c>[[bin]]</c> names, or the package name for a lone <c>src/main.rs</c>).</summary>
        public IReadOnlyList<string> Bins { get; }

        public bool IsLibraryOnly => Bins.Count == 0;

        public static CargoMember? TryRead(string directory)
        {
            var manifest = Path.Combine(directory, "Cargo.toml");
            if (!File.Exists(manifest))
            {
                return null;
            }

            var document = TomlDocument.Parse(File.ReadAllText(manifest));
            var name = document.GetValue("package", "name")?.AsString();
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            var bins = document.GetArrayOfTables("bin")
                .Select(bin => bin.GetValue("name")?.AsString())
                .Where(bin => !string.IsNullOrWhiteSpace(bin))
                .Select(bin => bin!)
                .ToList();
            if (bins.Count == 0 && File.Exists(Path.Combine(directory, "src", "main.rs")))
            {
                bins.Add(name!);
            }

            return new CargoMember(Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar), name!, bins);
        }
    }
}
