using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kubuno.Rust.Cargo.Naming;
using Kubuno.Rust.Cargo.Toml;
using Kubuno.Web.Logic.Modules;

namespace Kubuno.Web.Logic.Generation
{
    /// <summary>What a Kubuno repository is, for the solution generator.</summary>
    public enum WebRepositoryKind
    {
        /// <summary>
        /// Kubuno Core: the server (<c>server/</c>, or <c>crates/kubuno-core</c> before the platform layout) + the shared crates
        /// (<c>common/</c>) + the host frontend and the <c>@kubuno/*</c> packages (<c>web/</c>, or <c>frontend/</c>) + Kubuno
        /// Desktop (<c>desktop/</c>).
        /// </summary>
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

        private WebRepository(string root, WebRepositoryKind kind, string name, IReadOnlyList<CargoMember> members, string? runPackage, string? runBin, IReadOnlyList<FrontendProject> frontends, ModuleManifest? module, IReadOnlyList<string> submodules, IReadOnlyList<CargoMember> desktopMembers, PlatformParts? platform = null)
        {
            IsPlatformLayout = platform is not null;
            MobileMembers = platform?.MobileMembers ?? Array.Empty<CargoMember>();
            MobileReadme = platform?.MobileReadme;
            WorkspaceSizes = platform?.WorkspaceSizes ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            Root = root;
            Kind = kind;
            Name = name;
            Members = members;
            RunPackage = runPackage;
            RunBin = runBin;
            Frontends = frontends;
            Module = module;
            Submodules = submodules;
            DesktopMembers = desktopMembers;
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

        /// <summary>
        /// The packages of the module's desktop apps: the members of the Cargo workspaces under <c>desktop/windows</c>,
        /// <c>desktop/linux</c> and <c>desktop/macos</c> (a module repository holds all its clients - docs/WEB.md,
        /// "Solutions"). Each is a workspace of its own, apart from the server's. For the core in the platform layout, the
        /// members of Kubuno Desktop's workspace (<c>desktop/</c>); empty for the core in the former layout.
        /// </summary>
        public IReadOnlyList<CargoMember> DesktopMembers { get; }

        /// <summary>
        /// Whether the module uses the platform layout (<see cref="PlatformLayout"/>: <c>common/</c>, <c>server/</c>, <c>web/</c>,
        /// <c>desktop/</c>, <c>mobile/</c>), wholly or partly during the transition. Its solution then gets one folder per part.
        /// </summary>
        public bool IsPlatformLayout { get; }

        /// <summary>The Cargo packages under <c>mobile/</c> (platform layout only; none today, the folder holds a README).</summary>
        public IReadOnlyList<CargoMember> MobileMembers { get; }

        /// <summary><c>mobile/README.md</c> when <c>mobile/</c> holds no project yet (platform layout only), else null.</summary>
        public string? MobileReadme { get; }

        /// <summary>
        /// Platform layout: the number of packages of the Cargo workspace each package (by folder) belongs to - a project of
        /// a workspace of several packages builds the whole workspace in one cargo run. Empty for the former layout.
        /// </summary>
        public IReadOnlyDictionary<string, int> WorkspaceSizes { get; }

        /// <summary>The folders of a module repository that may hold a desktop app's Cargo workspace, one per OS.</summary>
        public static IReadOnlyList<string> DesktopWorkspaceFolders { get; } = new[] { "windows", "linux", "macos" };

        /// <summary>The module id, or <c>core</c>.</summary>
        public string Id => Module?.Id ?? "core";

        /// <summary>Recognizes <paramref name="directory"/> (or returns null with the reason in <paramref name="reason"/>).</summary>
        public static WebRepository? Detect(string directory, out string? reason)
        {
            reason = null;
            var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
            var moduleToml = Path.Combine(root, "module.toml");
            if (File.Exists(moduleToml) && !CoreRepository.IsCore(root) && PlatformLayout.IsPlatformLayout(root))
            {
                return DetectPlatform(root, ModuleManifest.Load(moduleToml));
            }

            // The core in the platform layout (server/, common/, web/, desktop/, mobile/): no root Cargo.toml any more.
            if (CoreRepository.IsPlatformLayout(root))
            {
                return DetectPlatform(root, null);
            }

            if (!File.Exists(Path.Combine(root, "Cargo.toml")))
            {
                reason = "no Cargo.toml in '" + root + "'.";
                return null;
            }

            var submodules = ReadSubmodules(root);
            var members = ReadMembers(root, submodules);
            var name = Path.GetFileName(root);

            if (CoreRepository.IsCore(root))
            {
                var core = members.FirstOrDefault(member => member.PackageName == "kubuno-core");
                var bin = core is not null && core.Bins.Contains("kubuno-core") && core.Bins.Count > 1 ? "kubuno-core" : null;
                return new WebRepository(root, WebRepositoryKind.Core, name, members, core?.PackageName, bin, ReadFrontends(root, "kubuno", isCore: true), null, submodules, Array.Empty<CargoMember>());
            }

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

                return new WebRepository(root, WebRepositoryKind.Module, name, members, run?.PackageName, bin, ReadFrontends(root, manifest.Id, isCore: false), manifest, submodules, ReadDesktopMembers(root));
            }

            reason = "'" + root + "' is neither Kubuno Core (server/ or crates/kubuno-core) nor a module (module.toml).";
            return null;
        }

        /// <summary>
        /// A module (<paramref name="manifest"/>) or the core (null) in the platform layout (<see cref="PlatformLayout"/>): its
        /// server packages (<c>server/</c>, or the root package not moved yet) and <c>common/</c> crates are
        /// <see cref="Members"/>, the members of <c>desktop/</c> (one workspace, or one per <c>desktop/&lt;os&gt;</c>)
        /// <see cref="DesktopMembers"/>, those of <c>mobile/</c> <see cref="MobileMembers"/>; the frontend is <c>web/</c> (or
        /// <c>frontend/</c> not moved yet). Nothing outside the repository, nothing in a build output or a vendored folder.
        /// The core runs <c>kubuno-core</c> (its <c>kubuno-core</c> bin; the package also builds the <c>kubuno</c> CLI).
        /// </summary>
        private static WebRepository DetectPlatform(string root, ModuleManifest? manifest)
        {
            var submodules = ReadSubmodules(root);
            var sizes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            void AddGroup(List<CargoMember> target, IEnumerable<CargoMember?> group)
            {
                var listed = group
                    .Where(member => member is not null && PlatformLayout.IsListed(root, member.Directory))
                    .Select(member => member!)
                    .Where(member => !sizes.ContainsKey(member.Directory))
                    .GroupBy(member => member.Directory, StringComparer.OrdinalIgnoreCase)
                    .Select(same => same.First())
                    .ToList();
                foreach (var member in listed)
                {
                    target.Add(member);
                    sizes[member.Directory] = listed.Count;
                }
            }

            // The server: server/ (its workspace), else - during the transition - the package still at the root.
            var members = new List<CargoMember>();
            var server = Path.Combine(root, "server");
            var serverRoot = File.Exists(Path.Combine(server, "Cargo.toml")) ? server : File.Exists(Path.Combine(root, "Cargo.toml")) ? root : null;
            if (serverRoot is not null)
            {
                AddGroup(members, ReadMembers(serverRoot));
            }

            foreach (var submodule in submodules)
            {
                AddGroup(members, new[] { CargoMember.TryRead(Path.GetFullPath(Path.Combine(root, submodule.Replace('/', Path.DirectorySeparatorChar)))) });
            }

            // The shared rules: common/ as one workspace, else each common/<crate> (a workspace of its own or a package).
            AddPlatformFolder(Path.Combine(root, "common"), group => AddGroup(members, group));

            // The desktop app: desktop/Cargo.toml (one workspace for every OS), else a workspace per desktop/<os>.
            var desktopMembers = new List<CargoMember>();
            var desktop = Path.Combine(root, "desktop");
            if (File.Exists(Path.Combine(desktop, "Cargo.toml")))
            {
                AddGroup(desktopMembers, ReadMembers(desktop));
            }
            else
            {
                foreach (var os in PlatformLayout.DesktopPlatforms)
                {
                    AddPlatformFolder(Path.Combine(desktop, os), group => AddGroup(desktopMembers, group));
                }
            }

            // The mobile apps: mobile/Cargo.toml, else per mobile/<platform>; a README alone keeps the folder visible.
            var mobileMembers = new List<CargoMember>();
            var mobile = Path.Combine(root, "mobile");
            if (File.Exists(Path.Combine(mobile, "Cargo.toml")))
            {
                AddGroup(mobileMembers, ReadMembers(mobile));
            }
            else
            {
                foreach (var platform in PlatformLayout.MobilePlatforms)
                {
                    AddPlatformFolder(Path.Combine(mobile, platform), group => AddGroup(mobileMembers, group));
                }
            }

            var readme = Path.Combine(mobile, "README.md");
            var mobileReadme = mobileMembers.Count == 0 && File.Exists(readme) ? readme : null;

            var parts = new PlatformParts(mobileMembers, mobileReadme, sizes);
            if (manifest is null)
            {
                var core = members.FirstOrDefault(member => member.PackageName == ProjectNaming.CoreRunPackage);
                var coreBin = core is not null && core.Bins.Contains(ProjectNaming.CoreRunPackage) && core.Bins.Count > 1 ? ProjectNaming.CoreRunPackage : null;
                return new WebRepository(root, WebRepositoryKind.Core, Path.GetFileName(root), members, core?.PackageName, coreBin, ReadFrontends(root, "kubuno", isCore: true), null, submodules, desktopMembers, parts);
            }

            var run = members.FirstOrDefault(member => member.Bins.Contains(manifest.Entrypoint, StringComparer.OrdinalIgnoreCase))
                ?? (serverRoot is null ? null : members.FirstOrDefault(member => string.Equals(member.Directory, serverRoot, StringComparison.OrdinalIgnoreCase)));
            var bin = run is not null && run.Bins.Count > 1 ? manifest.Entrypoint : null;
            if (run is not null && run.Bins.Count == 1 && !string.Equals(run.Bins[0], run.PackageName, StringComparison.Ordinal))
            {
                bin = run.Bins[0];
            }

            return new WebRepository(root, WebRepositoryKind.Module, Path.GetFileName(root), members, run?.PackageName, bin, ReadFrontends(root, manifest.Id, isCore: false), manifest, submodules, desktopMembers, parts);
        }

        /// <summary>
        /// The packages of a platform folder (<c>common/</c>, <c>desktop/&lt;os&gt;/</c>, <c>mobile/&lt;platform&gt;/</c>): its
        /// workspace when its <c>Cargo.toml</c> is one, else its own package and each sub-folder's (a sub-folder workspace with
        /// its members), one group per workspace.
        /// </summary>
        private static void AddPlatformFolder(string folder, Action<IEnumerable<CargoMember?>> addGroup)
        {
            if (File.Exists(Path.Combine(folder, "Cargo.toml")))
            {
                if (PlatformLayout.IsWorkspace(folder))
                {
                    addGroup(ReadMembers(folder));
                    return;
                }

                addGroup(new[] { CargoMember.TryRead(folder) });
            }

            foreach (var child in PlatformLayout.ChildDirectories(folder))
            {
                if (File.Exists(Path.Combine(child, "Cargo.toml")))
                {
                    addGroup(PlatformLayout.IsWorkspace(child) ? (IEnumerable<CargoMember?>)ReadMembers(child) : new[] { CargoMember.TryRead(child) });
                }
            }
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

        /// <summary>
        /// The members of every desktop workspace of a module repository (<c>desktop/&lt;os&gt;/Cargo.toml</c>, see
        /// <see cref="DesktopMembers"/>), in OS order.
        /// </summary>
        public static IReadOnlyList<CargoMember> ReadDesktopMembers(string root)
        {
            var result = new List<CargoMember>();
            foreach (var os in DesktopWorkspaceFolders)
            {
                var workspace = Path.Combine(root, "desktop", os);
                if (File.Exists(Path.Combine(workspace, "Cargo.toml")))
                {
                    result.AddRange(ReadMembers(workspace).Where(member => result.All(existing => !string.Equals(existing.Directory, member.Directory, StringComparison.OrdinalIgnoreCase))));
                }
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
            // The platform layout's web/ (docs/WEB.md, "Solutions"), else the former frontend/.
            var frontend = File.Exists(Path.Combine(root, "web", "package.json")) ? Path.Combine(root, "web") : Path.Combine(root, "frontend");
            if (File.Exists(Path.Combine(frontend, "package.json")))
            {
                result.Add(new FrontendProject(FrontendKind.App, frontend, isCore ? ProjectNaming.CoreFrontend : ProjectNaming.ForModuleFrontend(id), NpmName(frontend), null, Array.Empty<string>(), null));
            }
            else if (File.Exists(Path.Combine(root, "package.json")))
            {
                result.Add(new FrontendProject(FrontendKind.App, root, isCore ? ProjectNaming.CoreFrontend : ProjectNaming.ForModuleFrontend(id), NpmName(root), null, Array.Empty<string>(), null));
            }

            // The core packages in dependency order (ui, then sdk, then drive), the others by name.
            var order = new[] { "ui", "sdk", "drive" };
            var handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string Key(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);

            // The core's host-tree packages (@kubuno/ui, sdk, drive, built from the host app's sources): CO-LOCATED in
            // web/src/<id> when the package.json lives there (the project tree then matches disk, no linked items),
            // else still wrapped in web/packages/<id> with the sources linked in (the transition). In dependency order.
            if (isCore)
            {
                foreach (var shortName in order)
                {
                    if (!CorePackageSources.TryGetValue(shortName, out var folders))
                    {
                        continue;
                    }

                    var colocated = Path.Combine(frontend, "src", shortName);
                    var wrapped = Path.Combine(frontend, "packages", shortName);
                    var package = File.Exists(Path.Combine(colocated, "package.json")) ? colocated
                        : File.Exists(Path.Combine(wrapped, "package.json")) ? wrapped
                        : null;
                    if (package is null)
                    {
                        continue;
                    }

                    // Co-located: the package IS its source folder (no linked items). Wrapped: link the host src/ folders.
                    var linked = string.Equals(package, colocated, StringComparison.OrdinalIgnoreCase)
                        ? (IReadOnlyList<string>)Array.Empty<string>()
                        : folders.Select(folder => Path.Combine(frontend, "src", folder)).Where(System.IO.Directory.Exists).ToList();
                    result.Add(new FrontendProject(FrontendKind.Package, package, ProjectNaming.ForNpmPackage(NpmName(package), shortName, null), NpmName(package), shortName, linked, frontend));
                    handled.Add(Key(package));
                }
            }

            // The other packages (self-contained: host-runtime, vectors, views, views-compiler, views-migrate; and a
            // module's packages), whose sources live in packages/<name>. Skip any already added above.
            foreach (var packages in new[] { Path.Combine(frontend, "packages"), Path.Combine(root, "packages") })
            {
                if (!System.IO.Directory.Exists(packages))
                {
                    continue;
                }

                foreach (var package in System.IO.Directory.GetDirectories(packages)
                    .OrderBy(path => Array.IndexOf(order, Path.GetFileName(path).ToLowerInvariant()) is var rank && rank >= 0 ? rank : order.Length)
                    .ThenBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    if (!File.Exists(Path.Combine(package, "package.json")) || handled.Contains(Key(package)))
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

                    // Kubuno.Web.UI for @kubuno/ui in the core, Kubuno.<Module>.Web.<Name> in a module (ProjectNaming).
                    var projectName = ProjectNaming.ForNpmPackage(npmName, shortName, isCore ? null : id);
                    result.Add(new FrontendProject(FrontendKind.Package, package, projectName, npmName, packageId, linked, nodeModules));
                    handled.Add(Key(package));
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

    /// <summary>What the platform layout adds to a <see cref="WebRepository"/>.</summary>
    internal sealed class PlatformParts
    {
        public PlatformParts(IReadOnlyList<CargoMember> mobileMembers, string? mobileReadme, IReadOnlyDictionary<string, int> workspaceSizes)
        {
            MobileMembers = mobileMembers;
            MobileReadme = mobileReadme;
            WorkspaceSizes = workspaceSizes;
        }

        public IReadOnlyList<CargoMember> MobileMembers { get; }

        public string? MobileReadme { get; }

        public IReadOnlyDictionary<string, int> WorkspaceSizes { get; }
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
