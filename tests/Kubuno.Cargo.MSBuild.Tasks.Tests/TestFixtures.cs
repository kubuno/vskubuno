namespace Kubuno.Cargo.MSBuild.Tasks.Tests
{
    /// <summary>Locates the real captured cargo build-message fixtures under Fixtures/, mirroring Kubuno.Cargo.Tests' own helper.</summary>
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

        public static string[] ReadAllLines(params string[] segments) => File.ReadAllLines(Path(segments));
    }
}
