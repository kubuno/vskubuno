using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kubuno.Rust.Cargo.Diagnostics;
using Kubuno.Rust.Cargo.Metadata;

namespace Kubuno.Desktop.Logic.DesignSurface
{
    /// <summary>
    /// The project's own crate, as the design surface links it (docs/EVENTS.md EVT-7b, docs/DESIGNER.md
    /// section 15): its <c>#[derive(Component)]</c> / <c>#[derive(UserControl)]</c> controls register
    /// themselves with a static constructor, so a surface that links the crate renders them with their
    /// real <c>on_paint</c> - the way the WinForms designer loads the project's compiled assembly.
    ///
    /// <para>The project is an application (a <c>bin</c>): its crate root is compiled once more by
    /// <c>rustc</c>, as an <c>rlib</c> in the design folder, against the very artifacts of the project's
    /// build (its direct dependencies by path, everything else through <c>deps</c>) - nothing in the
    /// project's target folder is rebuilt. The surface then names it
    /// (<c>--extern &lt;crate&gt;=&lt;rlib&gt; --cfg kubuno_design_project</c>, <c>extern crate &lt;crate&gt; as
    /// _;</c> in a generated file) and links it. The crate's other direct library dependencies are
    /// named too, so controls declared in another crate of the project render as well.</para>
    /// </summary>
    public sealed class DesignProjectCrate
    {
        private readonly HashSet<string> _procMacros;

        private DesignProjectCrate(string crateName, string packageName, string version, string manifestDirectory, string sourcePath, string edition, IReadOnlyList<KeyValuePair<string, string>> externs, HashSet<string> procMacros, IReadOnlyList<string> features, string stampFile, IReadOnlyList<string> authors, string? description)
        {
            _procMacros = procMacros;
            CrateName = crateName;
            PackageName = packageName;
            Version = version;
            ManifestDirectory = manifestDirectory;
            SourcePath = sourcePath;
            Edition = edition;
            Externs = externs;
            Features = features;
            StampFile = stampFile;
            Authors = authors;
            Description = description;
        }

        /// <summary>The crate name (<c>my_app</c>): the bin target's name with <c>-</c> as <c>_</c>.</summary>
        public string CrateName { get; }

        public string PackageName { get; }

        public string Version { get; }

        public string ManifestDirectory { get; }

        /// <summary>The crate root (<c>src\main.rs</c>).</summary>
        public string SourcePath { get; }

        public string Edition { get; }

        /// <summary>Its direct normal dependencies (extern name, compiled library), in the manifest's order.</summary>
        public IReadOnlyList<KeyValuePair<string, string>> Externs { get; }

        /// <summary>The package's enabled features (<c>--cfg feature="…"</c>).</summary>
        public IReadOnlyList<string> Features { get; }

        /// <summary>The project's built program: its write time changes whenever the crate's code does (a design-build input).</summary>
        public string StampFile { get; }

        public IReadOnlyList<string> Authors { get; }

        public string? Description { get; }

        /// <summary>The rlib the design build compiles (<c>lib&lt;crate&gt;.rlib</c>).</summary>
        public string RlibFileName => "lib" + CrateName + ".rlib";

        /// <summary>
        /// The project crate of <paramref name="manifestPath"/>'s package: its bin target (the one named
        /// <paramref name="bin"/>, else the only/first one) as <paramref name="artifacts"/> reported it built,
        /// and its direct dependencies found in <paramref name="metadata"/>'s resolve graph. Null (with the
        /// reason) when the program was not built in this build (e.g. it failed) or a dependency's library
        /// is missing - the surface then renders the project's controls as placeholders.
        /// </summary>
        public static DesignProjectCrate? From(CargoMetadata metadata, IReadOnlyList<CargoArtifact> artifacts, string manifestPath, string? bin, out string? reason)
        {
            var package = metadata.Packages.FirstOrDefault(p => SamePath(p.ManifestPath, manifestPath));
            if (package is null)
            {
                reason = "the project's package is not in cargo metadata";
                return null;
            }

            var binArtifact = artifacts.LastOrDefault(a => a.PackageId == package.Id && a.Target.IsKind("bin") && (bin is null || a.Target.Name == bin));
            if (binArtifact is null)
            {
                reason = "the project's program was not built";
                return null;
            }

            var node = metadata.Resolve?.Find(package.Id);
            if (node is null)
            {
                reason = "the project's package is not in the resolve graph";
                return null;
            }

            var externs = new List<KeyValuePair<string, string>>();
            var procMacros = new HashSet<string>(StringComparer.Ordinal);
            foreach (var dep in node.Deps.Where(d => d.HasKind(null)))
            {
                var lib = artifacts.LastOrDefault(a => a.PackageId == dep.Pkg && IsLibrary(a.Target));
                var file = lib is null ? null : LibraryFile(lib);
                if (file is null)
                {
                    reason = $"the library of dependency `{dep.Name}` was not built";
                    return null;
                }

                externs.Add(new KeyValuePair<string, string>(dep.Name, file));
                if (lib!.Target.IsKind("proc-macro"))
                {
                    procMacros.Add(dep.Name);
                }
            }

            var stamp = binArtifact.Executable ?? binArtifact.Filenames.FirstOrDefault(f => f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) ?? binArtifact.Filenames.FirstOrDefault();
            if (stamp is null)
            {
                reason = "the project's program has no file";
                return null;
            }

            reason = null;
            return new DesignProjectCrate(
                binArtifact.Target.Name.Replace('-', '_'),
                package.Name,
                package.Version,
                Path.GetDirectoryName(package.ManifestPath) ?? string.Empty,
                binArtifact.Target.SrcPath,
                binArtifact.Target.Edition ?? package.Edition ?? "2021",
                externs,
                procMacros,
                node.Features,
                stamp,
                package.Authors,
                package.Description);
        }

        /// <summary>A library a crate can be linked against (not a proc macro: those are found by the compiler through <c>deps</c>).</summary>
        private static bool IsLibrary(CargoTarget target) =>
            target.IsKind("lib") || target.IsKind("rlib") || target.IsKind("dylib") || target.IsKind("proc-macro");

        /// <summary>The file <c>--extern</c> takes for a library artifact: its rlib, a dylib's DLL (its <c>deps</c> copy), a proc macro's DLL.</summary>
        private static string? LibraryFile(CargoArtifact artifact)
        {
            var rlib = artifact.Filenames.FirstOrDefault(f => f.EndsWith(".rlib", StringComparison.OrdinalIgnoreCase));
            if (rlib is not null)
            {
                return rlib;
            }

            var dll = artifact.Filenames.FirstOrDefault(f => f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
            if (dll is null)
            {
                return null;
            }

            // A dylib is reported at its uplifted path; rustc linked (and records) the deps copy.
            if (artifact.Target.IsKind("dylib"))
            {
                var profile = Path.GetDirectoryName(dll) ?? string.Empty;
                var depsCopy = Path.Combine(profile, "deps", Path.GetFileName(dll));
                return Path.GetFileName(profile) == "deps" ? dll : depsCopy;
            }

            return dll;
        }

        private static bool SamePath(string a, string b)
        {
            try
            {
                return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>The direct dependencies the surface names too (their controls then register), proc macros and the three <c>kubuno_*</c> crates excluded.</summary>
        public IEnumerable<string> LinkedDependencies =>
            Externs.Select(e => e.Key)
                .Where(n => !_procMacros.Contains(n))
                .Where(n => n != DesignSurfaceInputs.UiCrate && n != DesignSurfaceInputs.ViewsCrate && n != DesignSurfaceInputs.ControlsCrate);

        /// <summary>
        /// The Rust file the surface includes (<c>KUBUNO_DESIGN_PROJECT_RS</c>): <c>extern crate</c> of the project
        /// crate and of its other library dependencies, which links them (and runs their controls' static
        /// constructors).
        /// </summary>
        public string SurfaceIncludeSource()
        {
            var lines = new List<string>
            {
                "// Generated by the Visual Studio design build (vskubuno docs/EVENTS.md EVT-7b): links the project",
                "// crate and its libraries into the design surface, so their controls register themselves.",
                $"extern crate {CrateName} as _;",
            };
            lines.AddRange(LinkedDependencies.Select(d => $"extern crate {d} as _;"));
            return string.Join("\n", lines) + "\n";
        }

        /// <summary>The environment rustc gets for the crate (what cargo sets for <c>env!</c>): the package's name, version, folder and crate name.</summary>
        public IReadOnlyDictionary<string, string> Environment()
        {
            var env = new Dictionary<string, string>
            {
                ["CARGO_MANIFEST_DIR"] = ManifestDirectory,
                ["CARGO_MANIFEST_PATH"] = Path.Combine(ManifestDirectory, "Cargo.toml"),
                ["CARGO_PKG_NAME"] = PackageName,
                ["CARGO_CRATE_NAME"] = CrateName,
                ["CARGO_BIN_NAME"] = CrateName,
                ["CARGO_PKG_VERSION"] = Version,
                ["CARGO_PKG_AUTHORS"] = string.Join(":", Authors),
                ["CARGO_PKG_DESCRIPTION"] = Description ?? string.Empty,
            };
            var core = Version.Split('-')[0].Split('.');
            env["CARGO_PKG_VERSION_MAJOR"] = core.Length > 0 ? core[0] : "0";
            env["CARGO_PKG_VERSION_MINOR"] = core.Length > 1 ? core[1] : "0";
            env["CARGO_PKG_VERSION_PATCH"] = core.Length > 2 ? core[2] : "0";
            env["CARGO_PKG_VERSION_PRE"] = Version.Contains("-") ? Version.Substring(Version.IndexOf('-') + 1) : string.Empty;
            return env;
        }

        /// <summary><c>rustc</c> arguments compiling the crate root as an rlib into <paramref name="outputRlib"/>.</summary>
        public IReadOnlyList<string> RustcArguments(DesignSurfaceInputs inputs, string outputRlib, string profile)
        {
            var args = new List<string>
            {
                "--edition", Edition,
                "--crate-name", CrateName,
                "--crate-type", "rlib",
                SourcePath,
                "-C", string.Equals(profile, "release", StringComparison.Ordinal) ? "opt-level=3" : "opt-level=0",
                // Full debug info outside release (docs/DEBUGGING.md, "Debugging the design surface"): a custom control's
                // code runs in the surface, and a developer attaching the debugger to kubuno-design-surface.exe gets a PDB.
                "-C", string.Equals(profile, "release", StringComparison.Ordinal) ? "debuginfo=0" : "debuginfo=2",
                "-C", "metadata=kubunodesign",
                "--cap-lints", "allow",
                "-L", "dependency=" + inputs.DepsDirectory,
            };
            foreach (var feature in Features)
            {
                args.Add("--cfg");
                args.Add($"feature=\"{feature}\"");
            }

            foreach (var external in Externs)
            {
                args.Add("--extern");
                args.Add(external.Key + "=" + external.Value);
            }

            args.Add("-o");
            args.Add(outputRlib);
            return args;
        }
    }
}
