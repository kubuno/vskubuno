using System;
using System.ComponentModel.Design;
using System.IO;
using System.Threading.Tasks;
using EnvDTE;
using Kubuno.Cargo.Commands;
using Kubuno.Cargo.Diagnostics;
using Kubuno.Cargo.Processes;
using Kubuno.Launch;
using Kubuno.VisualStudio.Core;
using Kubuno.VisualStudio.Logging;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;

namespace Kubuno.VisualStudio.Debugging
{
    /// <summary>
    /// "Kubuno: Debug Rust Test at Cursor" (Tools menu - see <c>KubunoCommands.vsct</c>): finds
    /// the <c>#[test]</c> function enclosing the caret in the active <c>.rs</c> document
    /// (<see cref="RustTestLocator"/>), builds its test binary with
    /// <c>cargo test --no-run --message-format=json</c>, and launches it under the native
    /// debugger with <c>&lt;name&gt; --exact --nocapture --test-threads=1</c>
    /// (<see cref="TestLaunchArgs"/>) via <see cref="NativeDebugLauncher"/>.
    /// </summary>
    internal static class DebugRustTestAtCursorCommand
    {
        public static void Initialize(AsyncPackage package, OleMenuCommandService commandService)
        {
            var commandId = new CommandID(PackageGuids.KubunoCommandSet, PackageIds.DebugRustTestAtCursorCommand);
            var command = new OleMenuCommand((sender, args) => Execute(package), commandId);
#pragma warning disable VSTHRD010 // BeforeQueryStatus always fires on the UI thread (OnBeforeQueryStatus itself asserts this); the analyzer can't see that from the event subscription site.
            command.BeforeQueryStatus += (sender, args) => OnBeforeQueryStatus(package, (OleMenuCommand)sender!);
#pragma warning restore VSTHRD010
            commandService.AddCommand(command);
        }

        private static void OnBeforeQueryStatus(AsyncPackage package, OleMenuCommand command)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var path = TryGetActiveDocumentPath(package);
            command.Enabled = path is not null && path.EndsWith(Constants.RustFileExtension, StringComparison.OrdinalIgnoreCase);
        }

        private static void Execute(AsyncPackage package)
        {
            _ = package.JoinableTaskFactory.RunAsync(async () => await ExecuteAsync(package));
        }

        private static async Task ExecuteAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (GetActiveDocumentTextAndPath(package) is not (string text, string filePath))
            {
                ShowMessage(package, "Open a .rs file and place the cursor in or on a #[test] function first.");
                return;
            }

            var caretLine = GetCaretLine(package);
            var testName = RustTestLocator.FindEnclosingTestName(text, caretLine);
            if (testName is null)
            {
                ShowMessage(package, "No #[test] function found at or after the cursor.");
                return;
            }

            var workspaceRoot = CargoWorkspaceLocator.FindWorkspaceRoot(filePath, Directory.Exists, File.Exists);
            if (workspaceRoot is null)
            {
                ShowMessage(package, "Could not find a Cargo.toml above the active file.");
                return;
            }

            await TaskScheduler.Default;

            var manifestPath = Path.Combine(workspaceRoot, Constants.CargoManifestFileName);
            KubunoLog.WriteLine($"Kubuno: building test '{testName}' ({filePath}) via 'cargo test --no-run'...");

            var testBinaryPath = await BuildTestBinaryAsync(manifestPath, workspaceRoot, filePath).ConfigureAwait(false);
            if (testBinaryPath is null)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                ShowMessage(package, $"Could not build or find the test binary for '{testName}'. See the \"Kubuno\" Output pane for details.");
                return;
            }

            var launchProcessRunner = new SystemProcessRunner();
            var sysrootResult = RustToolchain.GetSysroot(launchProcessRunner, workspaceRoot);
            var hostTripleResult = RustToolchain.GetHostTriple(launchProcessRunner, workspaceRoot);
            if (!sysrootResult.Succeeded || !hostTripleResult.Succeeded)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                ShowMessage(package, $"Could not resolve the Rust toolchain: {sysrootResult.Error ?? hostTripleResult.Error}");
                return;
            }

            var target = new LaunchTarget(
                Package: string.Empty,
                Name: testName,
                Kind: LaunchTargetKind.Test,
                Profile: "dev",
                TargetDir: CargoLayout.ResolveTargetDir(workspaceRoot, Environment.GetEnvironmentVariable("CARGO_TARGET_DIR")),
                WorkspaceRoot: workspaceRoot,
                TestBinaryPath: testBinaryPath);

            var description = LaunchDescriptionBuilder.Build(
                target,
                sysrootResult.Sysroot!,
                hostTripleResult.HostTriple!,
                testArgs: TestLaunchArgs.Build(testName),
                existingPath: Environment.GetEnvironmentVariable("PATH"));

            await NativeDebugLauncher.LaunchAsync(description, package).ConfigureAwait(false);
        }

        /// <summary>
        /// Runs `cargo test --no-run --message-format=json`, logs every diagnostic/artifact line
        /// to the "Kubuno" Output pane, and returns the executable path of the test target whose
        /// source file is <paramref name="activeFilePath"/> (matching `CargoArtifact.Target.SrcPath`
        /// - exact for both a `mod tests` inside the crate's own `lib.rs`/`main.rs` and a
        /// `tests/*.rs` integration test, since the active file *is* the target's entry file in
        /// both cases), or null when the build failed or no matching artifact was produced.
        /// </summary>
        private static async Task<string?> BuildTestBinaryAsync(string manifestPath, string workspaceRoot, string activeFilePath)
        {
            var commandLine = CargoCommand.Test()
                .WithManifestPath(manifestPath)
                .WithMessageFormat("json")
                .WithExtraArgs("--no-run")
                .ToCommandLine();

            var runner = new ProcessRunner();
            var request = new ProcessRunRequest(commandLine.FileName, commandLine.Arguments) { WorkingDirectory = workspaceRoot };

            Kubuno.Cargo.Processes.ProcessRunResult result;
            try
            {
                result = await runner.RunAsync(request, onOutput: null, default).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                KubunoLog.WriteException("cargo test --no-run failed to start", exception);
                return null;
            }

            var normalizedActiveFile = NormalizePath(activeFilePath);
            string? matchedExecutable = null;

            foreach (var line in result.StandardOutputLines)
            {
                var buildEvent = CargoMessageParser.Parse(line, workspaceRoot);
                switch (buildEvent)
                {
                    case CargoDiagnosticEvent diagnosticEvent:
                        KubunoLog.WriteLine($"[cargo test] {diagnosticEvent.Diagnostic}");
                        break;
                    case CargoArtifactEvent artifactEvent:
                        var artifact = artifactEvent.Artifact;
                        if (artifact.Target.Test && artifact.Executable is not null &&
                            NormalizePath(artifact.Target.SrcPath) == normalizedActiveFile &&
                            (matchedExecutable is null || IsTestHarnessPath(artifact.Executable)))
                        {
                            // `cargo test --no-run` emits a compiler-artifact for a matching
                            // target's *ordinary* build (e.g. a [[bin]] also gets built as part
                            // of the normal dependency graph) as well as for its actual
                            // `--test`-harness build - both share the same Target/SrcPath, so
                            // "Target.Test" alone doesn't disambiguate them (it reflects the
                            // Cargo.toml target definition, constant across both). Cargo always
                            // places the test harness under "…\deps\<name>-<hash>.exe" (unlike
                            // the plain build's "…\<name>.exe"), so prefer that one; once a
                            // deps-path match is found, a later ordinary-build artifact for the
                            // same target must not overwrite it.
                            matchedExecutable = artifact.Executable;
                        }
                        break;
                }
            }

            foreach (var line in result.StandardErrorLines)
            {
                KubunoLog.WriteLine($"[cargo test] {line}");
            }

            if (!result.Succeeded)
            {
                KubunoLog.WriteLine($"Kubuno: 'cargo test --no-run' exited with code {result.ExitCode}.");
                return null;
            }

            return matchedExecutable;
        }

        private static string NormalizePath(string path) =>
            Path.GetFullPath(path).TrimEnd('\\', '/').ToLowerInvariant();

        /// <summary>True for Cargo's own "…\deps\&lt;name&gt;-&lt;hash&gt;.exe" test-harness convention (see the caller's remarks).</summary>
        private static bool IsTestHarnessPath(string executablePath) =>
            executablePath.IndexOf($"{Path.DirectorySeparatorChar}deps{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) >= 0;

        private static (string Text, string FilePath)? GetActiveDocumentTextAndPath(AsyncPackage package)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (ServiceProvider.GlobalProvider.GetService(typeof(DTE)) is not DTE dte || dte.ActiveDocument is null)
            {
                return null;
            }

            var filePath = dte.ActiveDocument.FullName;
            if (!filePath.EndsWith(Constants.RustFileExtension, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (dte.ActiveDocument.Object("TextDocument") is not TextDocument textDocument)
            {
                return null;
            }

            var text = textDocument.StartPoint.CreateEditPoint().GetText(textDocument.EndPoint);
            return (text, filePath);
        }

        private static int GetCaretLine(AsyncPackage package)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (ServiceProvider.GlobalProvider.GetService(typeof(DTE)) is DTE dte && dte.ActiveDocument?.Selection is TextSelection selection)
            {
                // DTE lines are 1-based; RustTestLocator expects a 0-based line.
                return Math.Max(0, selection.ActivePoint.Line - 1);
            }

            return 0;
        }

        private static string? TryGetActiveDocumentPath(AsyncPackage package)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                return (ServiceProvider.GlobalProvider.GetService(typeof(DTE)) as DTE)?.ActiveDocument?.FullName;
            }
            catch (Exception)
            {
                // Best-effort visibility check only.
                return null;
            }
        }

        private static void ShowMessage(AsyncPackage package, string message)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            VsShellUtilities.ShowMessageBox(
                package,
                message,
                "Kubuno",
                OLEMSGICON.OLEMSGICON_INFO,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }
    }
}
