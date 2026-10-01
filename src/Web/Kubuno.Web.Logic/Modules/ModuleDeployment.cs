using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Kubuno.Web.Logic.Modules
{
    /// <summary>
    /// The Windows port of <c>_tools/deploy_local.sh</c> for a module (docs/WEB.md, "F5 on a module"): the built
    /// executable, <c>module.toml</c> (the core reads the INSTALLED manifest), the top-level <c>migrations/*.sql</c>
    /// and <c>frontend/dist</c> are copied into the module's folder of the dev core - its store folder when it was
    /// installed from a <c>.kbpkg</c>, else <c>modules\&lt;id&gt;</c>. Like the script, nothing is deleted from the
    /// target (stale hashed chunks are harmless).
    /// </summary>
    public static class ModuleDeployment
    {
        /// <param name="moduleDirectory">The module repository (holding module.toml).</param>
        /// <param name="executable">The built module executable.</param>
        /// <param name="targetDirectory">The module's folder in the dev core (DevCoreLayout.ModuleDirectory).</param>
        /// <param name="manifest">The parsed module.toml (gives the executable's deployed name).</param>
        public static IReadOnlyList<DeploymentCopy> Plan(string moduleDirectory, string executable, string targetDirectory, ModuleManifest manifest)
        {
            if (manifest is null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            var copies = new List<DeploymentCopy>
            {
                new DeploymentCopy(executable, Path.Combine(targetDirectory, manifest.WindowsExecutableName)),
                new DeploymentCopy(Path.Combine(moduleDirectory, "module.toml"), Path.Combine(targetDirectory, "module.toml")),
            };

            // The PDB next to the executable (rustc names it after the crate: dashes become underscores), so the attached
            // debugger also finds the symbols of the deployed copy when the build folder is gone.
            var stem = Path.GetFileNameWithoutExtension(executable);
            var pdb = new[] { stem, stem.Replace('-', '_') }
                .Select(name => Path.Combine(Path.GetDirectoryName(executable) ?? string.Empty, name + ".pdb"))
                .FirstOrDefault(File.Exists);
            if (pdb is not null)
            {
                copies.Add(new DeploymentCopy(pdb, Path.Combine(targetDirectory, Path.GetFileName(pdb))));
            }

            var migrations = Path.Combine(moduleDirectory, "migrations");
            if (Directory.Exists(migrations))
            {
                foreach (var sql in Directory.GetFiles(migrations, "*.sql", SearchOption.TopDirectoryOnly).OrderBy(file => file, StringComparer.OrdinalIgnoreCase))
                {
                    copies.Add(new DeploymentCopy(sql, Path.Combine(targetDirectory, "migrations", Path.GetFileName(sql))));
                }
            }

            var dist = FrontendDist(moduleDirectory);
            if (dist is not null)
            {
                foreach (var file in Directory.GetFiles(dist, "*", SearchOption.AllDirectories).OrderBy(file => file, StringComparer.OrdinalIgnoreCase))
                {
                    var relative = file.Substring(dist.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    copies.Add(new DeploymentCopy(file, Path.Combine(targetDirectory, "frontend", relative)));
                }
            }

            return copies;
        }

        /// <summary><c>frontend\dist</c> when the module has a frontend that was built; null for a backend-only module.</summary>
        public static string? FrontendDist(string moduleDirectory)
        {
            var dist = Path.Combine(moduleDirectory, "frontend", "dist");
            return Directory.Exists(dist) ? dist : null;
        }

        /// <summary>Whether the module has a frontend at all (frontend/package.json).</summary>
        public static bool HasFrontend(string moduleDirectory) => File.Exists(Path.Combine(moduleDirectory, "frontend", "package.json"));

        /// <summary>Copies what <see cref="Plan"/> returned, skipping files whose size and time stamp are unchanged. Returns the number copied.</summary>
        public static int Execute(IEnumerable<DeploymentCopy> copies)
        {
            var copied = 0;
            foreach (var copy in copies)
            {
                var source = new FileInfo(copy.Source);
                if (!source.Exists)
                {
                    throw new FileNotFoundException("Nothing to deploy at '" + copy.Source + "'.", copy.Source);
                }

                var target = new FileInfo(copy.Target);
                if (target.Exists && target.Length == source.Length && target.LastWriteTimeUtc == source.LastWriteTimeUtc)
                {
                    continue;
                }

                Directory.CreateDirectory(target.DirectoryName!);
                File.Copy(source.FullName, target.FullName, overwrite: true);
                File.SetLastWriteTimeUtc(target.FullName, source.LastWriteTimeUtc);
                copied++;
            }

            return copied;
        }

        /// <summary>
        /// Stops the processes whose executable lies under <paramref name="directory"/> - modules left running by a
        /// previous debug session (the core started them; stopping the debugger ends the core but not its children),
        /// which would keep the executable locked and the module's port taken. Only that folder is ever touched, never
        /// a core or module installed elsewhere. Returns the stopped process ids.
        /// </summary>
        public static IReadOnlyList<int> StopProcessesUnder(string directory, string? processName = null)
        {
            var stopped = new List<int>();
            var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var candidates = processName is null ? Process.GetProcesses() : Process.GetProcessesByName(processName);
            foreach (var process in candidates)
            {
                using (process)
                {
                    string? path;
                    try
                    {
                        path = process.MainModule?.FileName;
                    }
                    catch (Exception exception) when (exception is InvalidOperationException || exception is System.ComponentModel.Win32Exception || exception is NotSupportedException)
                    {
                        continue; // Exited, or another user's/elevated process: not ours.
                    }

                    if (path is null || !path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    try
                    {
                        process.Kill();
                        process.WaitForExit(5000);
                        stopped.Add(process.Id);
                    }
                    catch (Exception exception) when (exception is InvalidOperationException || exception is System.ComponentModel.Win32Exception)
                    {
                        // Already gone.
                    }
                }
            }

            return stopped;
        }
    }

    /// <summary>One file of a deployment.</summary>
    public sealed class DeploymentCopy
    {
        public DeploymentCopy(string source, string target)
        {
            Source = source;
            Target = target;
        }

        public string Source { get; }

        public string Target { get; }

        public override string ToString() => Source + " -> " + Target;
    }
}
