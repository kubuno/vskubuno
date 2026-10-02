using System.Threading;
using System.Threading.Tasks;

namespace Kubuno.Core.Extensibility
{
    /// <summary>
    /// One product layer of the extension (docs/ARCHITECTURE.md, "Layers (as built)"): Rust (<c>RustLayer</c>), Desktop
    /// (<c>DesktopLayer</c>), Web (<c>WebLayer</c>), Mobile (<c>MobileLayer</c>). The single Kubuno package
    /// (src/Kubuno.VisualStudio) creates one instance of each and <see cref="KubunoLayerHost"/> calls these hooks in
    /// order, layer by layer (Core's own services first, then the layers in the order the package lists them):
    /// <list type="number">
    /// <item><see cref="InitializeAsync"/> - background thread, while the package loads: file probing, service lookups.</item>
    /// <item><see cref="InitializeOnUIThread"/> - UI thread, while the package loads: only what must exist before the
    /// layer's first command, editor or key binding (commands, editor factories, option hosts). Keep it short: this
    /// time is reported as the package's UI-thread load time.</item>
    /// <item><see cref="InitializeOnIdle"/> - UI thread at idle, once the solution (or folder) has finished loading.</item>
    /// <item><see cref="InitializeDeferredAsync"/> - background thread, after the idle step.</item>
    /// <item><see cref="Dispose"/> - UI thread, when the package is disposed (layers in reverse order).</item>
    /// </list>
    /// A hook that throws is logged to the "Kubuno" Output pane and does not stop the other layers. Everything a layer
    /// contributes that Visual Studio discovers from the registry (tool windows, options pages, editor factories,
    /// UI contexts) is still declared by attributes on the package class, and its commands in <c>KubunoCommands.vsct</c>
    /// (the packaging project owns the pkgdef and the command table); the layer only provides the implementations.
    /// </summary>
    public abstract class KubunoLayer
    {
        /// <summary>A short name for logs ("Rust", "Desktop"...).</summary>
        public abstract string Name { get; }

        /// <summary>Background thread, while the package loads.</summary>
        public virtual Task InitializeAsync(KubunoLayerContext context, CancellationToken cancellationToken) => Task.CompletedTask;

        /// <summary>UI thread, while the package loads (after every layer's <see cref="InitializeAsync"/>).</summary>
        public virtual void InitializeOnUIThread(KubunoLayerContext context)
        {
        }

        /// <summary>UI thread at idle priority, once the solution has finished loading.</summary>
        public virtual void InitializeOnIdle(KubunoLayerContext context)
        {
        }

        /// <summary>Background thread, after every layer's <see cref="InitializeOnIdle"/>.</summary>
        public virtual Task InitializeDeferredAsync(KubunoLayerContext context, CancellationToken cancellationToken) => Task.CompletedTask;

        /// <summary>UI thread, when the package is disposed.</summary>
        public virtual void Dispose(KubunoLayerContext context)
        {
        }

        public override string ToString() => Name;
    }
}
