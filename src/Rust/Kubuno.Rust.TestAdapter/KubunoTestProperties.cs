using Microsoft.VisualStudio.TestPlatform.ObjectModel;

namespace Kubuno.TestAdapter
{
    /// <summary>
    /// Custom <see cref="TestProperty"/>s this adapter stashes on every <see cref="TestCase"/> it
    /// produces during discovery, so execution never has to re-derive them from
    /// <see cref="TestCase.Source"/> (the Cargo.toml manifest path - see Containers/ for why
    /// that, and not the built test executable, is the container/source) or re-run
    /// `cargo metadata`. All hidden: they are adapter plumbing, not something Test Explorer's
    /// trait UI should show the developer.
    /// </summary>
    public static class KubunoTestProperties
    {
        /// <summary>Full path of the built test executable that contains this test (see Discovery/CargoTestBuild.cs).</summary>
        public static readonly TestProperty ExecutablePath = TestProperty.Register(
            "Kubuno.TestAdapter.ExecutablePath", "Executable Path", typeof(string), TestPropertyAttributes.Hidden, typeof(TestCase));

        /// <summary>
        /// The exact name libtest itself uses for this test (e.g. "tests::greet_includes_the_name"),
        /// as reported by `&lt;exe&gt; --list --format terse`. Passed back to the exe with `--exact`
        /// at run time; kept separate from <see cref="TestCase.FullyQualifiedName"/> (which is
        /// prefixed with the crate/target name - see Discovery/CargoTestCaseFactory.cs) so
        /// execution never has to strip that prefix back off.
        /// </summary>
        public static readonly TestProperty LibtestName = TestProperty.Register(
            "Kubuno.TestAdapter.LibtestName", "Libtest Name", typeof(string), TestPropertyAttributes.Hidden, typeof(TestCase));

        /// <summary>The debuggee/test-process working directory (the owning package's root).</summary>
        public static readonly TestProperty WorkingDirectory = TestProperty.Register(
            "Kubuno.TestAdapter.WorkingDirectory", "Working Directory", typeof(string), TestPropertyAttributes.Hidden, typeof(TestCase));

        /// <summary>The resolved Cargo target directory (`&lt;target-dir&gt;`, without the profile/deps suffix) - needed to debug-launch (RustDebugEnvironment PATH additions).</summary>
        public static readonly TestProperty TargetDirectory = TestProperty.Register(
            "Kubuno.TestAdapter.TargetDirectory", "Cargo Target Directory", typeof(string), TestPropertyAttributes.Hidden, typeof(TestCase));
    }
}
