using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Kubuno.Rust.TestAdapter.Discovery
{
    /// <summary>
    /// Best-effort, "cheap" `fn &lt;name&gt;` lookup for a `TestCase.CodeFilePath`/`LineNumber`:
    /// a single-file line scan of the test binary's own entry source file
    /// (<see cref="CargoTestBinary.SourcePath"/> - lib.rs/main.rs/tests/basic.rs), not a real
    /// module-path resolution. Finds the common case (a `#[cfg(test)] mod tests { ... }` block
    /// inline in that same file, which is how the vast majority of Rust unit/integration tests
    /// are written); a test declared via `mod foo;` into a separate file is not found - callers
    /// treat a null result as "no navigation info", not an error.
    /// </summary>
    public static class TestSourceLocator
    {
        // Matches a (possibly `pub`/`pub(crate)`/`async`) `fn <name>` declaration. Deliberately
        // simple: no attempt to skip over doc comments, nested braces, or distinguish a real
        // declaration from one inside a string/doc example - a false-positive match only ever
        // costs a slightly-wrong navigation line, never a build/discovery failure.
        private static readonly Regex FnDeclaration = new Regex(
            @"^\s*(?:pub(?:\([^)]*\))?\s+)?(?:async\s+)?fn\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*[<(]",
            RegexOptions.Compiled);

        /// <summary>
        /// Looks for <paramref name="libtestName"/>'s bare function name (the segment after its
        /// last "::", e.g. "greet_includes_the_name" out of "tests::greet_includes_the_name") in
        /// <paramref name="sourceFilePath"/>. Returns null when the file can't be read or no
        /// matching `fn` is found - callers should treat that as "leave CodeFilePath/LineNumber
        /// unset", not as a discovery failure.
        /// </summary>
        public static TestSourceLocation? TryLocate(string sourceFilePath, string libtestName)
        {
            if (string.IsNullOrEmpty(sourceFilePath) || string.IsNullOrEmpty(libtestName))
            {
                return null;
            }

            string[] lines;
            try
            {
                lines = File.ReadAllLines(sourceFilePath);
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }

            string bareName = LastSegment(libtestName);

            for (int i = 0; i < lines.Length; i++)
            {
                Match match = FnDeclaration.Match(lines[i]);
                if (match.Success && string.Equals(match.Groups["name"].Value, bareName, StringComparison.Ordinal))
                {
                    return new TestSourceLocation(sourceFilePath, i + 1); // libtest/VS line numbers are 1-based.
                }
            }

            return null;
        }

        private static string LastSegment(string libtestName)
        {
            int index = libtestName.LastIndexOf("::", StringComparison.Ordinal);
            return index < 0 ? libtestName : libtestName.Substring(index + 2);
        }
    }

    /// <summary>A file/line pair found by <see cref="TestSourceLocator.TryLocate"/>.</summary>
    public readonly struct TestSourceLocation
    {
        public TestSourceLocation(string filePath, int lineNumber)
        {
            FilePath = filePath;
            LineNumber = lineNumber;
        }

        public string FilePath { get; }

        public int LineNumber { get; }
    }
}
