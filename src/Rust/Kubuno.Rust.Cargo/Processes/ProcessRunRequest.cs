using System;
using System.Collections.Generic;

namespace Kubuno.Rust.Cargo.Processes
{
    /// <summary>Everything needed to launch one process: no Cargo-specific knowledge here.</summary>
    public sealed class ProcessRunRequest
    {
        public ProcessRunRequest(string fileName, string arguments = "")
        {
            FileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
            Arguments = arguments ?? string.Empty;
        }

        /// <summary>Executable to launch, e.g. "cargo".</summary>
        public string FileName { get; }

        /// <summary>Pre-quoted argument string (see <see cref="Commands.CargoCommandLine"/>).</summary>
        public string Arguments { get; }

        public string? WorkingDirectory { get; set; }

        /// <summary>
        /// Environment variables to set (or override) on top of the current process's own
        /// environment, which is inherited by default. Use this to pass e.g. <c>CARGO_TARGET_DIR</c>.
        /// </summary>
        public IReadOnlyDictionary<string, string>? EnvironmentVariables { get; set; }
    }
}
