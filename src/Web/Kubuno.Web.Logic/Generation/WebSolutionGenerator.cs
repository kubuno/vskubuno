using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Xml.Linq;

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
        public static WebSdkVersions Current { get; } = new WebSdkVersions("1.1.0", "1.0.0", "1.0.6887863");

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

        public const string LibrariesFolder = "/Libraries/";

        /// <summary>The Vite dev server of the core's frontend (vite.config.ts: default port, proxy to :8080).</summary>
        public const string ViteDevServerUrl = "http://localhost:5173";

        /// <summary>The frontend project's file name: <c>kubuno-frontend.esproj</c> for the core, <c>&lt;id&gt;-frontend.esproj</c> for a module.</summary>
        public static string FrontendProjectName(WebRepository repository) =>
            (repository.Kind == WebRepositoryKind.Core ? "kubuno" : repository.Id) + "-frontend";

        /// <summary>
        /// The solution of one repository: <c>Kubuno.Core.Web.slnx</c> for the core - "Kubuno Core Web", the web server, as
        /// opposed to the desktop workspace's <c>Kubuno.Core.Desktop.slnx</c> - and <c>Kubuno.Calendar.slnx</c> for a module.
        /// </summary>
        public static string DefaultSolutionPath(WebRepository repository) =>
            System.IO.Path.Combine(repository.Root, repository.Kind == WebRepositoryKind.Core ? "Kubuno.Core.Web.slnx" : "Kubuno." + Pascal(repository.Id) + ".slnx");

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
                    return Relative(solutionDirectory, System.IO.Path.Combine(run.Directory, run.PackageName + ".rsproj")).Replace('/', '\\');
                }
            }

            return null;
        }

        /// <summary>
        /// Every file for <paramref name="repositories"/> in one solution at <paramref name="solutionPath"/> (a single
        /// repository, or the multi-repository solution: core + chosen modules).
        /// </summary>
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
            var multi = repositories.Count > 1;

            foreach (var repository in repositories)
            {
                string? runProject = null;
                foreach (var member in repository.Members)
                {
                    var projectPath = System.IO.Path.Combine(member.Directory, member.PackageName + ".rsproj");
                    var isRun = member.PackageName == repository.RunPackage;
                    var workspaceScope = repository.Kind == WebRepositoryKind.Core;
                    files.Add(new GeneratedFile(projectPath, Rsproj(member, repository, isRun, workspaceScope, versions), mergeable: false));
                    var folder = member.IsLibraryOnly ? (multi ? "/" + repository.Name + "/Libraries/" : LibrariesFolder) : (multi ? "/" + repository.Name + "/" : null);
                    entries.Add(new SolutionEntry(Relative(solutionDirectory, projectPath), RsprojTypeGuid, folder, isAnyCpu: false));
                    if (isRun)
                    {
                        runProject = projectPath;
                    }
                }

                string? frontendProject = null;
                if (repository.FrontendDirectory is not null)
                {
                    frontendProject = System.IO.Path.Combine(repository.FrontendDirectory, FrontendProjectName(repository) + ".esproj");
                    files.Add(new GeneratedFile(frontendProject, Esproj(repository, versions), mergeable: false));
                    files.Add(new GeneratedFile(System.IO.Path.Combine(repository.FrontendDirectory, ".kubuno", "launch.json"), LaunchJson(repository), mergeable: false));
                    entries.Add(new SolutionEntry(Relative(solutionDirectory, frontendProject), EsprojTypeGuid, multi ? "/" + repository.Name + "/" : null, isAnyCpu: true));
                }

                // F5 on the backend of a module deploys its frontend too: build it first.
                if (runProject is not null && frontendProject is not null && repository.Kind == WebRepositoryKind.Module)
                {
                    var run = entries.First(entry => entry.Path == Relative(solutionDirectory, runProject));
                    run.Dependencies.Add(Relative(solutionDirectory, frontendProject));
                }

                if (runProject is not null)
                {
                    var server = Relative(solutionDirectory, runProject);
                    if (repository.Kind == WebRepositoryKind.Core)
                    {
                        launchProjects.Add(("Kubuno Core Web (serveur)", new List<string> { server }));
                        if (frontendProject is not null)
                        {
                            launchProjects.Add(("Kubuno Core Web (serveur + Vite)", new List<string> { server, Relative(solutionDirectory, frontendProject) }));
                        }
                    }
                    else
                    {
                        var paths = new List<string> { server };
                        if (frontendProject is not null)
                        {
                            paths.Add(Relative(solutionDirectory, frontendProject));
                        }

                        launchProjects.Add((Pascal(repository.Id) + " (Kubuno Core Web + navigateur)", paths));
                    }
                }
            }

            files.Add(new GeneratedFile(solutionPath, existingSolution is null ? FreshSlnx(entries) : MergeSlnx(existingSolution, entries), mergeable: true));
            if (launchProjects.Count > 0)
            {
                files.Add(new GeneratedFile(System.IO.Path.ChangeExtension(solutionPath, ".slnLaunch"), SlnLaunch(launchProjects), mergeable: false));
            }

            return files;
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
            builder.Append("  <!-- Generated by \"Kubuno: Generate Web Solution\" (docs/WEB.md). This file is yours: it is never overwritten\n");
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
            }

            builder.Append("  </PropertyGroup>\n\n</Project>\n");
            return builder.ToString();
        }

        /// <summary>The frontend's <c>.esproj</c> (Microsoft.VisualStudio.JavaScript.Sdk + Kubuno.Web.Sdk).</summary>
        public static string Esproj(WebRepository repository, WebSdkVersions versions)
        {
            var isCore = repository.Kind == WebRepositoryKind.Core;
            var builder = new StringBuilder();
            builder.Append("<Project Sdk=\"Microsoft.VisualStudio.JavaScript.Sdk/").Append(versions.JavaScriptSdk).Append("\">\n");
            builder.Append("  <!-- npm/Vite through the Kubuno tooling (docs/WEB.md, \"Frontends\"): Windows native packages when node_modules\n");
            builder.Append("       was installed by Linux, Node.js from Visual Studio when none is on PATH, no npm install over a foreign tree. -->\n");
            builder.Append("  <Sdk Name=\"Kubuno.Web.Sdk\" Version=\"").Append(versions.WebSdk).Append("\" />\n\n");
            builder.Append("  <!-- Generated by \"Kubuno: Generate Web Solution\" (docs/WEB.md). This file is yours: never overwritten by a later run. -->\n");
            builder.Append("  <PropertyGroup>\n");
            builder.Append("    <KubunoWebRole>").Append(isCore ? "CoreFrontend" : "ModuleFrontend").Append("</KubunoWebRole>\n");
            if (!isCore)
            {
                builder.Append("    <KubunoModuleId>").Append(Escape(repository.Id)).Append("</KubunoModuleId>\n");
            }

            builder.Append("    <!-- F5: ").Append(isCore
                ? "the Vite dev server (proxying /api, /modules and /ws to the dev core on :8080) and Edge with the script debugger."
                : "a watching vite build straight into the dev core's copy of this module, and Edge on the module with the script debugger.").Append(" -->\n");
            builder.Append("    <LaunchJsonFolder>.kubuno</LaunchJsonFolder>\n");
            if (File.Exists(System.IO.Path.Combine(repository.FrontendDirectory!, "vite.config.ts")) && isCore)
            {
                builder.Append("    <!-- Test Explorer: the vitest specs (configured in vite.config.ts). -->\n");
                builder.Append("    <JavaScriptTestFramework>Vitest</JavaScriptTestFramework>\n");
                builder.Append("    <JavaScriptTestRoot>src\\</JavaScriptTestRoot>\n");
            }

            builder.Append("  </PropertyGroup>\n\n</Project>\n");
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
                // The core serves the module's bundle at /modules/<id>/frontend/; its source maps point back into src.
                overrides = "\"sourceMapPathOverrides\": { \"http://localhost:8080/modules/" + repository.Id + "/frontend/*\": \"${workspaceFolder}/dist/*\" }";
            }

            var builder = new StringBuilder();
            builder.Append("{\n  \"version\": \"0.2.0\",\n  \"configurations\": [\n");
            foreach (var (type, name) in new[] { ("edge", "Edge"), ("chrome", "Chrome") })
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
                root.Descendants("Project").Select(project => Normalize((string?)project.Attribute("Path") ?? string.Empty)),
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
            foreach (var entry in entries.OrderBy(entry => entry.Folder ?? string.Empty, StringComparer.Ordinal).ThenBy(entry => entry.IsAnyCpu).ThenBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase))
            {
                var project = new XElement("Project", new XAttribute("Path", entry.Path));
                if (entry.IsAnyCpu)
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
                    // Nested solution folders (/core/Libraries/) need their parent declared too.
                    var parent = entry.Folder.TrimEnd('/');
                    var slash = parent.LastIndexOf('/');
                    if (slash > 0)
                    {
                        var parentName = parent.Substring(0, slash + 1);
                        if (!root.Elements("Folder").Any(element => (string?)element.Attribute("Name") == parentName))
                        {
                            root.Add(new XElement("Folder", new XAttribute("Name", parentName)));
                        }
                    }

                    folder = new XElement("Folder", new XAttribute("Name", entry.Folder));
                    root.Add(folder);
                }

                folder.Add(project);
            }
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

        private static string Pascal(string id) =>
            string.Concat(id.Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries).Select(part => char.ToUpperInvariant(part[0]) + part.Substring(1)));

        private sealed class SolutionEntry
        {
            public SolutionEntry(string path, string typeGuid, string? folder, bool isAnyCpu)
            {
                Path = path;
                TypeGuid = typeGuid;
                Folder = folder;
                IsAnyCpu = isAnyCpu;
            }

            public string Path { get; }

            public string TypeGuid { get; }

            public string? Folder { get; }

            public bool IsAnyCpu { get; }

            public List<string> Dependencies { get; } = new List<string>();
        }
    }
}
