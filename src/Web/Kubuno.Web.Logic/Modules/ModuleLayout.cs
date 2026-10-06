using System;
using System.IO;

namespace Kubuno.Web.Logic.Modules
{
    /// <summary>
    /// Where a module's files are in its repository, in the former layout (everything at the root: <c>Cargo.toml</c>,
    /// <c>migrations/</c>, <c>config.toml.example</c>, <c>frontend/</c>) and in the platform layout (docs/WEB.md,
    /// "Solutions": the server in <c>server/</c> with its <c>migrations/</c> and <c>config.toml.example</c>, the frontend in
    /// <c>web/</c>, <c>module.toml</c>, <c>LICENSE</c> and <c>CHANGELOG.md</c> still at the root) - both coexist during the
    /// transition, so each file is looked for in the new place first, then in the former one. The deployed and packed
    /// module keeps one layout whatever the repository's: the executable, <c>module.toml</c>, <c>frontend/</c>,
    /// <c>migrations/</c> at its root.
    /// </summary>
    public static class ModuleLayout
    {
        /// <summary>
        /// The module's folder (the one holding <c>module.toml</c>) from its server crate's folder (the Cargo manifest's):
        /// that folder itself, or its parent for a platform-layout <c>server/</c> (or any crate folder whose parent holds
        /// <c>module.toml</c> while it does not). <paramref name="manifestDirectory"/> itself when neither has one.
        /// </summary>
        public static string ModuleDirectory(string manifestDirectory)
        {
            var directory = Path.GetFullPath(manifestDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (File.Exists(Path.Combine(directory, "module.toml")))
            {
                return directory;
            }

            var parent = Path.GetDirectoryName(directory);
            return parent is not null && File.Exists(Path.Combine(parent, "module.toml")) ? parent : directory;
        }

        /// <summary>The server's folder: <c>server/</c> when it holds the server crate (platform layout), else the module's folder.</summary>
        public static string ServerDirectory(string moduleDirectory)
        {
            var server = Path.Combine(moduleDirectory, "server");
            return File.Exists(Path.Combine(server, "Cargo.toml")) ? server : moduleDirectory;
        }

        /// <summary>The frontend's folder: <c>web/</c> when it holds a <c>package.json</c> (platform layout), else <c>frontend/</c>.</summary>
        public static string FrontendDirectory(string moduleDirectory)
        {
            var web = Path.Combine(moduleDirectory, "web");
            return File.Exists(Path.Combine(web, "package.json")) ? web : Path.Combine(moduleDirectory, "frontend");
        }

        /// <summary>The SQL migrations: <c>server/migrations</c>, else <c>migrations</c> (null when neither exists).</summary>
        public static string? MigrationsDirectory(string moduleDirectory) =>
            FirstExisting(Directory.Exists, Path.Combine(moduleDirectory, "server", "migrations"), Path.Combine(moduleDirectory, "migrations"));

        /// <summary>
        /// A file of the module's root in the package (<c>config.toml.example</c>, <c>LICENSE</c>, <c>CHANGELOG.md</c>):
        /// at the module's root, else in <c>server/</c> (null when neither exists). <c>config.toml.example</c> is looked
        /// for in <c>server/</c> first, where the platform layout keeps it.
        /// </summary>
        public static string? RootFile(string moduleDirectory, string name)
        {
            var atRoot = Path.Combine(moduleDirectory, name);
            var inServer = Path.Combine(moduleDirectory, "server", name);
            return string.Equals(name, "config.toml.example", StringComparison.OrdinalIgnoreCase)
                ? FirstExisting(File.Exists, inServer, atRoot)
                : FirstExisting(File.Exists, atRoot, inServer);
        }

        private static string? FirstExisting(Func<string, bool> exists, params string[] candidates)
        {
            foreach (var candidate in candidates)
            {
                if (exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }
    }
}
