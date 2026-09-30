using System;
using Kubuno.Desktop.Designer.DesignSurface;

namespace Kubuno.Desktop.Designer.Selection.Infrastructure
{
    /// <summary>
    /// The real <see cref="IDesignSurfaceSelectionTarget"/>: forwards to
    /// <see cref="RustDesignSurfaceHost.Select"/> (DSG-6/DSG-7, docs/DESIGNER.md §9's <c>kubuno/select</c>) -
    /// see <see cref="IDesignSurfaceSelectionTarget"/>'s own doc comment for why this thin wrapper exists
    /// instead of <see cref="SelectionSyncService"/> depending on <see cref="RustDesignSurfaceHost"/>
    /// directly (that class lives under <c>DesignSurface/</c>, out of this package's scope to edit).
    /// Not unit-tested here for the same reason
    /// <see cref="Editing.Infrastructure.BufferEditApplier"/>'s own doc comment gives every real,
    /// live-collaborator-dependent adapter in this library: it needs an actual running design-surface
    /// process behind <see cref="RustDesignSurfaceHost"/> to observe anything.
    /// </summary>
    public sealed class DesignSurfaceSelectionTarget : IDesignSurfaceSelectionTarget
    {
        private readonly RustDesignSurfaceHost _host;

        public DesignSurfaceSelectionTarget(RustDesignSurfaceHost host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
        }

        public void Select(string? elementId) => _host.Select(elementId);
    }
}
