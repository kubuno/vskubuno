using System.Collections.Generic;

namespace Kubuno.Views.Locating
{
    /// <summary>
    /// Everything <see cref="KubunoViewsLanguageServerLocator"/> needs to ask about the outside world
    /// (the file system, the PATH variable, the fixed dev-build folders), kept as an interface so the
    /// locator's decision logic can be unit-tested against fakes instead of the real machine - the
    /// same split as <c>Kubuno.Rust.Logic.IRustAnalyzerEnvironment</c> in the sibling VSIX
    /// project. The real implementation (<see cref="Infrastructure.RealKubunoViewsLanguageServerEnvironment"/>)
    /// lives in this same library (unlike the Rust one, which lives in the VSIX project) because this
    /// library, unlike <c>Kubuno.Rust.Logic</c>, already depends on nothing that would make a
    /// real file-system implementation untestable - it just is not exercised by the unit tests, which
    /// use a fake instead.
    /// </summary>
    public interface IKubunoViewsLanguageServerEnvironment
    {
        /// <summary>Returns <see langword="true"/> when a file exists at the given full path.</summary>
        bool FileExists(string path);

        /// <summary>
        /// The directories on the PATH environment variable, in search order, already split (no
        /// further parsing needed). Empty when PATH is unset.
        /// </summary>
        IEnumerable<string> PathDirectories { get; }

        /// <summary>
        /// The local dev build output folders to search last, in order
        /// (<c>C:\kubuno-build\desktop-target\debug\</c>, then <c>C:\kubuno-build\agent-views-ls\debug\</c>
        /// in the real implementation - see CLAUDE.md's <c>CARGO_TARGET_DIR</c> convention and
        /// <c>kubuno-views-ls</c>'s own dev build target).
        /// </summary>
        IEnumerable<string> DevBuildDirectories { get; }
    }
}
