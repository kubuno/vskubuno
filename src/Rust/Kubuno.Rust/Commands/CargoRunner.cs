using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Rust.Cargo.Commands;
using Kubuno.Rust.Cargo.Processes;
using Kubuno.Rust.Logic.SolutionExplorer;
using Kubuno.Shared.Logging;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;

namespace Kubuno.Rust.Commands
{
    /// <summary>
    /// Runs the cargo commands the Dependencies node, the Reference Manager and the crate manager issue
    /// (<c>cargo add/remove/update/doc</c>) off the UI thread, with their output in the "Kubuno" Output
    /// pane (brought to the front, like NuGet's "Package Manager" pane) and a message box on failure.
    /// <c>Cargo.toml</c>/<c>Cargo.lock</c> are only ever changed by cargo itself.
    /// </summary>
    internal static class CargoRunner
    {
        /// <returns>Whether cargo succeeded.</returns>
        public static async Task<bool> RunAsync(CargoCommand command, string description, bool reportFailure = true, CancellationToken cancellationToken = default)
        {
            var result = await RunCapturedAsync(command.ToCommandLine(), description, workingDirectory: null, environment: null, cancellationToken);
            var succeeded = result?.Succeeded == true;
            if (!succeeded && reportFailure)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                ShowWarning(DependenciesText.CargoFailed(description));
            }

            return succeeded;
        }

        /// <summary>Runs a command line, echoing its output to the pane; <see langword="null"/> if it could not start.</summary>
        public static async Task<ProcessRunResult?> RunCapturedAsync(CargoCommandLine line, string description, string? workingDirectory, IReadOnlyDictionary<string, string>? environment = null, CancellationToken cancellationToken = default)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            KubunoLog.Activate();
            await TaskScheduler.Default;

            KubunoLog.WriteLine($"Kubuno: {description}...");
            try
            {
                var result = await new ProcessRunner().RunAsync(
                    new ProcessRunRequest(line.FileName, line.Arguments) { WorkingDirectory = workingDirectory, EnvironmentVariables = environment },
                    onOutput: new InOrderProgress(l => KubunoLog.WriteLine($"Kubuno:   {l.Text}")),
                    cancellationToken);
                KubunoLog.WriteLine(result.Succeeded
                    ? $"Kubuno:   {description}: done."
                    : $"Kubuno:   {description} failed (exit code {result.ExitCode}).");
                return result;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                KubunoLog.WriteException($"Kubuno: {description}", exception);
                return null;
            }
        }

        public static void ShowWarning(string message)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Show(message, OLEMSGICON.OLEMSGICON_WARNING);
        }

        public static void ShowInfo(string message)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Show(message, OLEMSGICON.OLEMSGICON_INFO);
        }

        /// <summary>Reports on the calling thread (unlike <see cref="Progress{T}"/>), so cargo's lines keep their order.</summary>
        private sealed class InOrderProgress : IProgress<ProcessOutputLine>
        {
            private readonly Action<ProcessOutputLine> _report;

            public InOrderProgress(Action<ProcessOutputLine> report) => _report = report;

            public void Report(ProcessOutputLine value) => _report(value);
        }

        private static void Show(string message, OLEMSGICON icon)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            VsShellUtilities.ShowMessageBox(
                ServiceProvider.GlobalProvider,
                message,
                "Kubuno",
                icon,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }
    }
}
