using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Kubuno.Core;
using Kubuno.Core.Logging;
using Kubuno.Web.Logic.Generation;
using Kubuno.Web.Logic.Versions;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;

namespace Kubuno.Web.Commands
{
    /// <summary>
    /// The version tools of the polyrepo (docs/WEB.md, "Version tools"), under Tools: the read-only audit
    /// (<c>_tools/check_versions.py</c> when a real Python is installed, else its built-in subset,
    /// <see cref="VersionAudit"/>), and the two "prepare" actions - the file edits of <c>bump_npm_floors.sh</c> and
    /// <c>bump_shared_crates.sh</c>, shown first and applied on confirmation. Nothing is ever committed, tagged,
    /// pushed or published: those outbound steps stay with the developer.
    /// </summary>
    internal static class VersionCommands
    {
        public static void Initialize(AsyncPackage package, OleMenuCommandService commandService)
        {
#pragma warning disable VSTHRD010 // menu command handlers are always invoked on the UI thread.
            commandService.AddCommand(new OleMenuCommand((_, _) => Run(package, CheckAsync), new CommandID(KubunoGuids.CommandSet, PackageIds.CheckVersionsCommand)));
            commandService.AddCommand(new OleMenuCommand((_, _) => Run(package, PrepareNpmFloorsAsync), new CommandID(KubunoGuids.CommandSet, PackageIds.PrepareNpmFloorsCommand)));
            commandService.AddCommand(new OleMenuCommand((_, _) => Run(package, PrepareSharedCrateTagsAsync), new CommandID(KubunoGuids.CommandSet, PackageIds.PrepareSharedCrateTagsCommand)));
#pragma warning restore VSTHRD010
        }

        private static void Run(AsyncPackage package, Func<AsyncPackage, string, Task> action)
        {
            _ = package.JoinableTaskFactory.RunAsync(async () =>
            {
                await package.JoinableTaskFactory.SwitchToMainThreadAsync();
                var workspace = ResolveWorkspace(package);
                if (workspace is null)
                {
                    return;
                }

                KubunoLog.Activate();
                try
                {
                    await action(package, workspace);
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is InvalidOperationException || exception is JsonException || exception is System.ComponentModel.Win32Exception)
                {
                    KubunoLog.WriteException("Kubuno versions", exception);
                }
            });
        }

        /// <summary>The polyrepo folder: the parent of the current repository, holding <c>core</c> and <c>_tools</c>.</summary>
        private static string? ResolveWorkspace(AsyncPackage package)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var repository = WebUi.ResolveRepository(package, null, quiet: true);
            var workspace = repository is not null ? Path.GetDirectoryName(repository.Root) : null;
            if (workspace is null || !Directory.Exists(Path.Combine(workspace, "core")))
            {
                workspace = WebUi.BrowseFolder(package, "Choose the folder holding the Kubuno repositories (core, _tools, the modules)");
            }

            if (workspace is not null && !Directory.Exists(Path.Combine(workspace, "core")))
            {
                WebUi.ShowMessage(package, "'" + workspace + "' has no core repository.", error: true);
                return null;
            }

            return workspace;
        }

        private static IReadOnlyList<string> Repositories(string workspace) =>
            Directory.GetDirectories(workspace)
                .Where(directory => File.Exists(Path.Combine(directory, "Cargo.toml")) && (File.Exists(Path.Combine(directory, "module.toml")) || Path.GetFileName(directory) == "core"))
                .OrderBy(directory => directory, StringComparer.OrdinalIgnoreCase)
                .ToList();

        private static async Task CheckAsync(AsyncPackage package, string workspace)
        {
            await TaskScheduler.Default;
            var script = Path.Combine(workspace, "_tools", "check_versions.py");
            var python = FindPython();
            if (python is not null && File.Exists(script))
            {
                KubunoLog.WriteLine("Kubuno versions: " + python + " " + script);
                var exit = await RunAsync(python, "\"" + script + "\"", workspace).ConfigureAwait(false);
                KubunoLog.WriteLine("Kubuno versions: check_versions.py exited with " + exit + ".");
                return;
            }

            KubunoLog.WriteLine("Kubuno versions: no Python on this machine - built-in checks (alignment, npm floors, shared crate tags; "
                + "check_versions.py also checks tags, docker/VERSIONS and installed packages on the server).");
            var published = await PublishedNpmVersionsAsync(workspace).ConfigureAwait(false);
            var tags = VersionAudit.LatestTags(await GitLinesAsync(Path.Combine(workspace, "core"), "tag -l").ConfigureAwait(false));
            var findings = new List<VersionFinding>();
            foreach (var repository in Repositories(workspace))
            {
                findings.AddRange(VersionAudit.CheckAlignment(repository));
                findings.AddRange(VersionAudit.CheckNpmFloors(repository, published));
                findings.AddRange(VersionAudit.CheckSharedCrateTags(repository, tags));
            }

            foreach (var finding in findings.Where(finding => finding.Severity != VersionSeverity.Ok))
            {
                KubunoLog.WriteLine("  " + finding);
            }

            KubunoLog.WriteLine("Kubuno versions: " + findings.Count(f => f.Severity == VersionSeverity.Ok) + " OK, "
                + findings.Count(f => f.Severity == VersionSeverity.Warn) + " WARN, " + findings.Count(f => f.Severity == VersionSeverity.Fail) + " FAIL.");
        }

        private static async Task PrepareNpmFloorsAsync(AsyncPackage package, string workspace)
        {
            await TaskScheduler.Default;
            var published = await PublishedNpmVersionsAsync(workspace).ConfigureAwait(false);
            var edits = Repositories(workspace).Where(directory => Path.GetFileName(directory) != "core").SelectMany(directory => VersionAudit.PlanNpmFloors(directory, published)).ToList();
            await ConfirmAndApplyAsync(package, "npm floors", edits, "Then refresh each module's package-lock.json (npm install --package-lock-only) and commit yourself.").ConfigureAwait(false);
        }

        private static async Task PrepareSharedCrateTagsAsync(AsyncPackage package, string workspace)
        {
            await TaskScheduler.Default;
            var tags = VersionAudit.LatestTags(await GitLinesAsync(Path.Combine(workspace, "core"), "tag -l").ConfigureAwait(false));
            var drive = Path.Combine(workspace, "drive");
            if (Directory.Exists(drive))
            {
                var driveTags = VersionAudit.LatestTags(await GitLinesAsync(drive, "tag -l").ConfigureAwait(false));
                if (driveTags.TryGetValue("drive", out var driveTag))
                {
                    tags = tags.Concat(new[] { new KeyValuePair<string, string>("drive", driveTag) }).GroupBy(pair => pair.Key).ToDictionary(group => group.Key, group => group.Last().Value);
                }
            }

            KubunoLog.WriteLine("Kubuno versions: latest shared crate tags (local): " + string.Join(", ", tags.Values.OrderBy(tag => tag, StringComparer.Ordinal)));
            var edits = Repositories(workspace).Where(directory => Path.GetFileName(directory) != "core").SelectMany(directory => VersionAudit.PlanSharedCrateTags(directory, tags)).ToList();
            await ConfirmAndApplyAsync(package, "shared crate tags", edits, "The tags must be pushed before cargo can fetch them; Cargo.lock is refreshed by the next build. Commit yourself.").ConfigureAwait(false);
        }

        private static async Task ConfirmAndApplyAsync(AsyncPackage package, string what, IReadOnlyList<VersionEdit> edits, string after)
        {
            foreach (var edit in edits)
            {
                KubunoLog.WriteLine("  " + edit);
            }

            await package.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (edits.Count == 0)
            {
                WebUi.ShowMessage(package, "Kubuno " + what + ": nothing to change.");
                return;
            }

            var summary = string.Join("\n", edits.Take(15).Select(edit => RepositoryName(edit.File) + ": " + edit.Description));
            if (!WebUi.Ask(package, "Kubuno " + what + " - " + edits.Count + " change(s) (all listed in the \"Kubuno\" Output pane):\n\n" + summary
                + (edits.Count > 15 ? "\n..." : string.Empty) + "\n\nApply them to the files? Nothing is committed or pushed. " + after))
            {
                return;
            }

            var changed = VersionAudit.Apply(edits);
            KubunoLog.WriteLine("Kubuno versions: " + changed.Count + " file(s) changed: " + string.Join(", ", changed) + ". " + after);
        }

        /// <summary>
        /// The published <c>@kubuno/*</c> versions from the npm registry (read-only), else the core's sources
        /// (CLAUDE.md: <c>core/frontend/packages/*/package.json</c> is the source of truth for what gets published).
        /// </summary>
        private static async Task<IReadOnlyDictionary<string, string>> PublishedNpmVersionsAsync(string workspace)
        {
            var source = VersionAudit.SourceNpmVersions(Path.Combine(workspace, "core"));
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            foreach (var library in VersionAudit.NpmLibraries)
            {
                var name = "@kubuno/" + library;
                try
                {
                    var json = await client.GetStringAsync("https://registry.npmjs.org/@kubuno%2f" + library).ConfigureAwait(false);
                    using var document = JsonDocument.Parse(json);
                    var latest = document.RootElement.GetProperty("dist-tags").GetProperty("latest").GetString();
                    if (!string.IsNullOrEmpty(latest))
                    {
                        result[name] = latest!;
                        if (source.TryGetValue(name, out var sourceVersion) && sourceVersion != latest)
                        {
                            KubunoLog.WriteLine("  WARN npm: " + name + " published " + latest + ", sources at " + sourceVersion + " (publish pending?)");
                        }

                        continue;
                    }
                }
                catch (Exception exception) when (exception is HttpRequestException || exception is TaskCanceledException || exception is JsonException || exception is KeyNotFoundException)
                {
                    KubunoLog.WriteLine("Kubuno versions: npm registry unavailable for " + name + " (" + exception.Message + "), using the core's sources.");
                }

                if (source.TryGetValue(name, out var fallback))
                {
                    result[name] = fallback;
                }
            }

            return result;
        }

        /// <summary>The repository a file belongs to (the nearest folder with a Cargo.toml).</summary>
        private static string RepositoryName(string file)
        {
            for (var directory = Path.GetDirectoryName(file); directory is not null; directory = Path.GetDirectoryName(directory))
            {
                if (File.Exists(Path.Combine(directory, "Cargo.toml")))
                {
                    return Path.GetFileName(directory);
                }
            }

            return file;
        }

        private static string? FindPython()
        {
            foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(';'))
            {
                // The WindowsApps "python.exe" is the Store installer stub, not an interpreter.
                if (directory.IndexOf("WindowsApps", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }

                foreach (var name in new[] { "python3.exe", "python.exe", "py.exe" })
                {
                    var candidate = Path.Combine(directory.Trim(), name);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }

            return null;
        }

        private static async Task<IReadOnlyList<string>> GitLinesAsync(string repository, string arguments)
        {
            var lines = new List<string>();
            // A checkout on the server's share belongs to another user: git refuses it unless told it is safe.
            await RunAsync("git", "-c safe.directory=* -C \"" + repository + "\" " + arguments, repository, lines.Add).ConfigureAwait(false);
            return lines;
        }

        private static Task<int> RunAsync(string fileName, string arguments, string workingDirectory, Action<string>? collect = null)
        {
            var completion = new TaskCompletionSource<int>();
            var start = new ProcessStartInfo(fileName, arguments)
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
            };
            var process = new Process { StartInfo = start, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) { if (collect is null) KubunoLog.WriteLine("  " + e.Data); else collect(e.Data); } };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) KubunoLog.WriteLine("  " + e.Data); };
            process.Exited += (_, _) => { process.WaitForExit(); completion.TrySetResult(process.ExitCode); process.Dispose(); };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            return completion.Task;
        }
    }
}
