using System;

namespace Kubuno.Desktop.Views.Locating
{
    /// <summary>
    /// Outcome of <see cref="KubunoViewsLanguageServerLocator.Locate"/>: either a usable path and
    /// where it came from, or <see cref="KubunoViewsLanguageServerSource.NotFound"/> with no path,
    /// which the caller must turn into a visible info bar rather than fail silently.
    /// </summary>
    public sealed class KubunoViewsLanguageServerLocateResult
    {
        private KubunoViewsLanguageServerLocateResult(string? path, KubunoViewsLanguageServerSource source)
        {
            Path = path;
            Source = source;
        }

        /// <summary>Full path to <c>kubuno-views-ls.exe</c>, or <see langword="null"/> when not found.</summary>
        public string? Path { get; }

        public KubunoViewsLanguageServerSource Source { get; }

        public bool IsFound => Source != KubunoViewsLanguageServerSource.NotFound;

        public static KubunoViewsLanguageServerLocateResult Found(string path, KubunoViewsLanguageServerSource source)
        {
            if (source == KubunoViewsLanguageServerSource.NotFound)
            {
                throw new ArgumentException("A found result cannot use KubunoViewsLanguageServerSource.NotFound.", nameof(source));
            }

            return new KubunoViewsLanguageServerLocateResult(path, source);
        }

        public static KubunoViewsLanguageServerLocateResult NotFound() => new(null, KubunoViewsLanguageServerSource.NotFound);
    }
}
