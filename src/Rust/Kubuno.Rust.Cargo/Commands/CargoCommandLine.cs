using System.Collections.Generic;

namespace Kubuno.Rust.Cargo.Commands
{
    /// <summary>
    /// A ready-to-launch command line: the executable file name, the argv-style argument
    /// list (handy for assertions and for APIs that want individual arguments), and the
    /// single correctly Windows-quoted argument string built from it (what
    /// <see cref="System.Diagnostics.ProcessStartInfo.Arguments"/> expects).
    /// </summary>
    public sealed class CargoCommandLine
    {
        public CargoCommandLine(string fileName, IReadOnlyList<string> argumentList)
        {
            FileName = fileName;
            ArgumentList = argumentList;
            Arguments = WindowsCommandLineQuoting.Join(argumentList);
        }

        public string FileName { get; }

        public IReadOnlyList<string> ArgumentList { get; }

        /// <summary>The argument list joined into a single, correctly quoted command-line string.</summary>
        public string Arguments { get; }

        public override string ToString() => $"{FileName} {Arguments}";
    }
}
