using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Rust.Cargo.Processes;
using Xunit;

namespace Kubuno.Rust.Cargo.Tests.Processes
{
    /// <summary>
    /// Exercises the real <see cref="ProcessRunner"/> against <c>cmd.exe</c> (always present on
    /// Windows, unlike "cargo"), so these run everywhere without depending on the Rust
    /// toolchain: output capture, line streaming, exit codes, environment variables and
    /// cancellation.
    /// </summary>
    public class ProcessRunnerTests
    {
        private static ProcessRunRequest CmdRequest(string command) =>
            new ProcessRunRequest("cmd.exe", $"/d /c {command}");

        [Fact]
        public async Task Captures_standard_output_lines()
        {
            var runner = new ProcessRunner();
            ProcessRunResult result = await runner.RunAsync(CmdRequest("echo hello-kubuno"), onOutput: null, CancellationToken.None);

            Assert.Equal(0, result.ExitCode);
            Assert.True(result.Succeeded);
            Assert.Contains("hello-kubuno", result.StandardOutputLines);
        }

        [Fact]
        public async Task Captures_standard_error_lines_separately()
        {
            var runner = new ProcessRunner();
            ProcessRunResult result = await runner.RunAsync(
                CmdRequest("echo on-stderr 1>&2"), onOutput: null, CancellationToken.None);

            // cmd's `echo` reproduces the trailing space before the redirection operator verbatim.
            Assert.Contains(result.StandardErrorLines, line => line.TrimEnd() == "on-stderr");
            Assert.DoesNotContain(result.StandardOutputLines, line => line.Contains("on-stderr"));
        }

        [Fact]
        public async Task Non_zero_exit_code_is_reported_without_throwing()
        {
            var runner = new ProcessRunner();
            ProcessRunResult result = await runner.RunAsync(CmdRequest("exit 3"), onOutput: null, CancellationToken.None);

            Assert.Equal(3, result.ExitCode);
            Assert.False(result.Succeeded);
        }

        [Fact]
        public async Task Streams_output_lines_to_the_progress_callback_as_they_happen()
        {
            var runner = new ProcessRunner();
            var seen = new List<ProcessOutputLine>();
            var progress = new RecordingProgress<ProcessOutputLine>(seen);

            await runner.RunAsync(CmdRequest("echo one && echo two"), progress, CancellationToken.None);

            // cmd's `echo` reproduces the trailing space before "&&" verbatim.
            Assert.Contains(seen, l => l.Text.TrimEnd() == "one" && !l.IsError);
            Assert.Contains(seen, l => l.Text.TrimEnd() == "two" && !l.IsError);
        }

        [Fact]
        public async Task Environment_variables_are_passed_to_the_child_process()
        {
            var runner = new ProcessRunner();
            var request = CmdRequest("echo %KUBUNO_CARGO_TEST_VAR%");
            request.EnvironmentVariables = new Dictionary<string, string> { ["KUBUNO_CARGO_TEST_VAR"] = "hello-from-test" };

            ProcessRunResult result = await runner.RunAsync(request, onOutput: null, CancellationToken.None);

            Assert.Contains("hello-from-test", result.StandardOutputLines);
        }

        [Fact]
        public async Task Working_directory_is_honoured()
        {
            var runner = new ProcessRunner();
            string tempDirectory = System.IO.Path.GetTempPath().TrimEnd('\\');
            var request = CmdRequest("echo %CD%");
            request.WorkingDirectory = tempDirectory;

            ProcessRunResult result = await runner.RunAsync(request, onOutput: null, CancellationToken.None);

            Assert.Contains(result.StandardOutputLines, line => line.TrimEnd('\\').Equals(tempDirectory, StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task Cancellation_kills_the_process_and_throws_OperationCanceledException()
        {
            var runner = new ProcessRunner();
            using var cts = new CancellationTokenSource();

            // ping a handful of times, ~5s total: long enough to reliably cancel mid-flight.
            Task run = runner.RunAsync(CmdRequest("ping -n 6 127.0.0.1 >nul"), onOutput: null, cts.Token);

            cts.CancelAfter(TimeSpan.FromMilliseconds(300));

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
            stopwatch.Stop();

            // If the kill didn't work, this would take the full ~5s of the ping instead.
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(4), $"Cancellation took too long: {stopwatch.Elapsed}");
        }

        private sealed class RecordingProgress<T> : IProgress<T>
        {
            private readonly List<T> _sink;

            public RecordingProgress(List<T> sink) => _sink = sink;

            public void Report(T value)
            {
                lock (_sink)
                {
                    _sink.Add(value);
                }
            }
        }
    }
}
