using System;
using Kubuno.Views.Designer;
using Kubuno.Views.Designer.DesignSurface;

namespace Kubuno.Desktop.Designer.DesignSurface
{
    /// <summary>
    /// The production <see cref="IDesignSurfaceHostFactory"/>: one <see cref="RustDesignSurfaceHost"/>
    /// per open designer pane. Each pane runs the design surface built against its project's own
    /// <c>kubuno_ui.dll</c> when the <see cref="IDesignSurfaceRuntimeProvider"/> has one (docs/DESIGNER.md
    /// section 15), else the surface bundled with the extension (<c>tools\surface\</c>).
    /// </summary>
    public sealed class RustDesignSurfaceHostFactory : IDesignSurfaceHostFactory
    {
        private readonly string _bundledExePath;
        private readonly string _extraArgs;
        private readonly object? _oleServiceProvider;
        private readonly IDesignSurfaceRuntimeProvider? _runtimeProvider;

        /// <param name="bundledExePath">Full path to the bundled design surface exe (the fallback runtime).</param>
        /// <param name="extraArgs">Extra command-line arguments for every pane's surface process, if any.</param>
        /// <param name="oleServiceProvider">
        /// The VS package's own site-wide <c>IOleServiceProvider</c>, forwarded to every host for the real
        /// <c>IVsFilterKeys2</c> accelerator path - typed <see cref="object"/> on purpose, see
        /// <see cref="VsFilterKeysBridge"/>. <see langword="null"/> outside VS.
        /// </param>
        /// <param name="runtimeProvider">Finds each document's project runtime; <see langword="null"/>: always the bundled surface.</param>
        public RustDesignSurfaceHostFactory(string bundledExePath, string extraArgs = "", object? oleServiceProvider = null, IDesignSurfaceRuntimeProvider? runtimeProvider = null)
        {
            _bundledExePath = bundledExePath ?? throw new ArgumentNullException(nameof(bundledExePath));
            _extraArgs = extraArgs ?? string.Empty;
            _oleServiceProvider = oleServiceProvider;
            _runtimeProvider = runtimeProvider;
        }

        public IDesignSurfaceHost Create(DesignSurfaceDocument? document)
        {
            var lease = document is not null && _runtimeProvider is not null
                ? _runtimeProvider.Acquire(document)
                : new DesignSurfaceRuntimeLease(new FixedDesignSurfaceRuntimeSource(new DesignSurfaceRuntime(_bundledExePath, isProjectRuntime: false, expectedUiDllSha256: null), DesignSurfaceRuntimeState.NotApplicable), null);
            var host = new RustDesignSurfaceHost(lease, _extraArgs, _oleServiceProvider);
            if (document is not null && !string.IsNullOrEmpty(document.Path))
            {
                try { host.BaseDirectory = System.IO.Path.GetDirectoryName(document.Path); }
                catch (ArgumentException) { }
            }
            return host;
        }
    }
}
