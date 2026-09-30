using System.Collections.Generic;

namespace Kubuno.Rust.Logic
{
    /// <summary>
    /// Everything <see cref="RustAnalyzerLocator"/> needs to ask about the outside world (the file
    /// system, the PATH variable, and running <c>rustup</c>), kept as an interface so the locator's
    /// decision logic can be unit-tested against fakes instead of the real machine. The real
    /// implementation lives in the VSIX project (Kubuno.VisualStudio), which is the only place
    /// allowed to touch the file system or start a process.
    /// </summary>
    public interface IRustAnalyzerEnvironment
    {
        /// <summary>Returns <see langword="true"/> when a file exists at the given full path.</summary>
        bool FileExists(string path);

        /// <summary>
        /// The user's home directory (<c>%USERPROFILE%</c> on Windows), used to build the default
        /// <c>.cargo\bin\rust-analyzer.exe</c> candidate. <see langword="null"/>/empty means unknown.
        /// </summary>
        string? UserProfileDirectory { get; }

        /// <summary>
        /// The directories on the PATH environment variable, in search order, already split (no
        /// further parsing needed). Empty when PATH is unset.
        /// </summary>
        IEnumerable<string> PathDirectories { get; }

        /// <summary>
        /// Runs <c>rustup which rust-analyzer</c> and returns its trimmed stdout path on success, or
        /// <see langword="null"/> if rustup is missing, the component isn't installed, or the
        /// process fails for any reason. Must never throw.
        /// </summary>
        string? RunRustupWhich();
    }
}
