using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Kubuno.Rust.Cargo.Commands;
using Kubuno.Rust.Cargo.Tools;
using Kubuno.Rust.Logic.SolutionExplorer;
using Kubuno.Core.Logging;
using Kubuno.Rust.SolutionExplorer;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;

namespace Kubuno.Rust.Commands
{
    /// <summary>
    /// What the Dependencies menus do (<see cref="DependenciesNodeCommandTarget"/>): <c>cargo update</c>,
    /// <c>cargo remove</c>, unused-dependency cleanup through cargo-machete or cargo-udeps, and the
    /// "open" commands (docs.rs / the toolchain's local documentation / <c>cargo doc --open</c>, the
    /// crate's source, its folder).
    /// </summary>
    internal static class DependencyCommands
    {
        /// <summary><c>cargo update</c> for the whole lock file, or <c>-p name@version</c> for one crate.</summary>
        public static async Task UpdateAsync(RsprojProjectContext context, DependencyItem? item)
        {
            var command = CargoCommand.Update().WithManifestPath(context.ManifestPath);
            var description = "cargo update";
            if (item != null)
            {
                var spec = item.ResolvedVersion != null ? $"{item.Name}@{item.ResolvedVersion}" : item.Name;
                command = command.WithPackage(spec);
                description += " -p " + spec;
            }

            await CargoRunner.RunAsync(command, description);
            await ReloadAsync(context);
        }

        /// <summary><c>cargo remove</c>, once per table/target the dependency is declared in.</summary>
        public static async Task RemoveAsync(RsprojProjectContext context, DependencyItem item)
        {
            await RemoveCoreAsync(context, item.DisplayName, item.Declarations);
            await ReloadAsync(context);
        }

        public static async Task OpenDocumentationAsync(RsprojProjectContext context, DependencyItem item)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (item.ItemKind != DependencyItemKind.Crate)
            {
                // The toolchain's own documentation: local copy (rust-docs component) when installed.
                var page = item.ItemKind == DependencyItemKind.Toolchain ? "index.html" : Path.Combine(item.Name, "index.html");
                var local = item.Toolchain?.Sysroot is null ? null : Path.Combine(item.Toolchain.Sysroot, "share", "doc", "rust", "html", page);
                OpenUrl(local != null && File.Exists(local)
                    ? new Uri(local).AbsoluteUri
                    : "https://doc.rust-lang.org/" + (item.ItemKind == DependencyItemKind.Toolchain ? string.Empty : item.Name + "/"));
                return;
            }

            if (item.IsCratesIo)
            {
                OpenUrl($"https://docs.rs/{item.Name}/{item.ResolvedVersion ?? "latest"}");
                return;
            }

            if (!string.IsNullOrEmpty(item.Package?.Documentation))
            {
                OpenUrl(item.Package!.Documentation!);
                return;
            }

            // A path/git crate (or another registry): build its documentation locally and open it.
            var spec = item.ResolvedVersion != null ? $"{item.Name}@{item.ResolvedVersion}" : item.Name;
            await CargoRunner.RunAsync(
                CargoCommand.Doc().WithManifestPath(context.ManifestPath).WithPackage(spec).WithExtraArgs("--no-deps", "--open"),
                $"cargo doc -p {spec} --open");
        }

        /// <summary>Opens the crate's library root in the editor (else its folder).</summary>
        public static void OpenSource(DependencyItem item)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var file = item.LibrarySourcePath;
            if (file != null && File.Exists(file))
            {
                VsShellUtilities.OpenDocument(ServiceProvider.GlobalProvider, file);
                return;
            }

            OpenFolder(item);
        }

        public static void CopyPath(DependencyItem item)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (item.Directory is null)
            {
                return;
            }

            try
            {
                Clipboard.SetText(item.Directory);
            }
            catch (Exception exception)
            {
                KubunoLog.WriteException("Dependencies: copying the path", exception);
            }
        }

        public static void OpenFolder(DependencyItem item)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var folder = item.Directory;
            if (folder is null || !Directory.Exists(folder))
            {
                CargoRunner.ShowInfo(DependenciesText.NoFolder);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + folder + "\"") { UseShellExecute = true });
            }
            catch (Exception exception)
            {
                KubunoLog.WriteException("Dependencies: opening the folder", exception);
            }
        }

        /// <summary>
        /// "Remove Unused Dependencies...": asks cargo-machete (else cargo-udeps on nightly) which
        /// dependencies of this package look unused, lets the user pick, then <c>cargo remove</c>s them.
        /// </summary>
        public static async Task RemoveUnusedAsync(RsprojProjectContext context, DependencyTreeModel model)
        {
            var (tool, found) = await FindUnusedAsync(context);
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (tool is null)
            {
                CargoRunner.ShowWarning(DependenciesText.NoUnusedTool);
                return;
            }

            if (found is null)
            {
                CargoRunner.ShowWarning(DependenciesText.UnusedToolFailed(tool));
                return;
            }

            if (found.Count == 0)
            {
                CargoRunner.ShowInfo(DependenciesText.NoUnusedFound);
                return;
            }

            var dialog = new UnusedDependenciesDialog(tool, found);
            if (dialog.ShowModal() != true)
            {
                return;
            }

            foreach (var name in dialog.SelectedNames)
            {
                var item = model.AllItems.FirstOrDefault(i => i.ItemKind == DependencyItemKind.Crate && !i.IsTransitive
                    && (string.Equals(i.DisplayName, name, StringComparison.Ordinal) || string.Equals(i.DisplayName.Replace('-', '_'), name.Replace('-', '_'), StringComparison.Ordinal)));
                await RemoveCoreAsync(context, item?.DisplayName ?? name, item?.Declarations ?? Array.Empty<Kubuno.Rust.Cargo.Metadata.CargoDependency>());
            }

            await ReloadAsync(context);
        }

        private static async Task RemoveCoreAsync(RsprojProjectContext context, string localName, IReadOnlyList<Kubuno.Rust.Cargo.Metadata.CargoDependency> declarations)
        {
            foreach (var command in CrateInstallPlanner.Uninstall(context.ManifestPath, context.PackageName, localName, declarations))
            {
                await CargoRunner.RunAsync(command, CrateInstallPlanner.Describe(command));
            }
        }

        /// <returns>The tool used (<see langword="null"/> if none is installed) and its findings for this package (<see langword="null"/> if it failed).</returns>
        private static async Task<(string? Tool, IReadOnlyList<string>? Unused)> FindUnusedAsync(RsprojProjectContext context)
        {
            var directory = context.ProjectDirectory;
            var machete = FindCargoSubcommand("machete") is null ? null : await CargoRunner.RunCapturedAsync(
                new CargoCommandLine("cargo", new[] { "machete", "--skip-target-dir", directory }),
                "cargo machete",
                directory);
            if (machete != null && !IsMissingSubcommand(machete))
            {
                if (machete.ExitCode != 0 && machete.ExitCode != 1)
                {
                    return ("cargo-machete", null);
                }

                var reports = CargoMacheteOutputParser.Parse(machete.StandardOutputLines.Concat(machete.StandardErrorLines));
                return ("cargo-machete", reports.Where(r => SameManifest(r.ManifestPath, directory, context.ManifestPath)).SelectMany(r => r.Dependencies).Distinct().ToList());
            }

            // cargo-udeps needs nightly. Never let "cargo +nightly" make rustup download a toolchain:
            // only run it when both are already installed, and forbid rustup's auto-install anyway.
            if (FindCargoSubcommand("udeps") is null || !await IsNightlyInstalledAsync(directory))
            {
                return (null, null);
            }

            var args = new List<string> { "+nightly", "udeps", "--output", "json", "--manifest-path", context.ManifestPath, "--all-targets" };
            if (!string.IsNullOrEmpty(context.PackageName))
            {
                args.Add("-p");
                args.Add(context.PackageName!);
            }

            var udeps = await CargoRunner.RunCapturedAsync(new CargoCommandLine("cargo", args), "cargo +nightly udeps", directory, NoAutoInstall);
            if (udeps is null || IsMissingSubcommand(udeps))
            {
                return (null, null);
            }

            try
            {
                var reports = CargoUdepsOutputParser.Parse(string.Join("\n", udeps.StandardOutputLines));
                if (udeps.ExitCode != 0 && reports.Count == 0)
                {
                    return ("cargo-udeps", null);
                }

                return ("cargo-udeps", reports.Where(r => SameManifest(r.ManifestPath, directory, context.ManifestPath)).SelectMany(r => r.Dependencies).Distinct().ToList());
            }
            catch (System.Text.Json.JsonException)
            {
                return ("cargo-udeps", null);
            }
        }

        private static readonly IReadOnlyDictionary<string, string> NoAutoInstall = new Dictionary<string, string> { ["RUSTUP_AUTO_INSTALL"] = "0" };

        /// <summary>The <c>cargo-NAME.exe</c> a <c>cargo NAME</c> subcommand would run, from PATH or cargo's bin folder.</summary>
        private static string? FindCargoSubcommand(string name)
        {
            var file = "cargo-" + name + ".exe";
            var folders = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator).ToList();
            var cargoHome = Environment.GetEnvironmentVariable("CARGO_HOME");
            folders.Add(Path.Combine(string.IsNullOrEmpty(cargoHome) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cargo") : cargoHome!, "bin"));
            foreach (var folder in folders.Where(f => f.Trim().Length > 0))
            {
                try
                {
                    var candidate = Path.Combine(folder.Trim().Trim('"'), file);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                catch (ArgumentException)
                {
                    // A malformed PATH entry.
                }
            }

            return null;
        }

        private static async Task<bool> IsNightlyInstalledAsync(string directory)
        {
            var result = await CargoRunner.RunCapturedAsync(new CargoCommandLine("rustup", new[] { "toolchain", "list" }), "rustup toolchain list", directory, NoAutoInstall);
            return result?.Succeeded == true && result.StandardOutputLines.Any(l => l.TrimStart().StartsWith("nightly", StringComparison.Ordinal));
        }

        private static bool IsMissingSubcommand(Kubuno.Rust.Cargo.Processes.ProcessRunResult result) =>
            result.ExitCode != 0 && result.StandardErrorLines.Concat(result.StandardOutputLines)
                .Any(l => l.IndexOf("no such command", StringComparison.OrdinalIgnoreCase) >= 0
                    || l.IndexOf("toolchain 'nightly", StringComparison.OrdinalIgnoreCase) >= 0
                    || l.IndexOf("is not installed", StringComparison.OrdinalIgnoreCase) >= 0);

        private static bool SameManifest(string reported, string baseDirectory, string manifestPath)
        {
            try
            {
                var full = Path.GetFullPath(Path.IsPathRooted(reported) ? reported : Path.Combine(baseDirectory, reported));
                return string.Equals(full, Path.GetFullPath(manifestPath), StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void OpenUrl(string url)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                VsShellUtilities.OpenSystemBrowser(url);
            }
            catch (Exception exception)
            {
                KubunoLog.WriteException("Dependencies: opening " + url, exception);
            }
        }

        private static async Task ReloadAsync(RsprojProjectContext context)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            ProjectDependenciesSource.For(context.Hierarchy)?.Reload();
        }
    }
}
