using System.Diagnostics;
using System.Text;

namespace Kubuno.Launch
{
    /// <summary>
    /// The real <see cref="IProcessRunner"/>, backed by <see cref="Process"/>. Not used by
    /// unit tests (which inject a fake); used by integration tests and, later, by the VSIX.
    /// </summary>
    public sealed class SystemProcessRunner : IProcessRunner
    {
        public ProcessRunResult Run(string fileName, string arguments, string? workingDirectory = null)
        {
            var startInfo = new ProcessStartInfo(fileName, arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            if (!string.IsNullOrEmpty(workingDirectory))
            {
                startInfo.WorkingDirectory = workingDirectory;
            }

            using var process = new Process { StartInfo = startInfo };

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    stdout.AppendLine(e.Data);
                }
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    stderr.AppendLine(e.Data);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();

            return new ProcessRunResult(process.ExitCode, stdout.ToString(), stderr.ToString());
        }
    }
}
