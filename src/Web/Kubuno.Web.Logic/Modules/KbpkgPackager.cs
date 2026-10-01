using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Kubuno.Web.Logic.Modules
{
    /// <summary>
    /// The Windows port of a module's <c>build_kbpkg.sh</c> packaging step (the build itself is the solution's): a ZIP
    /// whose root is the module folder as the core expects it on disk - the executable named by
    /// <c>[process] entrypoint</c>, <c>module.toml</c>, <c>frontend/</c> (the built <c>frontend/dist</c>),
    /// <c>migrations/*.sql</c>, <c>config.toml.example</c>, <c>LICENSE</c>, <c>CHANGELOG.md</c> - plus
    /// <c>SHA256SUMS</c> in <c>sha256sum</c>'s format, written to <c>dist\&lt;id&gt;-&lt;version&gt;-windows-x86_64.kbpkg</c>.
    /// </summary>
    public static class KbpkgPackager
    {
        /// <summary>The files of the package: archive path (forward slashes) to source file, in the archive's order.</summary>
        public static IReadOnlyList<KeyValuePair<string, string>> PlanEntries(string moduleDirectory, string executable, ModuleManifest manifest)
        {
            if (manifest is null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            var entries = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>(manifest.WindowsExecutableName, executable),
                new KeyValuePair<string, string>("module.toml", Path.Combine(moduleDirectory, "module.toml")),
            };

            var dist = ModuleDeployment.FrontendDist(moduleDirectory);
            if (ModuleDeployment.HasFrontend(moduleDirectory) && dist is null)
            {
                throw new InvalidOperationException("frontend/dist not found: build the frontend first (the module has a frontend/package.json).");
            }

            if (dist is not null)
            {
                foreach (var file in Directory.GetFiles(dist, "*", SearchOption.AllDirectories))
                {
                    entries.Add(new KeyValuePair<string, string>("frontend/" + Relative(dist, file), file));
                }
            }

            var migrations = Path.Combine(moduleDirectory, "migrations");
            if (Directory.Exists(migrations))
            {
                foreach (var sql in Directory.GetFiles(migrations, "*.sql", SearchOption.TopDirectoryOnly))
                {
                    entries.Add(new KeyValuePair<string, string>("migrations/" + Path.GetFileName(sql), sql));
                }
            }

            foreach (var name in new[] { "config.toml.example", "LICENSE", "CHANGELOG.md" })
            {
                var path = Path.Combine(moduleDirectory, name);
                if (File.Exists(path))
                {
                    entries.Add(new KeyValuePair<string, string>(name, path));
                }
            }

            return entries.OrderBy(entry => "./" + entry.Key, StringComparer.Ordinal).ToList();
        }

        /// <summary><c>&lt;id&gt;-&lt;version&gt;-windows-x86_64.kbpkg</c>.</summary>
        public static string FileName(ModuleManifest manifest, string? cargoVersion = null)
        {
            var version = manifest.Version ?? cargoVersion ?? "0.0.0";
            return manifest.Id + "-" + version + "-windows-x86_64.kbpkg";
        }

        /// <summary>Writes the package (replacing an existing one) and returns its path.</summary>
        public static string Pack(string moduleDirectory, string executable, ModuleManifest manifest, string outputDirectory)
        {
            var entries = PlanEntries(moduleDirectory, executable, manifest);
            foreach (var entry in entries)
            {
                if (!File.Exists(entry.Value))
                {
                    throw new FileNotFoundException("Nothing to package at '" + entry.Value + "'.", entry.Value);
                }
            }

            Directory.CreateDirectory(outputDirectory);
            var output = Path.Combine(outputDirectory, FileName(manifest));
            if (File.Exists(output))
            {
                File.Delete(output);
            }

            var sums = new StringBuilder();
            using (var archive = ZipFile.Open(output, ZipArchiveMode.Create))
            {
                foreach (var entry in entries)
                {
                    archive.CreateEntryFromFile(entry.Value, entry.Key, CompressionLevel.Optimal);
                    sums.Append(Sha256(entry.Value)).Append("  ./").Append(entry.Key).Append('\n');
                }

                var sumsEntry = archive.CreateEntry("SHA256SUMS", CompressionLevel.Optimal);
                using var writer = new StreamWriter(sumsEntry.Open(), new UTF8Encoding(false));
                writer.Write(sums.ToString());
            }

            return output;
        }

        /// <summary>Lowercase hexadecimal SHA-256 of a file, as <c>sha256sum</c> prints it.</summary>
        public static string Sha256(string path)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(path);
            var hash = sha.ComputeHash(stream);
            var builder = new StringBuilder(hash.Length * 2);
            foreach (var b in hash)
            {
                builder.Append(b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }

        private static string Relative(string root, string file) =>
            file.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace(Path.DirectorySeparatorChar, '/');
    }
}
