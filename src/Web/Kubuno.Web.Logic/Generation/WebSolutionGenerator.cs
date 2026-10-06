using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Xml.Linq;
using Kubuno.Rust.Cargo.Naming;

namespace Kubuno.Web.Logic.Generation
{
    /// <summary>Versions of the SDKs the generated projects name.</summary>
    public sealed class WebSdkVersions
    {
        public WebSdkVersions(string rustSdk, string webSdk, string javaScriptSdk)
        {
            RustSdk = rustSdk;
            WebSdk = webSdk;
            JavaScriptSdk = javaScriptSdk;
        }

        /// <summary>The versions this extension ships (Kubuno.Rust.Sdk, Kubuno.Web.Sdk) and the JavaScript SDK it was verified with.</summary>
        public static WebSdkVersions Current { get; } = new WebSdkVersions("1.1.1", "1.0.2", "1.0.6887863");

        public string RustSdk { get; }

        public string WebSdk { get; }

        public string JavaScriptSdk { get; }
    }

    /// <summary>One file the generator proposes.</summary>
    public sealed class GeneratedFile
    {
        public GeneratedFile(string path, string content, bool mergeable)
        {
            Path = path;
            Content = content;
            Mergeable = mergeable;
        }

        public string Path { get; }

        public string Content { get; }

        /// <summary>A solution: merged into an existing file (missing projects added). Every other file is only ever created.</summary>
        public bool Mergeable { get; }
    }

    /// <summary>
    /// Generates the Visual Studio files of Kubuno web repositories (docs/WEB.md, "Solutions"): one <c>.rsproj</c> per
    /// Cargo package (the package F5 starts with <c>Kubuno.Web.Sdk</c> and its role), one <c>.esproj</c> for the
    /// frontend (Visual Studio's own JavaScript project system), its <c>launch.json</c>, the <c>.slnx</c> and a shared
    /// <c>.slnLaunch</c> (multi-project launch profiles). Like "Generate Visual Studio Projects", a project file that
    /// exists is never touched; a solution is merged into (only the missing projects are added). Pure: returns the
    /// files, the Visual Studio command writes them.
    /// </summary>
    public static class WebSolutionGenerator
    {
        public const string RsprojTypeGuid = "6c7c4cb5-6e36-4c6f-9c6f-9c6e9b4d4c13";

        public const string EsprojTypeGuid = "54a90642-561a-4bb1-a94e-469adee60c69";

        /// <summary>Solution folders of a repository (docs/WEB.md, "Solutions").</summary>
        public const string ServerFolder = "Server";

        public const string LibrariesFolderName = "Libraries";

        public const string FrontendFolder = "Frontend";

        /// <summary>The module's desktop apps (<c>desktop/windows</c>, later <c>desktop/linux</c> and <c>desktop/macos</c>).</summary>
        public const string DesktopFolder = "Desktop";

        /// <summary>The core's publishable npm packages (<c>@kubuno/ui</c>, sdk, drive...).</summary>
        public const string PackagesFolder = "npm packages";

        /// <summary>The single-repository libraries folder (kept for callers of the first version).</summary>
        public const string LibrariesFolder = "/Libraries/";

        /// <summary>The Vite dev server of the core's frontend (vite.config.ts: default port, proxy to :8080).</summary>
        public const string ViteDevServerUrl = "http://localhost:5173";

        /// <summary>The main app project's name: <c>Kubuno.Core.Frontend</c> for the core, <c>Kubuno.&lt;Module&gt;.Web</c> for a module (<see cref="ProjectNaming"/>).</summary>
        public static string FrontendProjectName(WebRepository repository) => repository.App?.ProjectName
            ?? (repository.Kind == WebRepositoryKind.Core ? ProjectNaming.CoreFrontend : ProjectNaming.ForModuleFrontend(repository.Id));

        /// <summary>
        /// The project name (and <c>.rsproj</c> file name) of a Cargo package (<see cref="ProjectNaming"/>): in the core,
        /// <c>Kubuno.Core.Server</c> for <c>kubuno-core</c> and <c>Kubuno.Core.&lt;X&gt;</c> for <c>kubuno-x</c>; in a module,
        /// <c>Kubuno.&lt;Module&gt;.Server</c> for the package F5 starts and <c>Kubuno.&lt;Module&gt;.&lt;X&gt;</c> for the others.
        /// </summary>
        public static string RsprojName(WebRepository repository, CargoMember member)
        {
            var isRun = member.PackageName == repository.RunPackage;
            return repository.Kind == WebRepositoryKind.Core
                ? ProjectNaming.ForCoreCrate(member.PackageName, isRun)
                : ProjectNaming.ForModuleCrate(repository.Id, member.PackageName, isRun);
        }

        /// <summary>
        /// The solution of one repository - a solution is its repository: <c>Kubuno.Core.slnx</c> for the core and
        /// <c>Kubuno.Calendar.slnx</c> for a module (the desktop repository's is <c>Kubuno.Desktop.slnx</c>).
        /// </summary>
        public static string DefaultSolutionPath(WebRepository repository) =>
            System.IO.Path.Combine(repository.Root, repository.Kind == WebRepositoryKind.Core ? ProjectNaming.CoreSolutionFileName : ProjectNaming.ModuleSolutionFileName(repository.Id));

        /// <summary>
        /// The project the solution starts (its startup project, set by the command once the solution is open - Visual
        /// Studio keeps it per user, not in the <c>.slnx</c>): the core's <c>kubuno-core</c> when the core is in the
        /// solution, else the first module's backend. Never a library. Relative to the solution folder, backslashes (the
        /// form <c>SolutionBuild.StartupProjects</c> takes). Null when no repository has a package to run.
        /// </summary>
        public static string? StartupProject(IReadOnlyList<WebRepository> repositories, string solutionPath)
        {
            var solutionDirectory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(solutionPath))!;
            foreach (var repository in repositories.OrderBy(repository => repository.Kind == WebRepositoryKind.Core ? 0 : 1))
            {
                var run = repository.Members.FirstOrDefault(member => member.PackageName == repository.RunPackage && !member.IsLibraryOnly);
                if (run is not null)
                {
                    // A checkout whose projects were generated before the 2026-10 naming keeps its package-named project.
                    var project = System.IO.Path.Combine(run.Directory, RsprojName(repository, run) + ".rsproj");
                    var legacy = System.IO.Path.Combine(run.Directory, run.PackageName + ".rsproj");
                    if (!File.Exists(project) && File.Exists(legacy))
                    {
                        project = legacy;
                    }

                    return Relative(solutionDirectory, project).Replace('/', '\\');
                }
            }

            // A platform-layout module without a server to run: its desktop app's Windows program.
            foreach (var repository in repositories.Where(repository => repository.IsPlatformLayout))
            {
                if (DesktopStartupProject(repository) is { } desktop)
                {
                    return Relative(solutionDirectory, desktop).Replace('/', '\\');
                }
            }

            return null;
        }

        /// <summary>
        /// The desktop startup project of a platform-layout repository (docs/WEB.md, "Solutions"): the program under
        /// <c>desktop/windows/</c> - <c>kubuno-&lt;id&gt;-desktop</c> for a module, Kubuno Desktop (<c>kubuno-desktop-shell</c>)
        /// for the core, when there are several, else the first by name. Its <c>.rsproj</c> path, or null when the repository
        /// has no Windows desktop program.
        /// </summary>
        public static string? DesktopStartupProject(WebRepository repository)
        {
            if (repository is null || !repository.IsPlatformLayout)
            {
                return null;
            }

            var windows = System.IO.Path.Combine(repository.Root, "desktop", "windows") + System.IO.Path.DirectorySeparatorChar;
            var programs = repository.DesktopMembers
                .Where(member => !member.IsLibraryOnly && (member.Directory + System.IO.Path.DirectorySeparatorChar).StartsWith(windows, StringComparison.OrdinalIgnoreCase))
                .OrderBy(member => member.PackageName == DesktopProgramPackage(repository) ? 0 : 1)
                .ThenBy(member => member.PackageName, StringComparer.Ordinal)
                .ToList();
            return programs.Count == 0 ? null : System.IO.Path.Combine(programs[0].Directory, ProjectNaming.ForCrate(programs[0].PackageName) + ".rsproj");
        }

        /// <summary>The package of a repository's main desktop program: Kubuno Desktop for the core, <c>kubuno-&lt;id&gt;-desktop</c> for a module.</summary>
        public static string DesktopProgramPackage(WebRepository repository) =>
            repository.Kind == WebRepositoryKind.Core ? "kubuno-desktop-shell" : "kubuno-" + repository.Id + "-desktop";

        /// <summary>
        /// Every file for <paramref name="repositories"/> in one solution at <paramref name="solutionPath"/> (a single
        /// repository, or the multi-repository solution: core + chosen modules). Each repository's projects go to its
        /// Server / Libraries / Frontend / npm packages folders (under <c>/&lt;repository&gt;/</c> in a multi-repository solution).
        /// Build dependencies only ever join projects of the same repository (<see cref="ModuleIsolation"/>): a module's
        /// backend after its frontend, the core's host app after the <c>@kubuno/*</c> packages.
        /// </summary>
        /// <exception cref="InvalidOperationException">A module of a multi-repository solution reaches outside itself.</exception>
        public static IReadOnlyList<GeneratedFile> Plan(IReadOnlyList<WebRepository> repositories, string solutionPath, WebSdkVersions versions, string? existingSolution)
        {
            if (repositories is null || repositories.Count == 0)
            {
                throw new ArgumentException("No repository to generate.", nameof(repositories));
            }

            var files = new List<GeneratedFile>();
            var solutionDirectory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(solutionPath))!;
            var entries = new List<SolutionEntry>();
            var launchProjects = new List<(string Name, IReadOnlyList<string> Paths)>();
            var dependencies = new List<(string From, string To)>();
            var multi = repositories.Count > 1;

            foreach (var repository in repositories)
            {
                var owner = multi ? repository.Name : string.Empty;
                var platform = repository.IsPlatformLayout;

                // The former layout: Server / Libraries / Frontend / Desktop / npm packages. The platform layout: a folder per
                // part of the repository, by the project's path (PlatformLayout: Common, Server, Web, Desktop/<OS>, Mobile).
                SolutionEntry Entry(string projectPath, string typeGuid, string legacyFolder, bool isAnyCpu)
                {
                    var name = platform ? PlatformLayout.FolderOf(repository.Root, System.IO.Path.GetDirectoryName(projectPath)!) : legacyFolder;
                    return new SolutionEntry(Relative(solutionDirectory, projectPath), typeGuid, (multi ? "/" + repository.Name : string.Empty) + "/" + name + "/", isAnyCpu, owner, platform ? PlatformLayout.Rank(name) : FolderRank(name));
                }

                bool InWorkspace(CargoMember member, int legacyCount) => platform
                    ? repository.WorkspaceSizes.TryGetValue(member.Directory, out var size) && size > 1
                    : legacyCount > 1;

                var workspaceScope = repository.Kind == WebRepositoryKind.Core || repository.Members.Count > 1;

                string? runProject = null;
                foreach (var member in repository.Members)
                {
                    var projectPath = System.IO.Path.Combine(member.Directory, RsprojName(repository, member) + ".rsproj");
                    var isRun = member.PackageName == repository.RunPackage;
                    files.Add(new GeneratedFile(projectPath, Rsproj(member, repository, isRun, platform ? InWorkspace(member, 0) : workspaceScope, versions), mergeable: false));
                    var folder = isRun ? ServerFolder : (member.IsLibraryOnly ? LibrariesFolderName : ServerFolder);
                    entries.Add(Entry(projectPath, RsprojTypeGuid, folder, isAnyCpu: false));
                    if (isRun)
                    {
                        runProject = projectPath;
                    }
                }

                var appProjects = new List<string>();
                var packageProjects = new List<(FrontendProject Frontend, string Path)>();
                foreach (var frontend in repository.Frontends)
                {
                    var projectPath = System.IO.Path.Combine(frontend.Directory, frontend.ProjectName + ".esproj");
                    files.Add(new GeneratedFile(projectPath, Esproj(repository, frontend, versions), mergeable: false));
                    if (frontend.Kind == FrontendKind.App)
                    {
                        files.Add(new GeneratedFile(System.IO.Path.Combine(frontend.Directory, ".kubuno", "launch.json"), LaunchJson(repository), mergeable: false));
                        appProjects.Add(projectPath);
                    }
                    else
                    {
                        packageProjects.Add((frontend, projectPath));
                    }

                    entries.Add(Entry(projectPath, EsprojTypeGuid, frontend.Kind == FrontendKind.App ? FrontendFolder : PackagesFolder, isAnyCpu: true));
                }

                // The module's desktop apps (desktop/<os>, or the platform layout's desktop/ workspace with desktop/common,
                // windows, linux, macos): Kubuno.<Module>.Desktop and its crates, named like the desktop repository's
                // projects (ProjectNaming.ForCrate). No build dependency on the server or the frontend: separate programs.
                foreach (var member in repository.DesktopMembers)
                {
                    var projectPath = System.IO.Path.Combine(member.Directory, ProjectNaming.ForCrate(member.PackageName) + ".rsproj");
                    var what = repository.Kind == WebRepositoryKind.Core ? "Kubuno Desktop (the core's desktop workspace)" : "a desktop app of the module";
                    files.Add(new GeneratedFile(projectPath, ClientRsproj(member, InWorkspace(member, repository.DesktopMembers.Count), versions, what), mergeable: false));
                    entries.Add(Entry(projectPath, RsprojTypeGuid, DesktopFolder, isAnyCpu: false));
                }

                // Platform layout: the mobile apps' Rust packages (mobile/common, android, ios), else mobile/README.md, so
                // the Mobile folder shows where they will go.
                foreach (var member in repository.MobileMembers)
                {
                    var projectPath = System.IO.Path.Combine(member.Directory, ProjectNaming.ForCrate(member.PackageName) + ".rsproj");
                    files.Add(new GeneratedFile(projectPath, ClientRsproj(member, InWorkspace(member, 0), versions, "a mobile app of the module"), mergeable: false));
                    entries.Add(Entry(projectPath, RsprojTypeGuid, PlatformLayout.Mobile, isAnyCpu: false));
                }

                if (repository.MobileReadme is not null)
                {
                    var readme = Entry(repository.MobileReadme, string.Empty, PlatformLayout.Mobile, isAnyCpu: false);
                    readme.IsFile = true;
                    entries.Add(readme);
                }

                var desktopStartup = DesktopStartupProject(repository);

                // F5 on the backend of a module deploys its frontend: build it first.
                if (runProject is not null && repository.Kind == WebRepositoryKind.Module)
                {
                    dependencies.AddRange(appProjects.Select(app => (runProject, app)));
                }

                // The host app after the packages it ships; the core packages one after the other (they share one emit of
                // the host's declarations: ui, then sdk, then drive).
                foreach (var app in appProjects)
                {
                    dependencies.AddRange(packageProjects.Select(package => (app, package.Path)));
                }

                for (var index = 1; index < packageProjects.Count; index++)
                {
                    if (packageProjects[index].Frontend.PackageId is not null && packageProjects[index - 1].Frontend.PackageId is not null)
                    {
                        dependencies.Add((packageProjects[index].Path, packageProjects[index - 1].Path));
                    }
                }

                if (runProject is not null)
                {
                    var server = Relative(solutionDirectory, runProject);
                    var app = appProjects.Count > 0 ? Relative(solutionDirectory, appProjects[0]) : null;
                    if (repository.Kind == WebRepositoryKind.Core)
                    {
                        launchProjects.Add(("Kubuno Core (server)", new List<string> { server }));
                        if (app is not null)
                        {
                            launchProjects.Add(("Kubuno Core (server + Vite)", new List<string> { server, app }));
                        }
                    }
                    else
                    {
                        var paths = new List<string> { server };
                        if (app is not null)
                        {
                            paths.Add(app);
                        }

                        launchProjects.Add((ProjectNaming.Product(repository.Id) + " (Kubuno Core + browser)", paths));
                    }
                }

                // Platform layout: the desktop app too (its Windows program, the desktop startup project).
                if (platform && desktopStartup is not null)
                {
                    var desktopProfile = repository.Kind == WebRepositoryKind.Core ? "Kubuno Desktop (Windows)" : ProjectNaming.Product(repository.Id) + " desktop (Windows)";
                    launchProjects.Add((desktopProfile, new List<string> { Relative(solutionDirectory, desktopStartup) }));
                }
            }

            ModuleIsolation.EnsureIsolated(repositories, dependencies);
            foreach (var (from, to) in dependencies)
            {
                entries.First(entry => entry.Path == Relative(solutionDirectory, from)).Dependencies.Add(Relative(solutionDirectory, to));
            }

            files.Add(new GeneratedFile(solutionPath, existingSolution is null ? FreshSlnx(entries) : MergeSlnx(existingSolution, entries), mergeable: true));
            if (launchProjects.Count > 0)
            {
                files.Add(new GeneratedFile(System.IO.Path.ChangeExtension(solutionPath, ".slnLaunch"), SlnLaunch(launchProjects), mergeable: false));
            }

            return files;
        }

        /// <summary>
        /// Writes what <see cref="Plan"/> returned, the way "Kubuno Web: Generate Solution" does: a project file that exists
        /// is kept, a solution is written when its (merged) content changed. <paramref name="replaceSolutionFiles"/> also
        /// rewrites an existing <c>.slnLaunch</c> (with a solution planned fresh: a repository whose layout changed).
        /// Returns one report line per file ("created", "updated", "current", "kept").
        /// </summary>
        public static IReadOnlyList<string> Apply(IEnumerable<GeneratedFile> files, bool replaceSolutionFiles = false)
        {
            var report = new List<string>();
            foreach (var file in files)
            {
                var exists = File.Exists(file.Path);
                var replace = file.Mergeable || (replaceSolutionFiles && file.Path.EndsWith(".slnLaunch", StringComparison.OrdinalIgnoreCase));
                if (exists && !replace)
                {
                    report.Add("  kept     " + file.Path);
                    continue;
                }

                if (exists && string.Equals(File.ReadAllText(file.Path), file.Content, StringComparison.Ordinal))
                {
                    report.Add("  current  " + file.Path);
                    continue;
                }

                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file.Path)!);
                File.WriteAllText(file.Path, file.Content, new UTF8Encoding(false));
                report.Add((exists ? "  updated  " : "  created  ") + file.Path);
            }

            return report;
        }

        /// <summary>
        /// The projects of a repository as the report shows them: one line per project, "folder: project (kind)".
        /// </summary>
        public static IReadOnlyList<string> Describe(WebRepository repository)
        {
            var lines = new List<string>();
            string FolderName(string directory, string legacyFolder) => repository.IsPlatformLayout ? PlatformLayout.FolderOf(repository.Root, directory) : legacyFolder;
            foreach (var member in repository.Members)
            {
                var role = member.PackageName == repository.RunPackage ? "server" : member.IsLibraryOnly ? "library" : "program";
                lines.Add(FolderName(member.Directory, role == "library" ? LibrariesFolderName : ServerFolder) + ": " + RsprojName(repository, member) + ".rsproj (" + member.PackageName + ", " + role + ")");
            }

            foreach (var frontend in repository.Frontends)
            {
                lines.Add(FolderName(frontend.Directory, frontend.Kind == FrontendKind.App ? FrontendFolder : PackagesFolder) + ": " + frontend.ProjectName + ".esproj ("
                    + (frontend.NpmName ?? "app") + (frontend.LinkedSources.Count > 0 ? ", sources " + string.Join(", ", frontend.LinkedSources.Select(System.IO.Path.GetFileName).Select(name => "src/" + name)) : string.Empty) + ")");
            }

            foreach (var member in repository.DesktopMembers.Concat(repository.MobileMembers))
            {
                lines.Add(FolderName(member.Directory, DesktopFolder) + ": " + ProjectNaming.ForCrate(member.PackageName) + ".rsproj (" + member.PackageName + ", " + (member.IsLibraryOnly ? "library" : "program") + ")");
            }

            if (repository.MobileReadme is not null)
            {
                lines.Add(PlatformLayout.Mobile + ": mobile/README.md (no mobile project yet)");
            }

            foreach (var submodule in repository.Submodules)
            {
                lines.Add("git submodule: " + submodule);
            }

            return lines;
        }

        /// <summary>The <c>.rsproj</c> of a Cargo package; the one F5 starts gets Kubuno.Web.Sdk and its role.</summary>
        public static string Rsproj(CargoMember member, WebRepository repository, bool isRun, bool workspaceScope, WebSdkVersions versions)
        {
            var builder = new StringBuilder();
            builder.Append("<Project Sdk=\"Kubuno.Rust.Sdk/").Append(versions.RustSdk).Append("\">\n");
            if (isRun)
            {
                builder.Append("  <!-- The Kubuno web tooling (docs/WEB.md): F5 starts a dev core");
                builder.Append(repository.Kind == WebRepositoryKind.Module ? " with this module deployed into it" : string.Empty);
                builder.Append(", behind the development database guard. -->\n");
                builder.Append("  <Sdk Name=\"Kubuno.Web.Sdk\" Version=\"").Append(versions.WebSdk).Append("\" />\n");
            }

            builder.Append('\n');
            builder.Append("  <!-- Generated by \"Kubuno Web: Generate Solution\" (docs/WEB.md). This file is yours: it is never overwritten\n");
            builder.Append("       by a later run - edit it freely, or delete it and run the command again for a fresh default. -->\n");
            builder.Append("  <PropertyGroup>\n");
            builder.Append("    <CargoPackage>").Append(Escape(member.PackageName)).Append("</CargoPackage>\n");
            if (isRun && repository.RunBin is not null)
            {
                builder.Append("    <CargoBin>").Append(Escape(repository.RunBin)).Append("</CargoBin>\n");
            }
            else if (!isRun && member.Bins.Count == 1 && member.Bins[0] != member.PackageName)
            {
                builder.Append("    <CargoBin>").Append(Escape(member.Bins[0])).Append("</CargoBin>\n");
            }

            if (workspaceScope)
            {
                builder.Append("    <!-- One `cargo build` of the workspace for every project of it (docs/RSPROJ.md, \"Cargo workspaces\"). -->\n");
                builder.Append("    <CargoBuildScope>Workspace</CargoBuildScope>\n");
            }

            if (isRun)
            {
                builder.Append("    <KubunoWebRole>").Append(repository.Kind == WebRepositoryKind.Core ? "Core" : "Module").Append("</KubunoWebRole>\n");
                if (repository.Kind == WebRepositoryKind.Module && !string.Equals(member.Directory, repository.Root, StringComparison.OrdinalIgnoreCase))
                {
                    // The module's folder (module.toml, frontend, migrations) is not this crate's.
                    builder.Append("    <KubunoModuleDirectory>$([System.IO.Path]::GetFullPath('$(MSBuildThisFileDirectory)")
                        .Append(Escape(Relative(member.Directory, repository.Root + System.IO.Path.DirectorySeparatorChar).TrimEnd('/').Replace('/', '\\'))).Append("'))</KubunoModuleDirectory>\n");
                }
            }

            builder.Append("  </PropertyGroup>\n\n</Project>\n");
            return builder.ToString();
        }

        /// <summary>
        /// The <c>.rsproj</c> of a desktop app's package (<see cref="WebRepository.DesktopMembers"/>): a plain Rust project,
        /// its program for a binary, the whole desktop workspace in one cargo run when it has several packages - the same
        /// project as "Generate Visual Studio Projects" writes in the desktop repository.
        /// </summary>
        public static string DesktopRsproj(CargoMember member, bool workspaceScope, WebSdkVersions versions) =>
            ClientRsproj(member, workspaceScope, versions, "a desktop app of the module");

        /// <summary>The <c>.rsproj</c> of a client app's package (desktop or mobile, <paramref name="what"/> says which).</summary>
        public static string ClientRsproj(CargoMember member, bool workspaceScope, WebSdkVersions versions, string what)
        {
            var builder = new StringBuilder();
            builder.Append("<Project Sdk=\"Kubuno.Rust.Sdk/").Append(versions.RustSdk).Append("\">\n\n");
            builder.Append("  <!-- Generated by \"Kubuno Web: Generate Solution\" (docs/WEB.md): ").Append(what).Append(". This file is\n");
            builder.Append("       yours: it is never overwritten by a later run - edit it freely, or delete it and run the command again. -->\n");
            builder.Append("  <PropertyGroup>\n");
            builder.Append("    <CargoPackage>").Append(Escape(member.PackageName)).Append("</CargoPackage>\n");
            if (member.Bins.Count == 1 && member.Bins[0] != member.PackageName)
            {
                builder.Append("    <CargoBin>").Append(Escape(member.Bins[0])).Append("</CargoBin>\n");
            }

            if (workspaceScope)
            {
                builder.Append("    <!-- One `cargo build` of the ").Append(what.Contains("mobile") ? "mobile" : "desktop").Append(" workspace for every project of it (docs/RSPROJ.md, \"Cargo workspaces\"). -->\n");
                builder.Append("    <CargoBuildScope>Workspace</CargoBuildScope>\n");
            }

            builder.Append("  </PropertyGroup>\n\n</Project>\n");
            return builder.ToString();
        }

        /// <summary>The main app's <c>.esproj</c> (kept for callers of the first version).</summary>
        public static string Esproj(WebRepository repository, WebSdkVersions versions) =>
            Esproj(repository, repository.App ?? throw new InvalidOperationException("No frontend app."), versions);

        /// <summary>A JavaScript project's <c>.esproj</c> (Microsoft.VisualStudio.JavaScript.Sdk + Kubuno.Web.Sdk).</summary>
        public static string Esproj(WebRepository repository, FrontendProject frontend, WebSdkVersions versions)
        {
            var isCore = repository.Kind == WebRepositoryKind.Core;
            var builder = new StringBuilder();
            builder.Append("<Project Sdk=\"Microsoft.VisualStudio.JavaScript.Sdk/").Append(versions.JavaScriptSdk).Append("\">\n");
            builder.Append("  <!-- npm/Vite through the Kubuno tooling (docs/WEB.md, \"Frontends\"): Windows native packages when node_modules\n");
            builder.Append("       was installed by Linux, Node.js from Visual Studio when none is on PATH, no npm install over a foreign tree. -->\n");
            builder.Append("  <Sdk Name=\"Kubuno.Web.Sdk\" Version=\"").Append(versions.WebSdk).Append("\" />\n\n");
            builder.Append("  <!-- Generated by \"Kubuno Web: Generate Solution\" (docs/WEB.md). This file is yours: never overwritten by a later run. -->\n");
            builder.Append("  <PropertyGroup>\n");
            if (frontend.Kind == FrontendKind.Package)
            {
                builder.Append("    <KubunoWebRole>Package</KubunoWebRole>\n");
                if (frontend.PackageId is not null)
                {
                    var host = System.IO.Path.GetFileName(frontend.NodeModulesDirectory ?? "frontend");
                    builder.Append("    <!-- ").Append(Escape(frontend.NpmName ?? frontend.PackageId)).Append(" is built from the host app's sources (core/").Append(host).Append("/packages/build.sh):\n");
                    builder.Append("         the declarations of ").Append(host).Append("/src, then ").Append(frontend.PackageId == "ui" ? "its type tree and the ESM library" : "its type tree")
                        .Append(", in obj\\package. The committed types/ and dist/ are regenerated by build.sh; publishing stays a user action. -->\n");
                    builder.Append("    <KubunoPackageId>").Append(Escape(frontend.PackageId)).Append("</KubunoPackageId>\n");
                }

                if (frontend.NodeModulesDirectory is not null)
                {
                    builder.Append("    <KubunoNodeModulesDirectory>$([System.IO.Path]::GetFullPath('$(MSBuildProjectDirectory)\\")
                        .Append(Escape(Relative(frontend.Directory, frontend.NodeModulesDirectory + System.IO.Path.DirectorySeparatorChar).TrimEnd('/').Replace('/', '\\'))).Append("'))\\</KubunoNodeModulesDirectory>\n");
                }
            }
            else
            {
                builder.Append("    <KubunoWebRole>").Append(isCore ? "CoreFrontend" : "ModuleFrontend").Append("</KubunoWebRole>\n");
                if (!isCore)
                {
                    builder.Append("    <KubunoModuleId>").Append(Escape(repository.Id)).Append("</KubunoModuleId>\n");
                }

                builder.Append("    <!-- F5: ").Append(isCore
                    ? "the Vite dev server (proxying /api, /modules and /ws to the dev core on :8080) and Edge with the script debugger."
                    : "a watching vite build straight into the dev core's copy of this module, and Edge on the module with the script debugger.").Append(" -->\n");
                builder.Append("    <LaunchJsonFolder>.kubuno</LaunchJsonFolder>\n");

                var packageFolders = repository.Frontends
                    .Where(other => other.Kind == FrontendKind.Package)
                    .SelectMany(other => other.LinkedSources.Concat(new[] { other.Directory }))
                    .Where(path => (path + System.IO.Path.DirectorySeparatorChar).StartsWith(frontend.Directory + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    .Select(path => Relative(frontend.Directory, path).TrimEnd('/').Replace('/', '\\') + "\\**")
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (packageFolders.Count > 0)
                {
                    builder.Append("    <!-- Shown by their own projects (npm packages). -->\n");
                    builder.Append("    <DefaultItemExcludes>$(DefaultItemExcludes);").Append(Escape(string.Join(";", packageFolders))).Append("</DefaultItemExcludes>\n");
                }

                // The vitest specs are not declared to Test Explorer (JavaScriptTestFramework): in the test host shared with
                // the Rust adapter, Visual Studio's JavaScript adapter fails to load (System.Text.Json conflict) and needs
                // a Node.js on PATH (docs/WEB.md, "Tests"); they run with `npm run test`.
            }

            builder.Append("  </PropertyGroup>\n");
            if (frontend.LinkedSources.Count > 0)
            {
                builder.Append("\n  <!-- The package's sources, which live in the host app's tree: shown here (and hidden from the app project). -->\n");
                builder.Append("  <ItemGroup>\n");
                foreach (var source in frontend.LinkedSources)
                {
                    var relative = Relative(frontend.Directory, source).TrimEnd('/').Replace('/', '\\');
                    var name = System.IO.Path.GetFileName(source);
                    builder.Append("    <None Include=\"").Append(Escape(relative)).Append("\\**\\*\" Link=\"src\\").Append(Escape(name)).Append("\\%(RecursiveDir)%(Filename)%(Extension)\" />\n");
                }

                builder.Append("  </ItemGroup>\n");
            }

            builder.Append("\n</Project>\n");
            return builder.ToString();
        }

        /// <summary>The frontend's <c>.kubuno/launch.json</c>: Edge and Chrome with Visual Studio's script debugger.</summary>
        public static string LaunchJson(WebRepository repository)
        {
            string url;
            string webRoot;
            string? overrides = null;
            if (repository.Kind == WebRepositoryKind.Core)
            {
                url = ViteDevServerUrl;
                webRoot = "${workspaceFolder}";
            }
            else
            {
                var path = repository.Module?.SidebarPath ?? "/" + repository.Id;
                url = "http://localhost:8080" + (path.StartsWith("/", StringComparison.Ordinal) ? path : "/" + path);
                webRoot = "${workspaceFolder}";
                // The core serves the module's bundle at /modules/<id>/entry.js; F5 deploys its source maps with absolute
                // source paths (SourceMapPaths), so its TypeScript maps to the checkout's src.
                overrides = "\"sourceMapPathOverrides\": { \"http://localhost:8080/modules/" + repository.Id + "/*\": \"${workspaceFolder}/dist/*\" }";
            }

            var builder = new StringBuilder();
            builder.Append("{\n  \"version\": \"0.2.0\",\n  \"configurations\": [\n");
            foreach (var (type, name) in new[] { ("chrome", "Chrome"), ("edge", "Edge") })
            {
                builder.Append("    {\n");
                builder.Append("      \"type\": \"").Append(type).Append("\",\n");
                builder.Append("      \"request\": \"launch\",\n");
                builder.Append("      \"name\": \"").Append(repository.Kind == WebRepositoryKind.Core ? "Kubuno host" : "Kubuno " + repository.Id).Append(" (").Append(name).Append(")\",\n");
                builder.Append("      \"url\": \"").Append(url).Append("\",\n");
                builder.Append("      \"webRoot\": \"").Append(webRoot).Append('"');
                if (overrides is not null)
                {
                    builder.Append(",\n      ").Append(overrides);
                }

                builder.Append("\n    }").Append(type == "edge" ? ",\n" : "\n");
            }

            builder.Append("  ]\n}\n");
            return builder.ToString();
        }

        /// <summary>The shared multi-project launch profiles (<c>.slnLaunch</c>, Visual Studio's own format).</summary>
        public static string SlnLaunch(IReadOnlyList<(string Name, IReadOnlyList<string> Paths)> profiles)
        {
            var builder = new StringBuilder("[\n");
            for (var index = 0; index < profiles.Count; index++)
            {
                var (name, paths) = profiles[index];
                builder.Append("  {\n    \"Name\": \"").Append(name).Append("\",\n    \"Projects\": [\n");
                for (var p = 0; p < paths.Count; p++)
                {
                    builder.Append("      {\n        \"Path\": \"").Append(paths[p].Replace("/", "\\\\")).Append("\",\n        \"Action\": \"Start\"\n      }")
                        .Append(p + 1 < paths.Count ? ",\n" : "\n");
                }

                builder.Append("    ]\n  }").Append(index + 1 < profiles.Count ? ",\n" : "\n");
            }

            return builder.Append("]\n").ToString();
        }

        private static string FreshSlnx(List<SolutionEntry> entries)
        {
            var root = new XElement("Solution",
                new XElement("Configurations", new XElement("Platform", new XAttribute("Name", "x64"))));
            AddEntries(root, entries);
            return Serialize(root);
        }

        private static string MergeSlnx(string existing, List<SolutionEntry> entries)
        {
            var document = XDocument.Parse(existing, LoadOptions.PreserveWhitespace);
            var root = document.Root ?? throw new InvalidOperationException("The solution file has no root element.");
            var present = new HashSet<string>(
                root.Descendants("Project").Concat(root.Descendants("File")).Select(project => Normalize((string?)project.Attribute("Path") ?? string.Empty)),
                StringComparer.OrdinalIgnoreCase);
            var missing = entries.Where(entry => !present.Contains(Normalize(entry.Path))).ToList();
            if (missing.Count == 0)
            {
                return existing;
            }

            var fresh = XDocument.Parse(existing);
            AddEntries(fresh.Root!, missing);
            return Serialize(fresh.Root!);
        }

        private static void AddEntries(XElement root, List<SolutionEntry> entries)
        {
            // Programs before frontends: the first project of a solution is Visual Studio's default startup project.
            // A repository's folders in their order (Rank: the former layout's Server first; the platform layout's Common,
            // Server, Web, Desktop/<OS>, Mobile), a folder's projects before its files.
            foreach (var entry in entries.OrderBy(entry => entry.Owner, StringComparer.Ordinal).ThenBy(entry => entry.Rank).ThenBy(entry => entry.IsFile).ThenBy(entry => entry.IsAnyCpu).ThenBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase))
            {
                var project = new XElement(entry.IsFile ? "File" : "Project", new XAttribute("Path", entry.Path));
                if (entry.IsFile)
                {
                    // A solution item (mobile/README.md): no type, no build.
                }
                else if (entry.IsAnyCpu)
                {
                    // What Visual Studio itself writes for an .esproj in an x64 solution (its type comes from the
                    // extension): mapped to AnyCPU, and <Build /> - without it the mapped project is not built.
                    project.Add(new XElement("Platform", new XAttribute("Project", "AnyCPU")));
                    project.Add(new XElement("Build"));
                }
                else
                {
                    project.Add(new XAttribute("Type", entry.TypeGuid));
                }

                foreach (var dependency in entry.Dependencies)
                {
                    project.Add(new XElement("BuildDependency", new XAttribute("Project", dependency)));
                }

                if (entry.Folder is null)
                {
                    var lastProject = root.Elements("Project").LastOrDefault();
                    if (lastProject is not null)
                    {
                        lastProject.AddAfterSelf(project);
                    }
                    else if (root.Element("Configurations") is { } configurations)
                    {
                        configurations.AddAfterSelf(project);
                    }
                    else
                    {
                        root.AddFirst(project);
                    }

                    continue;
                }

                var folder = root.Elements("Folder").FirstOrDefault(element => (string?)element.Attribute("Name") == entry.Folder);
                if (folder is null)
                {
                    // Nested solution folders (/core/Libraries/, /Desktop/Windows/, /drive/Desktop/Windows/) need every
                    // ancestor declared too, before them.
                    var segments = entry.Folder.Trim('/').Split('/');
                    for (var depth = 1; depth < segments.Length; depth++)
                    {
                        var ancestor = "/" + string.Join("/", segments.Take(depth)) + "/";
                        if (!root.Elements("Folder").Any(element => (string?)element.Attribute("Name") == ancestor))
                        {
                            root.Add(new XElement("Folder", new XAttribute("Name", ancestor)));
                        }
                    }

                    folder = new XElement("Folder", new XAttribute("Name", entry.Folder));
                    root.Add(folder);
                }

                folder.Add(project);
            }
        }

        /// <summary>Former layout - Server, Libraries, Frontend, Desktop, npm packages: the program first (Visual Studio's default startup project).</summary>
        private static int FolderRank(string? folder)
        {
            var name = (folder ?? string.Empty).Trim('/').Split('/').Last();
            return name switch
            {
                ServerFolder => 0,
                LibrariesFolderName => 1,
                FrontendFolder => 2,
                DesktopFolder => 3,
                PackagesFolder => 4,
                _ => 5,
            };
        }

        private static string Serialize(XElement root)
        {
            var settings = new System.Xml.XmlWriterSettings { OmitXmlDeclaration = true, Indent = true, IndentChars = "  ", NewLineChars = "\n" };
            var builder = new StringBuilder();
            using (var writer = System.Xml.XmlWriter.Create(builder, settings))
            {
                root.WriteTo(writer);
            }

            return builder.Append('\n').ToString();
        }

        private static string Relative(string fromDirectory, string path)
        {
            var from = new Uri(fromDirectory.TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar);
            var to = new Uri(System.IO.Path.GetFullPath(path));
            return Uri.UnescapeDataString(from.MakeRelativeUri(to).ToString());
        }

        private static string Normalize(string path) => path.Replace('\\', '/');

        private static string Escape(string value) => SecurityElement.Escape(value) ?? value;

        private sealed class SolutionEntry
        {
            public SolutionEntry(string path, string typeGuid, string? folder, bool isAnyCpu, string owner, int rank)
            {
                Path = path;
                TypeGuid = typeGuid;
                Folder = folder;
                IsAnyCpu = isAnyCpu;
                Owner = owner;
                Rank = rank;
            }

            /// <summary>The repository the entry belongs to in a multi-repository solution (empty in a single-repository one).</summary>
            public string Owner { get; }

            /// <summary>The position of its folder among the repository's folders.</summary>
            public int Rank { get; }

            /// <summary>A solution item (<c>&lt;File&gt;</c>) rather than a project.</summary>
            public bool IsFile { get; set; }

            public string Path { get; }

            public string TypeGuid { get; }

            public string? Folder { get; }

            public bool IsAnyCpu { get; }

            public List<string> Dependencies { get; } = new List<string>();
        }
    }
}
