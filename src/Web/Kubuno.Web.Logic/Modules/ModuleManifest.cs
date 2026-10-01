using System;
using System.IO;
using Kubuno.Rust.Cargo.Toml;

namespace Kubuno.Web.Logic.Modules
{
    /// <summary>
    /// The parts of a module's <c>module.toml</c> the tooling needs (the core's own reader is
    /// core/crates/kubuno-core/src/modules/manifest.rs): the id (also the folder name the core expects and the default
    /// executable <c>kubuno-&lt;id&gt;</c>), the version, the entry point, the port and the first sidebar path (where F5
    /// opens the browser).
    /// </summary>
    public sealed class ModuleManifest
    {
        private ModuleManifest(string id, string? displayName, string? version, string? runtime, string entrypoint, int? port, string? sidebarPath)
        {
            Id = id;
            DisplayName = displayName;
            Version = version;
            Runtime = runtime;
            Entrypoint = entrypoint;
            Port = port;
            SidebarPath = sidebarPath;
        }

        public string Id { get; }

        public string? DisplayName { get; }

        public string? Version { get; }

        /// <summary><c>rust</c> (the default), <c>python</c>, <c>node</c>.</summary>
        public string? Runtime { get; }

        /// <summary><c>[process] entrypoint</c>, <c>kubuno-&lt;id&gt;</c> when absent.</summary>
        public string Entrypoint { get; }

        public int? Port { get; }

        /// <summary>The first <c>[[sidebar_items]] path</c> (<c>/calendar</c>), or null.</summary>
        public string? SidebarPath { get; }

        /// <summary>The executable file name on Windows (the core also tries <c>&lt;entrypoint&gt;.exe</c>).</summary>
        public string WindowsExecutableName => Entrypoint.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? Entrypoint : Entrypoint + ".exe";

        public static ModuleManifest Load(string path) => Parse(File.ReadAllText(path), path);

        /// <exception cref="ModuleManifestException">No <c>[module] id</c>, or invalid TOML.</exception>
        public static ModuleManifest Parse(string text, string? path = null)
        {
            TomlDocument document;
            try
            {
                document = TomlDocument.Parse(text);
            }
            catch (TomlParseException exception)
            {
                throw new ModuleManifestException((path ?? "module.toml") + " is not valid TOML: " + exception.Message, exception);
            }

            var id = document.GetValue("module", "id")?.AsString();
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ModuleManifestException((path ?? "module.toml") + " has no [module] id.");
            }

            var entrypoint = document.GetValue("process", "entrypoint")?.AsString();
            if (string.IsNullOrWhiteSpace(entrypoint))
            {
                entrypoint = "kubuno-" + id;
            }

            var port = document.GetValue("server", "port")?.AsInteger();
            string? sidebarPath = null;
            var sidebar = document.GetArrayOfTables("sidebar_items");
            if (sidebar.Count > 0)
            {
                sidebarPath = sidebar[0].GetValue("path")?.AsString();
            }

            return new ModuleManifest(
                id!.Trim(),
                document.GetValue("module", "display_name")?.AsString(),
                document.GetValue("module", "version")?.AsString(),
                document.GetValue("module", "runtime")?.AsString(),
                entrypoint!.Trim(),
                port is null ? null : (int?)port.Value,
                sidebarPath);
        }
    }

    public sealed class ModuleManifestException : Exception
    {
        public ModuleManifestException(string message, Exception? inner = null)
            : base(message, inner)
        {
        }
    }
}
