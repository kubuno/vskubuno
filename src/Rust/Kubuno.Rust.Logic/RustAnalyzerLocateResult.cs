namespace Kubuno.VisualStudio.Core
{
    /// <summary>
    /// Outcome of <see cref="RustAnalyzerLocator.Locate"/>: either a usable path and where it came
    /// from, or <see cref="RustAnalyzerSource.NotFound"/> with no path, which the caller must turn
    /// into a visible info bar rather than fail silently.
    /// </summary>
    public sealed class RustAnalyzerLocateResult
    {
        private RustAnalyzerLocateResult(string? path, RustAnalyzerSource source)
        {
            Path = path;
            Source = source;
        }

        /// <summary>Full path to <c>rust-analyzer.exe</c>, or <see langword="null"/> when not found.</summary>
        public string? Path { get; }

        public RustAnalyzerSource Source { get; }

        public bool IsFound => Source != RustAnalyzerSource.NotFound;

        public static RustAnalyzerLocateResult Found(string path, RustAnalyzerSource source)
        {
            if (source == RustAnalyzerSource.NotFound)
            {
                throw new System.ArgumentException("A found result cannot use RustAnalyzerSource.NotFound.", nameof(source));
            }

            return new RustAnalyzerLocateResult(path, source);
        }

        public static RustAnalyzerLocateResult NotFound() => new(null, RustAnalyzerSource.NotFound);
    }
}
