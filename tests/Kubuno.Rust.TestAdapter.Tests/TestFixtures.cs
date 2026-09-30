using System;
using System.IO;

namespace Kubuno.Rust.TestAdapter.Tests
{
    /// <summary>Locates the real captured cargo/libtest fixtures under Fixtures/ (copied next to the test assembly by the .csproj).</summary>
    internal static class TestFixtures
    {
        private static readonly string RootDirectory = System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures");

        public static string Path(params string[] segments)
        {
            var parts = new string[segments.Length + 1];
            parts[0] = RootDirectory;
            Array.Copy(segments, 0, parts, 1, segments.Length);
            return System.IO.Path.Combine(parts);
        }

        public static string ReadAllText(params string[] segments) => File.ReadAllText(Path(segments));

        public static string[] ReadAllLines(params string[] segments) => File.ReadAllLines(Path(segments));
    }
}
