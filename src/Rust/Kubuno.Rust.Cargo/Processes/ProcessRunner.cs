using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Kubuno.Rust.Cargo.Processes
{
    /// <summary>Default <see cref="IProcessRunner"/>, backed by <see cref="Process"/>.</summary>
    public sealed class ProcessRunner : IProcessRunner
    {
        public async Task<ProcessRunResult> RunAsync(
            ProcessRunRequest request,
            IProgress<ProcessOutputLine>? onOutput,
            CancellationToken cancellationToken)
        {
            if (request is null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = request.FileName,
                Arguments = request.Arguments,
                WorkingDirectory = request.WorkingDirectory ?? string.Empty,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            if (request.EnvironmentVariables is not null)
            {
                foreach (var variable in request.EnvironmentVariables)
                {
                    startInfo.EnvironmentVariables[variable.Key] = variable.Value;
                }
            }

            var standardOutput = new List<string>();
            var standardError = new List<string>();
            var exitSignal = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

            using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is null)
                {
                    return;
                }
                lock (standardOutput)
                {
                    standardOutput.Add(e.Data);
                }
                onOutput?.Report(new ProcessOutputLine(e.Data, isError: false));
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is null)
                {
                    return;
                }
                lock (standardError)
                {
                    standardError.Add(e.Data);
                }
                onOutput?.Report(new ProcessOutputLine(e.Data, isError: true));
            };
            process.Exited += (_, _) => exitSignal.TrySetResult(process.ExitCode);

            using var cancellationRegistration = cancellationToken.Register(() => TryKill(process));

            if (!process.Start())
            {
                throw new InvalidOperationException($"Failed to start process '{request.FileName}'.");
            }
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            int exitCode = await exitSignal.Task.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            // Exited can be raised before the asynchronous readers have delivered the last lines: the
            // parameterless WaitForExit also waits for both redirected streams to reach end of file.
            // Without it a large single-line output (cargo metadata's JSON) was sometimes lost entirely.
            process.WaitForExit();

            List<string> stdoutCopy;
            List<string> stderrCopy;
            lock (standardOutput)
            {
                stdoutCopy = new List<string>(standardOutput);
            }
            lock (standardError)
            {
                stderrCopy = new List<string>(standardError);
            }

            return new ProcessRunResult(exitCode, stdoutCopy, stderrCopy);
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch (InvalidOperationException)
            {
                // Already exited between the check and the call: nothing to do.
            }
            catch (Win32Exception)
            {
                // The OS refused to terminate it (already gone, access denied, ...): nothing more we can do here.
            }
        }
    }
}
