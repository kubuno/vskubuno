using System;

namespace Kubuno.VisualStudio.Designer.DesignSurface
{
    /// <summary>
    /// The production <see cref="IDesignSurfaceHostFactory"/>: one <see cref="RustDesignSurfaceHost"/>
    /// per open designer pane, all launching the SAME design surface exe (captured here rather than
    /// resolved per pane - resolving it is a locator concern, deliberately left to the VSIX integration
    /// step per <c>IDesignSurfaceHostFactory</c>'s own doc ("from its own package/component
    /// initialization" - see INTEGRATION.md).
    /// </summary>
    public sealed class RustDesignSurfaceHostFactory : IDesignSurfaceHostFactory
    {
        private readonly string _exePath;
        private readonly string _extraArgs;
        private readonly object? _oleServiceProvider;

        /// <param name="exePath">Full path to the design surface exe - see <see cref="RustDesignSurfaceHost"/>'s own doc for what runs there today.</param>
        /// <param name="extraArgs">Extra command-line arguments for every pane's surface process, if any.</param>
        /// <param name="oleServiceProvider">
        /// The VS package's own site-wide <c>IOleServiceProvider</c> (obtained once, e.g. in
        /// <c>KubunoPackage.InitializeAsync</c> - <c>SVsFilterKeys</c> is a package-level service, not a
        /// per-document one), forwarded to every created host's own constructor for the real
        /// <c>IVsFilterKeys2</c> accelerator path - see <see cref="RustDesignSurfaceHost"/>'s own doc for
        /// what runs there, and <see cref="VsFilterKeysBridge"/>'s own doc for why this parameter is
        /// deliberately typed <see cref="object"/> rather than the real VS interop type (a real,
        /// live-observed startup crash outside VS otherwise). <see langword="null"/> outside VS (this
        /// library's own tests, the updated spike).
        /// </param>
        public RustDesignSurfaceHostFactory(string exePath, string extraArgs = "", object? oleServiceProvider = null)
        {
            _exePath = exePath ?? throw new ArgumentNullException(nameof(exePath));
            _extraArgs = extraArgs ?? string.Empty;
            _oleServiceProvider = oleServiceProvider;
        }

        public IDesignSurfaceHost Create() => new RustDesignSurfaceHost(_exePath, _extraArgs, _oleServiceProvider);
    }
}
