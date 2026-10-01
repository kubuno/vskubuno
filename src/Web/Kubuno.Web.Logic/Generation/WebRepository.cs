using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kubuno.Rust.Cargo.Toml;
using Kubuno.Web.Logic.Modules;

namespace Kubuno.Web.Logic.Generation
{
    /// <summary>What a Kubuno repository is, for the solution generator.</summary>
    public enum WebRepositoryKind
    {
        /// <summary>The core: <c>crates/kubuno-core</c> + the host frontend.</summary>
        Core,

        /// <summary>A module: <c>module.toml</c> at the root.</summary>
        Module,
    }

    /// <summary>
    /// A Kubuno repository as the web solution generator sees it (docs/WEB.md, "Solutions"): its kind, its Cargo
    /// packages (read from the manifests themselves - no <c>cargo metadata</c>, so the command works before anything
    /// was fetched), the package that runs (the core's <c>kubuno-core</c>, a module's <c>kubuno-&lt;id&gt;</c>) and its
    /// frontend folder.
    /// </summary>
    public sealed class WebRepository
    {
        private WebRepository(string root, WebRepositoryKind kind, string name, IReadOnlyList<CargoMember> members, string? runPackage, string? runBin, string? frontendDirectory, ModuleManifest? module)
        {
            Root = root;
            Kind = kind;
            Name = name;
            Members = members;
            RunPackage = runPackage;
            RunBin = runBin;
            FrontendDirectory = frontendDirectory;
            Module = module;
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

        /// <summary><c>&lt;root&gt;\frontend</c> when it holds a package.json.</summary>
        public string? FrontendDirectory { get; }

        public ModuleManifest? Module { get; }

        /// <summary>The module id, or <c>core</c>.</summary>
        public string Id => Module?.Id ?? "core";

        /// <summary>Recognizes <paramref name="directory"/> (or returns null with the reason in <paramref name="reason"/>).</summary>
        public static WebRepository? Detect(string directory, out string? reason)
        {
            reason = null;
            var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
            var cargo = Path.Combine(root, "Cargo.toml");
            if (!File.Exists(cargo))
            {
                reason = "no Cargo.toml in '" + root + "'.";
                return null;
            }

            var members = ReadMembers(root);
            var frontend = Path.Combine(root, "frontend");
            var frontendDirectory = File.Exists(Path.Combine(frontend, "package.json")) ? frontend : null;
            var name = Path.GetFileName(root);

            if (File.Exists(Path.Combine(root, "crates", "kubuno-core", "Cargo.toml")))
            {
                var core = members.FirstOrDefault(member => member.PackageName == "kubuno-core");
                return new WebRepository(root, WebRepositoryKind.Core, name, members, core?.PackageName, core is not null && core.Bins.Contains("kubuno-core") && core.Bins.Count > 1 ? "kubuno-core" : null, frontendDirectory, null);
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

                return new WebRepository(root, WebRepositoryKind.Module, name, members, run?.PackageName, bin, frontendDirectory, manifest);
            }

            reason = "'" + root + "' is neither the Kubuno core (crates/kubuno-core) nor a module (module.toml).";
            return null;
        }

        /// <summary>The root package (when <c>[package]</c> is there) and every <c>[workspace] members</c> entry (no globs: Kubuno lists them).</summary>
        public static IReadOnlyList<CargoMember> ReadMembers(string root)
        {
            var result = new List<CargoMember>();
            var document = TomlDocument.Parse(File.ReadAllText(Path.Combine(root, "Cargo.toml")));
            var rootMember = CargoMember.TryRead(root);
            if (rootMember is not null)
            {
                result.Add(rootMember);
            }

            var members = document.GetValue("workspace", "members")?.AsArray();
            if (members is not null)
            {
                foreach (var value in members)
                {
                    var relative = value.AsString();
                    if (string.IsNullOrWhiteSpace(relative) || relative!.IndexOfAny(new[] { '*', '?' }) >= 0)
                    {
                        continue;
                    }

                    var member = CargoMember.TryRead(Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar))));
                    if (member is not null && result.All(existing => existing.Directory != member.Directory))
                    {
                        result.Add(member);
                    }
                }
            }

            return result;
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
