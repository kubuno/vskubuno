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
        private readonly Func<int, IntPtr, IntPtr, bool>? _vsFilterKeys;

        /// <param name="exePath">Full path to the design surface exe - see <see cref="RustDesignSurfaceHost"/>'s own doc for what runs there today.</param>
        /// <param name="extraArgs">Extra command-line arguments for every pane's surface process, if any.</param>
        /// <param name="vsFilterKeys">Forwarded to every created host's <see cref="RustDesignSurfaceHost.VsFilterKeys"/> - see that property's own doc.</param>
        public RustDesignSurfaceHostFactory(string exePath, string extraArgs = "", Func<int, IntPtr, IntPtr, bool>? vsFilterKeys = null)
        {
            _exePath = exePath ?? throw new ArgumentNullException(nameof(exePath));
            _extraArgs = extraArgs ?? string.Empty;
            _vsFilterKeys = vsFilterKeys;
        }

        public IDesignSurfaceHost Create()
        {
            var host = new RustDesignSurfaceHost(_exePath, _extraArgs);
            if (_vsFilterKeys != null)
            {
                host.VsFilterKeys = _vsFilterKeys;
            }

            return host;
        }
    }
}
