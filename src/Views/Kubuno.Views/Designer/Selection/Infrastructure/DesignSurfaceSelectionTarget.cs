using System;
using Kubuno.Views.Designer.DesignSurface;

namespace Kubuno.Views.Designer.Selection.Infrastructure
{
    /// <summary>
    /// The real <see cref="IDesignSurfaceSelectionTarget"/>: forwards to
    /// <see cref="IProtocolDesignSurfaceHost.Select"/> (DSG-6/DSG-7, docs/DESIGNER.md §9's <c>kubuno/select</c>) -
    /// see <see cref="IDesignSurfaceSelectionTarget"/>'s own doc comment for why this thin wrapper exists
    /// instead of <see cref="SelectionSyncService"/> depending on <c>RustDesignSurfaceHost</c>
    /// directly (that class lives under <c>DesignSurface/</c>, out of this package's scope to edit).
    /// Not unit-tested here for the same reason
    /// <see cref="Editing.Infrastructure.BufferEditApplier"/>'s own doc comment gives every real,
    /// live-collaborator-dependent adapter in this library: it needs an actual running design-surface
    /// process behind <c>RustDesignSurfaceHost</c> to observe anything.
    /// </summary>
    public sealed class DesignSurfaceSelectionTarget : IDesignSurfaceSelectionTarget
    {
        private readonly IProtocolDesignSurfaceHost _host;

        public DesignSurfaceSelectionTarget(IProtocolDesignSurfaceHost host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
        }

        public void Select(string? elementId) => _host.Select(elementId);
    }
}
