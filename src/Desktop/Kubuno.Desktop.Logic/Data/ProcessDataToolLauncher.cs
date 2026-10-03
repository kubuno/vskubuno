using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Kubuno.Desktop.Logic.Data
{
    /// <summary>
    /// Finds <c>kubuno-data-tool.exe</c>: the extension's own <c>tools\</c> folder (where the VSIX ships it, beside
    /// <c>kubuno-views-ls.exe</c>; both are self-contained, statically linked exes), then a local
    /// development build folder. Never throws; <see langword="null"/> means "not found".
    /// </summary>
    public static class DataToolLocator
    {
        public const string ExeName = "kubuno-data-tool.exe";

        /// <summary>The desktop workspace's documented release output (vskubuno/CLAUDE.md §5).</summary>
        public const string DevBuildDirectory = @"C:\kubuno-build\desktop-target\release";

        public static string? Locate(string? extensionInstallDirectory, string? devBuildDirectory = DevBuildDirectory)
        {
            foreach (var candidate in new[]
            {
                Combine(extensionInstallDirectory, "tools"),
                devBuildDirectory,
            })
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                var path = Combine(candidate, ExeName);
                if (path != null && File.Exists(path))
                {
                    return path;
                }
            }

            return null;
        }

        private static string? Combine(string? first, string second)
        {
            if (string.IsNullOrWhiteSpace(first))
            {
                return null;
            }

            try
            {
                return Path.Combine(first, second);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Starts <c>kubuno-data-tool.exe --stdio</c> as a hidden child process: UTF-8 JSON lines on stdin/stdout, its stderr
    /// (tracing, never a secret) forwarded line by line to <see cref="StandardErrorLine"/> without ANSI colour codes.
    /// Closing stdin makes the tool exit; a tool that does not within two seconds is killed. When Visual Studio itself
    /// dies, the pipe breaks and the tool exits on the EOF.
    /// </summary>
    public sealed class ProcessDataToolLauncher : IDataToolLauncher
    {
        private static readonly Regex Ansi = new Regex(@"\x1B\[[0-9;]*[A-Za-z]", RegexOptions.Compiled);
        private readonly Func<string?> _exePath;

        /// <param name="exePath">Resolved on every start (the tool may be built after Visual Studio started).</param>
        public ProcessDataToolLauncher(Func<string?> exePath)
        {
            _exePath = exePath;
        }

        /// <summary>One line of the tool's stderr (any thread).</summary>
        public Action<string>? StandardErrorLine { get; set; }

        public IDataToolConnection Start()
        {
            string? exe = _exePath();
            if (exe is null || !File.Exists(exe))
            {
                throw new DataToolException(DataToolErrorKinds.Unavailable, $"{DataToolLocator.ExeName} was not found (reinstall the Kubuno extension, or build it: cargo build --release -p kubuno-desktop-data-tool).");
            }

            var info = new ProcessStartInfo(exe, "--stdio")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false),
                WorkingDirectory = Path.GetDirectoryName(exe) ?? Environment.CurrentDirectory,
            };
            info.EnvironmentVariables["NO_COLOR"] = "1";

            var process = new Process { StartInfo = info, EnableRaisingEvents = true };
            try
            {
                process.Start();
            }
            catch (Win32Exception exception)
            {
                process.Dispose();
                throw new DataToolException(DataToolErrorKinds.Unavailable, $"{DataToolLocator.ExeName} could not be started: {exception.Message}", exception);
            }

            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    StandardErrorLine?.Invoke(Ansi.Replace(e.Data, string.Empty));
                }
            };
            process.BeginErrorReadLine();
            return new ProcessConnection(process);
        }

        private sealed class ProcessConnection : IDataToolConnection
        {
            private readonly Process _process;
            private readonly StreamWriter _stdin;
            private readonly StreamReader _stdout;
            private bool _shutDown;

            public ProcessConnection(Process process)
            {
                _process = process;
                // .NET Framework has no StandardInputEncoding: write UTF-8 (no BOM) on the raw stream.
                _stdin = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false)) { AutoFlush = false, NewLine = "\n" };
                _stdout = process.StandardOutput;
            }

            public async Task WriteLineAsync(string line)
            {
                await _stdin.WriteAsync(line + "\n").ConfigureAwait(false);
                await _stdin.FlushAsync().ConfigureAwait(false);
            }

            public Task<string?> ReadLineAsync() => DataToolLines.ReadLineOrNullAsync(_stdout);

            public void Shutdown()
            {
                if (_shutDown)
                {
                    return;
                }

                _shutDown = true;
                try
                {
                    _stdin.Dispose();
                }
                catch (Exception)
                {
                    // Broken pipe: the tool is already gone.
                }

                try
                {
                    if (!_process.WaitForExit(2000))
                    {
                        _process.Kill();
                    }
                }
                catch (Exception)
                {
                    // Exited meanwhile.
                }
            }

            public void Dispose()
            {
                Shutdown();
                _process.Dispose();
            }
        }
    }
}
